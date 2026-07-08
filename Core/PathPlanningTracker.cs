using ArtaleAI.Models.Config;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Models.Map;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Services;
using ArtaleAI.Utils;
using System.Collections.Generic;
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

        private bool _sideJumpApproachInProgress;
        private string? _sideJumpApproachFromNodeId;
        private PlatformGeometryIndex _platformGeometry = PlatformGeometryIndex.Empty;
        private string? _approachFailNodeId;
        private int _approachFailCount;
        private readonly HashSet<string> _approachCutoffNodes = new(StringComparer.Ordinal);

        private bool _rescueCircuitBroken;
        private string? _lastRescueKey;
        private int _consecutiveSameRescueCount;

        private readonly object _flightLock = new();
        private NavigationFlight? _activeFlight;
        private ExecutionTarget? _frozenFlightTarget;
        private bool _waypointCompletionAcknowledged;

        private enum ExecutionTargetResolveMode
        {
            Live,
            Frozen
        }

        /// <summary>是否有進行中且尚未宣告完成的導航飛行。</summary>
        public bool HasActiveNavigationFlight
        {
            get
            {
                lock (_flightLock)
                    return _activeFlight != null && !_waypointCompletionAcknowledged;
            }
        }

        /// <summary>救援熔斷已觸發時為 true；編排層應停止 TryStartNavigation。</summary>
        public bool IsRescueCircuitBroken => _rescueCircuitBroken;

        /// <summary>僅當失敗回報對應目前 in-flight token 時才應觸發救援。</summary>
        public bool ShouldAcceptFailureRescue(Guid flightToken)
        {
            lock (_flightLock)
                return _activeFlight != null && _activeFlight.Token == flightToken;
        }

        /// <summary>目前規劃中的路徑與索引。</summary>
        public PathPlanningState? CurrentPathState { get; private set; }

        public event Action<MinimapTrackingResult>? OnTrackingUpdated;
        public event Action<PathPlanningState>? OnPathStateChanged;
        public event Action<SdPointF>? OnWaypointReached;

        public NavigationGraph? NavGraph => _navGraph;

        /// <summary>地圖檔 <c>Ropes</c> 陣列複本（小地圖相對座標），供可視化；導航拓撲仍由 <see cref="NavigationGraph"/> 決定。</summary>
        public IReadOnlyList<(float X, float TopY, float BottomY)> MapRopeSegmentsForVisualization => _mapRopeSegmentsForVisualization;

        public PlatformGeometryIndex PlatformGeometry => _platformGeometry;

        /// <summary>診斷目前路徑點驗收狀態。</summary>
        public ArrivalDiagnostic? DiagnoseCurrentTarget(PointF playerPos)
        {
            var target = ResolveExecutionTarget(edge: null, ExecutionTargetResolveMode.Live);
            return target == null ? null : ArrivalValidator.Diagnose(playerPos, target, _platformGeometry);
        }

        public void LogCurrentArrivalDiagnostic(PointF playerPos, bool asWarning = false)
        {
            var diagnostic = DiagnoseCurrentTarget(playerPos);
            if (diagnostic != null)
                ArrivalValidator.LogDiagnostic(diagnostic, asWarning);
        }

        public void LogFrozenFlightArrivalDiagnostic(PointF playerPos, bool asWarning = false)
        {
            ExecutionTarget? target;
            lock (_flightLock)
                target = _frozenFlightTarget;

            if (target == null)
            {
                Logger.Warning("[導航] 凍結目標為 null，無法輸出 flight 驗收診斷");
                return;
            }

            ArrivalValidator.LogDiagnostic(
                ArrivalValidator.Diagnose(playerPos, target, _platformGeometry),
                asWarning);
        }

        /// <summary>目前路徑點是否通過執行層驗收（<see cref="ArrivalValidator"/>）。</summary>
        public bool IsPlayerAtTarget()
        {
            var target = ResolveExecutionTarget(edge: null, ExecutionTargetResolveMode.Live);
            if (target == null) return false;

            var state = CurrentPathState;
            if (state?.CurrentPlayerPosition is not { } playerPos)
                return false;

            if (state.IsPathCompleted || state.CurrentWaypointIndex >= state.PlannedPathNodes.Count)
                return false;

            return ArrivalValidator.IsArrived(playerPos, target, _platformGeometry);
        }

        /// <summary>以 BeginNavigationFlight 當下凍結的目標驗收；執行層收尾專用。</summary>
        public bool IsPlayerAtFrozenFlightTarget()
        {
            ExecutionTarget? target;
            lock (_flightLock)
                target = _frozenFlightTarget;

            if (target == null)
                return false;

            var state = CurrentPathState;
            if (state?.CurrentPlayerPosition is not { } playerPos)
                return false;

            return ArrivalValidator.IsArrived(playerPos, target, _platformGeometry);
        }

        /// <summary>啟動 edge 執行時建立飛行生命週期並凍結驗收目標。</summary>
        public Guid BeginNavigationFlight(NavigationEdge edge)
        {
            lock (_flightLock)
            {
                ClearNavigationFlightInternal();
                var state = CurrentPathState;
                int waypointIndex = state?.CurrentWaypointIndex ?? 0;
                var token = Guid.NewGuid();
                _activeFlight = new NavigationFlight(
                    token,
                    waypointIndex,
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.ActionType);
                _frozenFlightTarget = ResolveExecutionTarget(edge, ExecutionTargetResolveMode.Frozen);
                if (_frozenFlightTarget == null)
                {
                    Logger.Error(
                        $"[導航] 飛行啟動但凍結目標解析失敗 " +
                        $"edge={edge.FromNodeId}->{edge.ToNodeId} action={edge.ActionType}");
                }

                _waypointCompletionAcknowledged = false;
                return token;
            }
        }

        /// <summary>
        /// 唯一執行目標解析入口。
        /// Live：編排層即時驗收；Frozen：BeginNavigationFlight 當下凍結，缺失資料 fail-fast。
        /// </summary>
        private ExecutionTarget? ResolveExecutionTarget(NavigationEdge? edge, ExecutionTargetResolveMode mode)
        {
            bool frozen = mode == ExecutionTargetResolveMode.Frozen;

            if (_navGraph == null)
            {
                Logger.Error(
                    $"[導航] 執行目標解析失敗：NavGraph 未載入 " +
                    $"edge={FormatEdgeLabel(edge)} mode={mode}");
                return null;
            }

            if (_sideJumpApproachInProgress && !string.IsNullOrEmpty(_sideJumpApproachFromNodeId))
            {
                bool useTakeoff = frozen
                    ? edge?.ActionType == NavigationActionType.Walk
                    : true;

                if (useTakeoff)
                {
                    var approachFrom = _navGraph.GetNode(_sideJumpApproachFromNodeId);
                    if (approachFrom != null)
                        return ExecutionTargetTranslator.ForJumpTakeoff(approachFrom);

                    Logger.Error(
                        $"[導航] 執行目標解析失敗：找不到 approach 節點 " +
                        $"{_sideJumpApproachFromNodeId} edge={FormatEdgeLabel(edge)} mode={mode}");
                    return null;
                }
            }

            if (frozen && edge != null &&
                edge.ActionType is NavigationActionType.Jump or NavigationActionType.SideJump or NavigationActionType.JumpDown)
            {
                var landingNode = _navGraph.GetNode(edge.ToNodeId);
                if (landingNode != null)
                    return ExecutionTargetTranslator.ForJumpLanding(landingNode);

                Logger.Error(
                    $"[導航] 凍結執行目標失敗：Jump 落地節點不存在 to={edge.ToNodeId}");
                return null;
            }

            if (!frozen)
            {
                var state = CurrentPathState;
                if (state == null || state.IsPathCompleted ||
                    state.PlannedPathNodes == null ||
                    state.CurrentWaypointIndex >= state.PlannedPathNodes.Count)
                    return null;
            }

            var waypointNode = GetWaypointNodeAtCurrentIndex();
            if (waypointNode == null)
            {
                Logger.Error(
                    $"[導航] 執行目標解析失敗：無有效 waypoint " +
                    $"edge={FormatEdgeLabel(edge)} index={CurrentPathState?.CurrentWaypointIndex} mode={mode}");
                return null;
            }

            NavigationEdge? policyEdge = frozen ? edge : CurrentNavigationEdge;
            if (policyEdge?.ActionType is NavigationActionType.ClimbUp or NavigationActionType.ClimbDown)
            {
                if (!NavigationRopeHelper.TryExtractRopeX(policyEdge, out float ropeX))
                {
                    Logger.Error(
                        $"[導航] 執行目標解析失敗：Climb 邊缺少 ropeX metadata " +
                        $"edge={FormatEdgeLabel(policyEdge)}");
                    return null;
                }

                return ExecutionTargetTranslator.ForRopeLanding(waypointNode, ropeX);
            }

            return ExecutionTargetTranslator.ForWaypoint(waypointNode);
        }

        private static string FormatEdgeLabel(NavigationEdge? edge) =>
            edge == null ? "-" : $"{edge.FromNodeId}->{edge.ToNodeId}({edge.ActionType})";

        private NavigationNode? GetWaypointNodeAtCurrentIndex()
        {
            var state = CurrentPathState;
            if (state == null ||
                state.CurrentWaypointIndex < 0 ||
                state.PlannedPathNodes == null ||
                state.CurrentWaypointIndex >= state.PlannedPathNodes.Count)
                return null;

            return _navGraph?.GetNode(state.PlannedPathNodes[state.CurrentWaypointIndex]);
        }

        /// <summary>取消或收尾時清除飛行狀態。</summary>
        public void EndNavigationFlight()
        {
            lock (_flightLock)
                ClearNavigationFlightInternal();
        }

        /// <summary>
        /// 宣告目前 waypoint 完成；同一飛行生命週期僅生效一次（僅 Executor 路徑）。
        /// </summary>
        public bool TryAcknowledgeWaypointCompletion(Guid token)
        {
            lock (_flightLock)
            {
                if (_waypointCompletionAcknowledged)
                {
                    Logger.Debug("[導航] waypoint 完成已記錄，忽略重複宣告");
                    return false;
                }

                if (_activeFlight == null || _activeFlight.Token != token)
                {
                    Logger.Debug("[導航] 完成 token 不符，忽略延遲回報");
                    return false;
                }

                var state = CurrentPathState;
                if (state == null || state.CurrentWaypointIndex != _activeFlight.WaypointIndex)
                {
                    Logger.Debug("[導航] waypoint index 已變更，忽略過期 executor 回報");
                    return false;
                }

                _waypointCompletionAcknowledged = true;
                Logger.Info(
                    $"[導航] waypoint 完成確認 source=Executor " +
                    $"token={token} index={_activeFlight.WaypointIndex}");
                return true;
            }
        }

        private void ClearNavigationFlightInternal()
        {
            _activeFlight = null;
            _frozenFlightTarget = null;
            _waypointCompletionAcknowledged = false;
        }

        /// <summary>Walk 移動層：優先使用飛行凍結目標的平台幾何。</summary>
        public WalkPlatformContext? GetWalkPlatformContextForActiveFlight()
        {
            ExecutionTarget? target;
            lock (_flightLock)
                target = _frozenFlightTarget;

            if (target == null || string.IsNullOrEmpty(target.PlatformId))
                return null;

            return new WalkPlatformContext
            {
                PlatformId = target.PlatformId,
                Geometry = _platformGeometry,
                NodeId = target.NodeId
            };
        }

        /// <summary>供 Walk 移動層使用的平台幾何上下文。</summary>
        public WalkPlatformContext? GetCurrentWalkPlatformContext()
        {
            var target = ResolveExecutionTarget(edge: null, ExecutionTargetResolveMode.Live);
            if (target == null || string.IsNullOrEmpty(target.PlatformId))
                return null;

            return new WalkPlatformContext
            {
                PlatformId = target.PlatformId,
                Geometry = _platformGeometry,
                NodeId = target.NodeId
            };
        }

        /// <summary>目前生效的 Runtime 執行目標。</summary>
        public ExecutionTarget? GetCurrentExecutionTarget() =>
            ResolveExecutionTarget(edge: null, ExecutionTargetResolveMode.Live);

        /// <summary>玩家是否仍掛在繩索段上（不可啟動水平 Walk）。</summary>
        public bool IsPlayerOnRope(PointF playerPos)
        {
            var nav = AppConfig.Instance.Navigation;
            return NavigationRopeHelper.IsPositionOnRope(
                playerPos,
                _mapRopeSegmentsForVisualization,
                (float)nav.RopeSegmentXTolerancePx,
                (float)nav.RopeLandingYTolerancePx);
        }

        /// <summary>
        /// Jump / SideJump / JumpDown 前若尚未站在 from 節點 Hitbox，回傳需先執行的 Walk 邊與目標座標。
        /// 玩家不在 from 平台可水平站立帶時回傳 false（不可跨層水平 approach）。
        /// </summary>
        public bool IsJumpTakeoffReachableByWalk(PointF playerPos, NavigationEdge jumpEdge)
        {
            if (_navGraph == null) return false;
            var fromNode = _navGraph.GetNode(jumpEdge.FromNodeId);
            if (fromNode == null) return false;

            var takeoff = ExecutionTargetTranslator.ForJumpTakeoff(fromNode);
            return IsOnPlatformStandBand(playerPos, takeoff);
        }

        public bool TryGetSideJumpApproachWalk(
            NavigationEdge jumpEdge,
            out NavigationEdge approachWalkEdge,
            out SdPointF approachTarget)
        {
            approachWalkEdge = null!;
            approachTarget = default;

            if (jumpEdge.ActionType is not (NavigationActionType.Jump or NavigationActionType.SideJump or NavigationActionType.JumpDown) ||
                _navGraph == null ||
                CurrentPathState == null)
                return false;

            var fromNode = _navGraph.GetNode(jumpEdge.FromNodeId);
            if (fromNode == null) return false;

            var playerPos = CurrentPathState.CurrentPlayerPosition;
            if (!playerPos.HasValue) return false;

            var takeoffTarget = ExecutionTargetTranslator.ForJumpTakeoff(fromNode);

            if (!IsOnPlatformStandBand(playerPos.Value, takeoffTarget))
            {
                Logger.Warning(
                    $"[導航] Approach 拒絕：跨平台不可水平對位 node={fromNode.Id} " +
                    $"player=({playerPos.Value.X:F1},{playerPos.Value.Y:F1}) takeoffY={takeoffTarget.AnchorY:F1}");
                return false;
            }

            if (ArrivalValidator.IsArrived(playerPos.Value, takeoffTarget, _platformGeometry))
                return false;

            if (_approachCutoffNodes.Contains(jumpEdge.FromNodeId))
            {
                float xErr = Math.Abs(playerPos.Value.X - takeoffTarget.TargetX);
                if (xErr <= (float)AppConfig.Instance.Navigation.WalkAlignTolerancePx)
                {
                    Logger.Warning(
                        $"[導航] Approach 熔斷放行 Jump：node={jumpEdge.FromNodeId} xErr={xErr:F2}px");
                    ClearSideJumpApproachState();
                    return false;
                }
            }

            approachTarget = new SdPointF(takeoffTarget.TargetX, fromNode.Position.Y);
            CurrentPathState.TemporaryTarget = approachTarget;

            approachWalkEdge = ResolveApproachWalkEdge(playerPos.Value, jumpEdge.FromNodeId)
                ?? new NavigationEdge(jumpEdge.FromNodeId, jumpEdge.FromNodeId, NavigationActionType.Walk);
            return true;
        }

        public void MarkSideJumpApproachStarted(string fromNodeId)
        {
            _sideJumpApproachInProgress = true;
            _sideJumpApproachFromNodeId = fromNodeId;
        }

        /// <summary>Jump approach Walk 進行中；編排層應避免重啟 approach 或打斷跳躍。</summary>
        public bool IsSideJumpApproachInProgress => _sideJumpApproachInProgress;

        public void ClearSideJumpApproachState()
        {
            _sideJumpApproachInProgress = false;
            _sideJumpApproachFromNodeId = null;
            _approachFailNodeId = null;
            _approachFailCount = 0;
            if (CurrentPathState != null)
                CurrentPathState.TemporaryTarget = null;
        }

        private void RecordApproachWalkFailure(string fromNodeId)
        {
            if (_approachFailNodeId == fromNodeId)
                _approachFailCount++;
            else
            {
                _approachFailNodeId = fromNodeId;
                _approachFailCount = 1;
            }

            int cutoff = AppConfig.Instance.Navigation.ApproachFailureRescueCutoff;
            if (_approachFailCount >= cutoff)
            {
                _approachCutoffNodes.Add(fromNodeId);
                Logger.Warning(
                    $"[導航] Approach 熔斷觸發 node={fromNodeId} 連續失敗 {_approachFailCount} 次");
            }
        }

        private NavigationEdge? ResolveApproachWalkEdge(SdPointF playerPos, string fromNodeId)
        {
            if (_navGraph == null) return null;

            var nearest = _navGraph.FindNearestNode(playerPos, 150f);
            if (nearest == null || nearest.Id == fromNodeId) return null;

            var path = _navGraph.FindPath(nearest.Id, fromNodeId);
            if (path == null || path.Edges.Count == 0) return null;

            return path.Edges.FirstOrDefault(e => e.ActionType == NavigationActionType.Walk) ?? path.Edges[0];
        }

        private bool IsOnPlatformStandBand(PointF playerPos, ExecutionTarget target)
        {
            float yTol = (float)AppConfig.Instance.Navigation.SlopeStandYTolerancePx;

            if (!string.IsNullOrEmpty(target.PlatformId) &&
                _platformGeometry.TryProjectStandY(target.PlatformId, playerPos.X, out float projectedY, out _))
                return Math.Abs(playerPos.Y - projectedY) <= yTol;

            return Math.Abs(playerPos.Y - target.AnchorY) <= yTol;
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

                    if (_sideJumpApproachInProgress)
                    {
                        ClearSideJumpApproachState();
                        Logger.Info("[路徑追蹤] 跳躍起跳點已對齊，下帧執行跳躍動作。");
                        OnPathStateChanged?.Invoke(CurrentPathState);
                    }
                    else
                    {
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

                EndNavigationFlight();
                ResetRescueCircuitBreaker();
            }
        }

        private void ResetRescueCircuitBreaker()
        {
            _lastRescueKey = null;
            _consecutiveSameRescueCount = 0;
            _rescueCircuitBroken = false;
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
            _platformGeometry = _navGraph.PlatformGeometry;
            _navGraph.LogMapDataIssues();
            _approachCutoffNodes.Clear();
            _approachFailNodeId = null;
            _approachFailCount = 0;
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

            if (distSq > 0.25f) 
            {
                _lastPosition = pos;
                _lastMovementUtc = DateTime.UtcNow;
                ResetRescueCircuitBreaker();
            }
            else
            {
                var elapsedMs = (DateTime.UtcNow - _lastMovementUtc).TotalMilliseconds;
                if (elapsedMs > AppConfig.Instance.Navigation.StuckDetectionMs)
                {
                    if (_rescueCircuitBroken)
                    {
                        Logger.Warning($"[卡點判定] 角色已在 {pos.Value:F1} 停滯，救援熔斷已生效，停止重試。");
                        _lastMovementUtc = DateTime.UtcNow;
                        return;
                    }

                    Logger.Warning($"[卡點判定] 角色已在 {pos.Value:F1} 停滯超過 {elapsedMs:F0}ms，觸發救援。");
                    _lastMovementUtc = DateTime.UtcNow;
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
                        reachablePaths.Add((goalNode, pathObj));
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
                    CurrentPathState.TemporaryTarget = null;
                    ClearSideJumpApproachState();

                    Logger.Info($"[路徑規劃] 找到路線！{startNode.Id} -> {goalNode.Id}，保留所有原始節點共 {CurrentPathState.PlannedPath.Count} 個");
                    return;
                }

                CurrentPathState.IsPathCompleted = true;
            }
        }

        public List<MinimapTrackingResult> GetTrackingHistory()
        {
            return new List<MinimapTrackingResult>();
        }

        public void Dispose()
        {
            if (_fsm != null)
            {
                _fsm.OnStateChanged -= OnFsmStateChanged;
                _fsm = null;
            }
            _lastPosition = null;
            ResetRescueCircuitBreaker();
            CurrentPathState = null;
        }

        public bool TryRescuePath()
        {
            if (CurrentPathState == null || _navGraph == null || CurrentPathState.PlannedPath == null || CurrentPathState.PlannedPath.Count == 0) return false;

            if (_rescueCircuitBroken)
            {
                Logger.Warning("[導航救援] 熔斷已生效，停止重複救援。");
                return false;
            }

            if (_sideJumpApproachInProgress && !string.IsNullOrEmpty(_sideJumpApproachFromNodeId))
            {
                RecordApproachWalkFailure(_sideJumpApproachFromNodeId);
                if (_approachCutoffNodes.Contains(_sideJumpApproachFromNodeId))
                {
                    Logger.Warning("[導航救援] Approach 熔斷生效，停止重複 Rescue");
                    ClearSideJumpApproachState();
                    return false;
                }
            }

            var currentPosNullable = CurrentPathState.CurrentPlayerPosition;
            if (!currentPosNullable.HasValue) return false;
            var currentPos = currentPosNullable.Value;

            string originalTargetNodeId = CurrentPathState.PlannedPathNodes.Last();
            var originalTargetNode = _navGraph.GetNode(originalTargetNodeId);
            if (originalTargetNode == null) return false;

            Logger.Info($"[導航救援] 啟動全域重定位流程。當前位置: {currentPos:F1}，原始目標: {originalTargetNode.Id}");

            _fsm?.CancelNavigation("啟動全域重定位救援");
            EndNavigationFlight();
            ClearSideJumpApproachState();

            var nearestNode = _navGraph.FindNearestNode(currentPos, 150.0f);

            if (nearestNode == null)
            {
                Logger.Warning("[導航救援] 角色不再路徑檔的節點半徑 (150px) 內。無法自動恢復導航。");
                return false;
            }

            float dist = (float)Math.Sqrt(Math.Pow(currentPos.X - nearestNode.Position.X, 2) + Math.Pow(currentPos.Y - nearestNode.Position.Y, 2));
            Logger.Info($"[導航救援] 找到最近補給節點 {nearestNode.Id} (距離:{dist:F1}px)，重新啟動 A* 規劃...");

            if (!RecordRescueAttempt(nearestNode.Id, originalTargetNodeId))
                return false;

            var pathObj = _navGraph.FindPath(nearestNode.Id, originalTargetNodeId);
            
            if (pathObj == null || pathObj.Edges.Count == 0 && nearestNode.Id != originalTargetNodeId)
            {
                Logger.Error($"[導航救援] A* 無法從新起點 {nearestNode.Id} 規劃至終點 {originalTargetNodeId}。");
                return false;
            }

            var updatedPath = new List<SdPointF> { nearestNode.Position };
            var updatedNodes = new List<string> { nearestNode.Id };

            if (dist > 1f)
            {
                Logger.Warning(
                    $"[導航救援] 路徑起點對齊節點 {nearestNode.Id}，玩家偏移 {dist:F1}px");
            }

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

        private bool RecordRescueAttempt(string nearestNodeId, string ultimateTargetNodeId)
        {
            string rescueKey = _activeFlight?.RescueKey(ultimateTargetNodeId)
                ?? $"{nearestNodeId}|{ultimateTargetNodeId}";

            if (string.Equals(_lastRescueKey, rescueKey, StringComparison.Ordinal))
                _consecutiveSameRescueCount++;
            else
            {
                _consecutiveSameRescueCount = 1;
                _lastRescueKey = rescueKey;
            }

            int cutoff = AppConfig.Instance.Navigation.RescueRepeatCutoff;
            if (_consecutiveSameRescueCount < cutoff)
                return true;

            _rescueCircuitBroken = true;
            Logger.Error(
                $"[導航救援] 熔斷觸發：同導航鍵 {rescueKey} 連續救援 {_consecutiveSameRescueCount} 次，停止重試 (blocked/stuck)。");
            return false;
        }
    }
}
