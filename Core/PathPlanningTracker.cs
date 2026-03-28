using ArtaleAI.Models.Config;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Models.Map;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Services;
using ArtaleAI.Utils;
using System.Diagnostics;
using System.Linq;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;
using Timer = System.Threading.Timer;

namespace ArtaleAI.Core
{
    /// <summary>
    /// 路徑規劃追蹤器 - 負責持續檢測玩家位置和路徑狀態
    /// </summary>
    public partial class PathPlanningTracker : IDisposable
    {
        // 導航圖相關欄位
        private NavigationGraph? _navGraph;

        // Readonly 欄位
        private readonly GameVisionCore _gameVision;
        private readonly Random _random = new Random();
        private readonly object _randomLock = new object(); // 執行緒安全鎖
        private readonly List<MinimapTrackingResult> _trackingHistory;
        private readonly object _historyLock = new object(); // 執行緒安全鎖

        // 可變欄位 (volatile 確保多執行緒可見性)
        private INavigationStateMachine? _fsm;

        /// <summary>
        /// 取得當前路徑規劃狀態
        /// </summary>
        public PathPlanningState? CurrentPathState { get; private set; }

        public event Action<MinimapTrackingResult>? OnTrackingUpdated;
        public event Action<PathPlanningState>? OnPathStateChanged;
        public event Action<SdPointF>? OnWaypointReached;

        public NavigationGraph? NavGraph => _navGraph;

        /// <summary>
        /// 🛡️ SSOT 感知介面：判定當前角色是否已抵達目標 Hitbox。
        /// 物理執行層應主動拉取 (Pull) 此判定，而非等待推播。
        /// </summary>
        public bool IsPlayerAtTarget()
        {
            var state = CurrentPathState;
            if (state == null || state.IsPathCompleted || state.CurrentWaypointIndex >= state.PlannedPathNodes.Count)
                return false;

            var playerPos = state.CurrentPlayerPosition;
            if (!playerPos.HasValue) return false;

            var targetNode = _navGraph?.GetNode(state.PlannedPathNodes[state.CurrentWaypointIndex]);
            if (targetNode?.Hitbox == null) return false;

            return targetNode.Hitbox.Value.Contains(playerPos.Value.X, playerPos.Value.Y);
        }

        public NavigationNode? CurrentTarget
        {
            get
            {
                if (CurrentPathState != null &&
                    CurrentPathState.CurrentWaypointIndex >= 0 &&
                    CurrentPathState.CurrentWaypointIndex < CurrentPathState.PlannedPath.Count)
                {
                    var targetPoint = CurrentPathState.PlannedPath[CurrentPathState.CurrentWaypointIndex];
                    return _navGraph?.GetAllNodes().FirstOrDefault(n =>
                        n.Type == NavigationNodeType.Platform &&
                        Math.Abs(n.Position.X - targetPoint.X) < 1.0f &&
                        Math.Abs(n.Position.Y - targetPoint.Y) < 1.0f);
                }
                return null;
            }
        }

        public NavigationEdge? CurrentNavigationEdge
        {
            get
            {
                if (_navGraph == null || CurrentPathState == null || CurrentPathState.PlannedPath.Count == 0) return null;

                var path = CurrentPathState.PlannedPath;
                int currentIndex = CurrentPathState.CurrentWaypointIndex;

                if (currentIndex <= 0 || currentIndex >= path.Count) return null;
                
                var prevTarget = path[currentIndex - 1];
                var currentTarget = path[currentIndex];

                // 關鍵：CurrentPathState.PlannedPath[0] 在「救援重定位」時可能是角色當前座標，
                // 距離最近節點中心 > 15px；若仍用固定半徑找 nearest node，會導致 edge 解析失敗而回傳 null。
                // 因此優先使用 PlannedPathNodes（nodeId）來取得邊，讓解析語意與 A* 重規劃一致（SSOT）。
                if (CurrentPathState.PlannedPathNodes != null &&
                    CurrentPathState.PlannedPathNodes.Count > currentIndex &&
                    CurrentPathState.PlannedPathNodes.Count > currentIndex - 1)
                {
                    var prevNodeId = CurrentPathState.PlannedPathNodes[currentIndex - 1];
                    var currNodeId = CurrentPathState.PlannedPathNodes[currentIndex];

                    if (!string.IsNullOrWhiteSpace(prevNodeId) && !string.IsNullOrWhiteSpace(currNodeId))
                    {
                        var edge = _navGraph.GetEdge(prevNodeId, currNodeId);
                        if (edge != null) return edge;

                        Logger.Warning($"[路徑追蹤] 找不到實體邊：{prevNodeId} -> {currNodeId}，CurrentNavigationEdge 回傳 null。");
                    }
                }

                // fallback：仍保留基於座標的解析，但改用較寬鬆的半徑以降低救援後漂移導致的 edge 缺失。
                // 這是保護性機制，應該在 PlannedPathNodes 未填入時才會觸發。
                var currNode = _navGraph.FindNearestNode(new System.Drawing.PointF(currentTarget.X, currentTarget.Y), 60.0f);
                var prevNode = _navGraph.FindNearestNode(new System.Drawing.PointF(prevTarget.X, prevTarget.Y), 60.0f);

                if (prevNode != null && currNode != null)
                {
                    var edge = _navGraph.GetEdge(prevNode.Id, currNode.Id);
                    if (edge != null) return edge;

                    Logger.Warning($"[路徑追蹤] 找不到實體邊：{prevNode.Id} -> {currNode.Id}，拒絕產生 rescue_from 假邊。");
                }
                else
                {
                    Logger.Warning("[路徑追蹤] 無法解析當前/前一節點，CurrentNavigationEdge 回傳 null。");
                }

                return null;
            }
        }

        public PathPlanningTracker(GameVisionCore gameVision)
        {
            _gameVision = gameVision ?? throw new ArgumentNullException(nameof(gameVision));
            _trackingHistory = new List<MinimapTrackingResult>();
        }

        public void BindStateMachine(INavigationStateMachine fsm)
        {
            if (_fsm != null)
            {
                _fsm.OnStateChanged -= OnFsmStateChanged;
            }
            _fsm = fsm;
            _fsm.OnStateChanged += OnFsmStateChanged;
            Logger.Info("[路徑追蹤] 已成功綁定 FSM，開啟事件驅動進度模式。");
        }


        private void OnFsmStateChanged(NavigationState oldState, NavigationState newState)
        {
            if (newState == NavigationState.Reached_Waypoint)
            {
                lock (_randomLock) 
                {
                    if (CurrentPathState == null || CurrentPathState.PlannedPath.Count == 0) return;

                    Logger.Info($"[路徑追蹤] FSM 驗收成功，正式推進進度。");

                    var reachedIndex = CurrentPathState.CurrentWaypointIndex;
                    if (reachedIndex >= 0 && reachedIndex < CurrentPathState.PlannedPath.Count)
                        OnWaypointReached?.Invoke(CurrentPathState.PlannedPath[reachedIndex]);

                    CurrentPathState.CurrentWaypointIndex++;

                    if (CurrentPathState.CurrentWaypointIndex >= CurrentPathState.PlannedPath.Count)
                    {
                        Logger.Info("[路徑追蹤] 已完成整段路徑，觸發隨機巡邏規劃。");
                        SelectRandomPhysicalTarget();
                    }
                    
                    OnPathStateChanged?.Invoke(CurrentPathState);
                }
            }
        }


        public void LoadMap(MapData mapData)
        {
            if (mapData == null) return;

            if (mapData.Nodes == null || mapData.Nodes.Count == 0)
            {
                // A2: 完全不支援舊 MapData（強制升級到 NavGraph 結構）
                Logger.Error("[路徑追蹤] 不相容的 MapData：缺少 Nodes。已禁止進行 LegacyMapMigrator 遷移（A2）。");
                CurrentPathState = new PathPlanningState
                {
                    IsPathCompleted = true
                };
                return;
            }

            _navGraph = NavigationGraph.FromMapData(mapData);
            var graph = _navGraph;
            Logger.Info($"[路徑追蹤] 已載入導航圖：{graph.NodeCount} 個節點，{graph.EdgeCount} 條邊");

            var platformNodesCount = graph.GetAllNodes().Count(n => n.Type == NavigationNodeType.Platform);
            if (platformNodesCount >= 2)
            {
                CurrentPathState = new PathPlanningState
                {
                    PlannedPath = new List<SdPointF>(),
                    PlannedPathNodes = new List<string>(),
                    CurrentWaypointIndex = 0,
                    IsPathCompleted = false
                };
            }
        }


        public void ProcessTrackingResult(MinimapTrackingResult? result)
        {
            if (result == null) return;
            UpdateTrackingHistory(result);
            UpdatePathState(result);
            OnTrackingUpdated?.Invoke(result);
        }

        private void UpdateTrackingHistory(MinimapTrackingResult result)
        {
            lock (_historyLock)
            {
                _trackingHistory.Add(result);
                var maxHistory = AppConfig.Instance.Navigation.MaxTrackingHistory;
                if (_trackingHistory.Count > maxHistory) _trackingHistory.RemoveAt(0);
            }
        }

        /// <summary>
        /// 與 MainForm 一致：臨時目標優先，否則為當前路徑點索引所指座標。
        /// </summary>
        private static void RecalculateDistanceToNextWaypoint(PathPlanningState state, SdPointF playerPos)
        {
            var targetOpt = state.TemporaryTarget ?? state.NextWaypoint;
            if (!targetOpt.HasValue) return;
            var t = targetOpt.Value;
            float dx = playerPos.X - t.X;
            float dy = playerPos.Y - t.Y;
            state.DistanceToNextWaypoint = Math.Sqrt(dx * dx + dy * dy);
        }

        private void UpdatePathState(MinimapTrackingResult trackingResult)
        {
            var state = CurrentPathState;
            if (state == null) return;

            var playerPos = trackingResult.PlayerPosition;
            if (!playerPos.HasValue || playerPos.Value == SdPointF.Empty) return;

            // SSOT：座標快照必須與追蹤結果一致；若僅因 IsPathCompleted 略過更新，GetCurrentPosition 會與 UI/實際角色脫節（移動迴圈會基於凍結座標無限長按方向鍵）。
            state.CurrentPlayerPosition = playerPos.Value;

            if (state.IsPathCompleted) return;

            if (state.PlannedPath.Count == 0)
            {
                SelectRandomPhysicalTarget();
                if (state.PlannedPath.Count > 0)
                {
                    RecalculateDistanceToNextWaypoint(state, playerPos.Value);
                    OnPathStateChanged?.Invoke(state);
                }
                return;
            }

            RecalculateDistanceToNextWaypoint(state, playerPos.Value);

            // 僅更新快照事件，不再主動發送 NotifyTargetReached，由 FSM/Executor 輪詢 IsPlayerAtTarget。
            OnPathStateChanged?.Invoke(state);
        }

        private void SelectRandomPhysicalTarget()
        {
            if (CurrentPathState == null || _navGraph == null) return;

            var playerPos = CurrentPathState.CurrentPlayerPosition ?? new SdPointF(0, 0);
            var startNode = _navGraph.FindNearestNode(playerPos, 100f);

            if (startNode == null)
            {
                CurrentPathState.IsPathCompleted = true;
                return;
            }

            var candidateNodes = _navGraph.GetAllNodes()
                .Where(n => n.Type == NavigationNodeType.Platform && n.Id != startNode.Id)
                .ToList();

            if (candidateNodes.Count == 0)
            {
                CurrentPathState.IsPathCompleted = true;
                return;
            }

            ApplyRandomSelectionAndPathfind(startNode, candidateNodes);
        }

        private void ApplyRandomSelectionAndPathfind(NavigationNode startNode, List<NavigationNode> candidateNodes)
        {
            if (CurrentPathState == null || _navGraph == null || candidateNodes.Count == 0) return;

            lock (_randomLock)
            {
                var reachablePaths = new List<(NavigationNode Node, Core.Domain.Navigation.NavigationPath Path)>();

                foreach (var goalNode in candidateNodes)
                {
                    var pathObj = _navGraph.FindPath(startNode.Id, goalNode.Id);
                    if (pathObj != null && pathObj.Edges.Count > 0)
                    {
                        // 跳過含有未實作動作的路徑（JumpDown 尚未支援）
                        bool hasUnsupportedAction = pathObj.Edges.Any(e =>
                            e.ActionType == NavigationActionType.JumpDown);
                        if (hasUnsupportedAction) continue;

                        reachablePaths.Add((goalNode, pathObj));
                    }
                }

                if (reachablePaths.Count > 0)
                {
                    int selectedIdx = _random.Next(reachablePaths.Count);
                    var (goalNode, pathObj) = reachablePaths[selectedIdx];

                    var newPlannedPath = new List<SdPointF> { new SdPointF(startNode.Position.X, startNode.Position.Y) };
                    var newNodeIds = new List<string> { startNode.Id };

                    foreach (var edge in pathObj.Edges)
                    {
                        var toNode = _navGraph.GetNode(edge.ToNodeId);
                        if (toNode != null)
                        {
                            newPlannedPath.Add(new SdPointF(toNode.Position.X, toNode.Position.Y));
                            newNodeIds.Add(toNode.Id);
                        }
                    }

                    // 💡 [修正] 徹底移除路徑簡化機制，落實導航原始主義
                    // 救援流程可能先設 IsPathCompleted=true 再進入隨機選路；此處必須清旗標，否則 UpdatePathState 不再寫入 CurrentPlayerPosition，移動層會永遠讀到過期座標。
                    CurrentPathState.IsPathCompleted = false;
                    CurrentPathState.PlannedPath = newPlannedPath;
                    CurrentPathState.PlannedPathNodes = newNodeIds;

                    CurrentPathState.CurrentWaypointIndex = 1;

                    Logger.Info($"[路徑規劃] 找到路線！{startNode.Id} -> {goalNode.Id}，保留所有原始節點共 {CurrentPathState.PlannedPath.Count} 個");
                    return;
                }

                CurrentPathState.IsPathCompleted = true;
            }
        }

        public List<MinimapTrackingResult> GetTrackingHistory()
        {
            lock (_historyLock) return new List<MinimapTrackingResult>(_trackingHistory);
        }

        public void Dispose()
        {
            // 不需要 StopTracking，因為已經廢除主動追蹤迴圈
            if (_fsm != null)
            {
                _fsm.OnStateChanged -= OnFsmStateChanged;
                _fsm = null;
            }
            lock (_historyLock) _trackingHistory.Clear();
            CurrentPathState = null;
        }

        public bool TryRescuePath()
        {
            if (CurrentPathState == null || _navGraph == null || CurrentPathState.PlannedPath == null || CurrentPathState.PlannedPath.Count == 0) return false;

            var currentPosNullable = CurrentPathState.CurrentPlayerPosition;
            if (!currentPosNullable.HasValue) return false;
            var currentPos = currentPosNullable.Value;

            // 取得原始終點節點 ID（路徑的最後一個節點）
            string originalTargetNodeId = CurrentPathState.PlannedPathNodes.Last();
            var originalTargetNode = _navGraph.GetNode(originalTargetNodeId);
            if (originalTargetNode == null) return false;

            Logger.Info($"[導航救援] 啟動全域重定位流程。當前位置: {currentPos:F1}，原始目標: {originalTargetNode.Id}");

            // 🛡️ 物理緩衝：確保清空所有舊有的按鍵指令，防止重新規劃後因慣性衝過頭
            _fsm?.CancelNavigation("啟動全域重定位救援");

            // 🔍 實作「全域節點檢索」：設定搜尋半徑為 150.0 像素，不再受限於 5px 高度落差
            var nearestNode = _navGraph.FindNearestNode(currentPos, 150.0f);

            if (nearestNode == null)
            {
                Logger.Warning("[導航救援] 角色不再路徑檔的節點半徑 (150px) 內。無法自動恢復導航。");
                return false;
            }

            float dist = (float)Math.Sqrt(Math.Pow(currentPos.X - nearestNode.Position.X, 2) + Math.Pow(currentPos.Y - nearestNode.Position.Y, 2));
            Logger.Info($"[導航救援] 找到最近補給節點 {nearestNode.Id} (距離:{dist:F1}px)，重新啟動 A* 規劃...");

            // 🚀 執行 A* 重規劃：從最近節點規劃至原始終點
            var pathObj = _navGraph.FindPath(nearestNode.Id, originalTargetNodeId);
            
            if (pathObj == null || pathObj.Edges.Count == 0 && nearestNode.Id != originalTargetNodeId)
            {
                Logger.Error($"[導航救援] A* 無法從新起點 {nearestNode.Id} 規劃至終點 {originalTargetNodeId}。");
                return false;
            }

            // 構建全新路徑狀態
            var updatedPath = new List<SdPointF> { currentPos };
            var updatedNodes = new List<string> { nearestNode.Id }; // 最近節點作為新起點軌跡

            if (pathObj != null)
            {
                foreach (var edge in pathObj.Edges)
                {
                    var toNode = _navGraph.GetNode(edge.ToNodeId);
                    if (toNode != null)
                    {
                        updatedPath.Add(toNode.Position);
                        updatedNodes.Add(toNode.Id);
                    }
                }
            }

            // 更新 CurrentPathState 並觸發無縫重啟
            lock (_randomLock)
            {
                CurrentPathState.PlannedPath = updatedPath;
                CurrentPathState.PlannedPathNodes = updatedNodes;

                // 🛡️ [修正] 邊界保護：若救援後已在目標點，直接標記完成並啟動下一目標。
                if (updatedPath.Count <= 1)
                {
                    Logger.Info("[導航救援] 重定位完成：角色已位於目標節點。");
                    CurrentPathState.IsPathCompleted = true;
                    SelectRandomPhysicalTarget();
                }
                else
                {
                    CurrentPathState.IsPathCompleted = false;
                    CurrentPathState.CurrentWaypointIndex = 1; // 從第一個路徑點開始執行
                }
            }

            Logger.Info($"[導航救援] 重定位成功！新路徑包含 {CurrentPathState.PlannedPath.Count} 個節點，目標重新鎖定。");
            OnPathStateChanged?.Invoke(CurrentPathState);
            return true;
        }
    }
}
