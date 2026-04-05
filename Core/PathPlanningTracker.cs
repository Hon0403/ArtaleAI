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
    /// <summary>依小地圖追蹤更新路徑狀態、邊解析與救援重規劃。</summary>
    public partial class PathPlanningTracker : IDisposable
    {
        private NavigationGraph? _navGraph;

        /// <summary>來自 <see cref="MapData.Ropes"/>，供小地圖視窗畫垂直繩線（與節點 Type 是否為 Rope 無關）。</summary>
        private readonly List<(float X, float TopY, float BottomY)> _mapRopeSegmentsForVisualization = new();

        private readonly GameVisionCore _gameVision;
        private readonly Random _random = new Random();
        private readonly object _randomLock = new object();
        private INavigationStateMachine? _fsm;
        private SdPointF? _lastPosition;
        private DateTime _lastMovementUtc = DateTime.UtcNow;

        /// <summary>目前規劃中的路徑與索引。</summary>
        public PathPlanningState? CurrentPathState { get; private set; }

        public event Action<MinimapTrackingResult>? OnTrackingUpdated;
        public event Action<PathPlanningState>? OnPathStateChanged;
        public event Action<SdPointF>? OnWaypointReached;

        public NavigationGraph? NavGraph => _navGraph;

        /// <summary>地圖檔 <c>Ropes</c> 陣列複本（小地圖相對座標），供可視化；導航拓撲仍由 <see cref="NavigationGraph"/> 決定。</summary>
        public IReadOnlyList<(float X, float TopY, float BottomY)> MapRopeSegmentsForVisualization => _mapRopeSegmentsForVisualization;

        /// <summary>目前路徑點之 Hitbox 是否包含玩家位置。</summary>
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

        /// <summary>與 <see cref="IsPlayerAtTarget"/> 相同：以 <see cref="PathPlanningState.PlannedPathNodes"/> 解析，含平台與繩索。</summary>
        public NavigationNode? CurrentTarget
        {
            get
            {
                var state = CurrentPathState;
                if (state == null || state.IsPathCompleted) return null;
                if (state.CurrentWaypointIndex < 0) return null;
                if (state.PlannedPathNodes == null ||
                    state.CurrentWaypointIndex >= state.PlannedPathNodes.Count)
                    return null;

                var nodeId = state.PlannedPathNodes[state.CurrentWaypointIndex];
                if (string.IsNullOrWhiteSpace(nodeId)) return null;

                return _navGraph?.GetNode(nodeId);
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
                Logger.Error("[路徑追蹤] 不相容的 MapData：缺少 Nodes。");
                CurrentPathState = new PathPlanningState
                {
                    IsPathCompleted = true
                };
                return;
            }

            _mapRopeSegmentsForVisualization.Clear();
            if (mapData.Ropes != null)
            {
                foreach (var r in mapData.Ropes)
                {
                    if (r != null && r.Length >= 3)
                        _mapRopeSegmentsForVisualization.Add((r[0], r[1], r[2]));
                }
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
            var pos = result.PlayerPosition;
            if (!pos.HasValue) return;

            if (!_lastPosition.HasValue)
            {
                _lastPosition = pos;
                _lastMovementUtc = DateTime.UtcNow;
                return;
            }

            float dx = pos.Value.X - _lastPosition.Value.X;
            float dy = pos.Value.Y - _lastPosition.Value.Y;
            float distSq = dx * dx + dy * dy;

            // 如果位移超過微小閾值 (0.5px)，更新最後活動時間
            if (distSq > 0.25f) 
            {
                _lastPosition = pos;
                _lastMovementUtc = DateTime.UtcNow;
            }
            else
            {
                var elapsedMs = (DateTime.UtcNow - _lastMovementUtc).TotalMilliseconds;
                if (elapsedMs > AppConfig.Instance.Navigation.StuckDetectionMs)
                {
                    Logger.Warning($"[卡點判定] 角色已在 {pos.Value:F1} 停滯超過 {elapsedMs:F0}ms，觸發救援。");
                    _lastMovementUtc = DateTime.UtcNow; // 重置計時避免連續救援
                    TryRescuePath();
                }
            }
        }

        /// <summary>以臨時目標或下一路徑點計算距離。</summary>
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
            return new List<MinimapTrackingResult>(); // 已棄用歷史清單，回傳空值維持相容
        }

        public void Dispose()
        {
            if (_fsm != null)
            {
                _fsm.OnStateChanged -= OnFsmStateChanged;
                _fsm = null;
            }
            _lastPosition = null;
            CurrentPathState = null;
        }

        public bool TryRescuePath()
        {
            if (CurrentPathState == null || _navGraph == null || CurrentPathState.PlannedPath == null || CurrentPathState.PlannedPath.Count == 0) return false;

            var currentPosNullable = CurrentPathState.CurrentPlayerPosition;
            if (!currentPosNullable.HasValue) return false;
            var currentPos = currentPosNullable.Value;

            string originalTargetNodeId = CurrentPathState.PlannedPathNodes.Last();
            var originalTargetNode = _navGraph.GetNode(originalTargetNodeId);
            if (originalTargetNode == null) return false;

            Logger.Info($"[導航救援] 啟動全域重定位流程。當前位置: {currentPos:F1}，原始目標: {originalTargetNode.Id}");

            _fsm?.CancelNavigation("啟動全域重定位救援");

            var nearestNode = _navGraph.FindNearestNode(currentPos, 150.0f);

            if (nearestNode == null)
            {
                Logger.Warning("[導航救援] 角色不再路徑檔的節點半徑 (150px) 內。無法自動恢復導航。");
                return false;
            }

            float dist = (float)Math.Sqrt(Math.Pow(currentPos.X - nearestNode.Position.X, 2) + Math.Pow(currentPos.Y - nearestNode.Position.Y, 2));
            Logger.Info($"[導航救援] 找到最近補給節點 {nearestNode.Id} (距離:{dist:F1}px)，重新啟動 A* 規劃...");

            var pathObj = _navGraph.FindPath(nearestNode.Id, originalTargetNodeId);
            
            if (pathObj == null || pathObj.Edges.Count == 0 && nearestNode.Id != originalTargetNodeId)
            {
                Logger.Error($"[導航救援] A* 無法從新起點 {nearestNode.Id} 規劃至終點 {originalTargetNodeId}。");
                return false;
            }

            var updatedPath = new List<SdPointF> { currentPos };
            var updatedNodes = new List<string> { nearestNode.Id };

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

            lock (_randomLock)
            {
                CurrentPathState.PlannedPath = updatedPath;
                CurrentPathState.PlannedPathNodes = updatedNodes;

                if (updatedPath.Count <= 1)
                {
                    Logger.Info("[導航救援] 重定位完成：角色已位於目標節點。");
                    CurrentPathState.IsPathCompleted = true;
                    SelectRandomPhysicalTarget();
                }
                else
                {
                    CurrentPathState.IsPathCompleted = false;
                    CurrentPathState.CurrentWaypointIndex = 1;
                }
            }

            Logger.Info($"[導航救援] 重定位成功！新路徑包含 {CurrentPathState.PlannedPath.Count} 個節點，目標重新鎖定。");
            OnPathStateChanged?.Invoke(CurrentPathState);
            return true;
        }
    }
}
