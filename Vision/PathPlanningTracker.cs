using ArtaleAI.Models.Config;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Models.Map;
using ArtaleAI.Domain.Navigation;
using ArtaleAI.Contracts;
using ArtaleAI.Shared;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;
using Timer = System.Threading.Timer;

namespace ArtaleAI.Vision
{
    /// <summary>依小地圖追蹤更新路徑狀態、邊解析與救援重規劃。</summary>
    public partial class PathPlanningTracker : IDisposable
    {
        private NavigationGraph? _navGraph;

        /// <summary>來自 <see cref="MapData.Ropes"/>，供小地圖視窗畫垂直繩線（與節點 Type 是否為 Rope 無關）。</summary>
        private readonly List<(float X, float TopY, float BottomY)> _mapRopeSegmentsForVisualization = new();

        /// <summary>安全區終點權重（1=等權）；壓低以免巡邏常停在中繼跳板。</summary>
        private const float SafeZoneGoalWeight = 0.08f;

        /// <summary>路徑中每經過一個安全區節點的額外懲罰（連通仍保留）。</summary>
        private const float SafeZoneTransitWeightFactor = 0.7f;

        /// <summary>
        /// 純 Walk 相對含 Jump／Climb 路徑的偏好倍率。
        /// 不可把 Walk 權重設成「距離本身」，否則同層來回永遠壓過跨層。
        /// </summary>
        private const float WalkOnlyPreferenceMultiplier = 2.5f;

        /// <summary>終點與目前所在平台相同時的權重（鼓勵換層巡邏）。</summary>
        private const float SamePlatformStayPenalty = 0.35f;

        /// <summary>連續選到同一平台終點時，每多一次再乘一次（指數降權）。</summary>
        private const float SamePlatformRepeatPenalty = 0.2f;

        /// <summary>同平台連續巡邏達此次數後，若有其他平台可達則強制換層。</summary>
        private const int MaxSamePlatformStreakBeforeForceSwitch = 2;

        private string? _lastPatrolGoalPlatformId;
        private int _samePlatformPatrolStreak;

        /// <summary>
        /// 強制目標（休息導航）：設定後巡邏選點只追求該節點，到達後停住等待清除。
        /// 讀寫皆在 _randomLock 內，_forcedGoalArrived 另以 volatile 供 Pipeline 輪詢。
        /// </summary>
        private string? _forcedGoalNodeId;
        private volatile bool _forcedGoalArrived;

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

        /// <summary>進行中飛行的 ActionType；無飛行時為 null。</summary>
        public NavigationActionType? ActiveFlightActionType
        {
            get
            {
                lock (_flightLock)
                    return _activeFlight?.ActionType;
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

        public NavigationGraph? NavGraph => _navGraph;

        /// <summary>地圖檔 <c>Ropes</c> 陣列複本（小地圖相對座標），供可視化；導航拓撲仍由 <see cref="NavigationGraph"/> 決定。</summary>
        public IReadOnlyList<(float X, float TopY, float BottomY)> MapRopeSegmentsForVisualization => _mapRopeSegmentsForVisualization;

        public PlatformGeometryIndex PlatformGeometry => _platformGeometry;

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
                    }
                    else
                    {
                        Logger.Info($"[路徑追蹤] FSM 驗收成功，正式推進進度。");

                        CurrentPathState.CurrentWaypointIndex++;

                        bool pathEnded = CurrentPathState.CurrentWaypointIndex >= CurrentPathState.PlannedPath.Count;

                        if (_forcedGoalNodeId != null && !_forcedGoalArrived)
                        {
                            bool pathTargetsForcedGoal =
                                CurrentPathState.PlannedPathNodes.Count > 0 &&
                                string.Equals(CurrentPathState.PlannedPathNodes[^1], _forcedGoalNodeId, StringComparison.Ordinal);

                            if (pathTargetsForcedGoal)
                            {
                                if (pathEnded)
                                {
                                    _forcedGoalArrived = true;
                                    CurrentPathState.IsPathCompleted = true;
                                    _lastMovementUtc = DateTime.UtcNow;
                                    Logger.Info($"[路徑追蹤] 已到達強制目標 {_forcedGoalNodeId}（休息點），暫停巡邏。");
                                }
                            }
                            else
                            {
                                // 設定強制目標時尚有在途飛行：邊完成後立即改道。
                                Logger.Info($"[路徑追蹤] 在途邊完成，改道前往強制目標 {_forcedGoalNodeId}。");
                                SelectRandomPhysicalTarget();
                            }
                        }
                        else if (pathEnded)
                        {
                            Logger.Info("[路徑追蹤] 已完成整段路徑，觸發隨機巡邏規劃。");
                            SelectRandomPhysicalTarget();
                        }
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
            _lastPatrolGoalPlatformId = null;
            _samePlatformPatrolStreak = 0;
            _forcedGoalNodeId = null;
            _forcedGoalArrived = false;
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
                // 強制目標已到達＝休息倒數中，靜止是預期行為，不可當卡點。
                if (_forcedGoalArrived)
                {
                    _lastMovementUtc = DateTime.UtcNow;
                    return;
                }

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
                    RecalculateDistanceToNextWaypoint(state, playerPos.Value);
                return;
            }

            RecalculateDistanceToNextWaypoint(state, playerPos.Value);
        }

        /// <summary>是否有尚未清除的強制目標（休息導航中或倒數中）。</summary>
        public bool HasForcedGoal
        {
            get { lock (_randomLock) return _forcedGoalNodeId != null; }
        }

        /// <summary>強制目標已通過 FSM 驗收到達；等待 Pipeline 切換 Resting。</summary>
        public bool IsForcedGoalArrived => _forcedGoalArrived;

        /// <summary>強制目標尚未到達但規劃已停擺（起點不可達或 A* 失敗）；Pipeline 應節流重試。</summary>
        public bool IsForcedGoalPlanningParked
        {
            get
            {
                lock (_randomLock)
                {
                    return _forcedGoalNodeId != null
                        && !_forcedGoalArrived
                        && (CurrentPathState?.IsPathCompleted ?? true);
                }
            }
        }

        /// <summary>
        /// 注入強制目標：無在途飛行立即重規劃；有在途飛行則等該邊完成後，
        /// 由 FSM 驗收處改道，避免中斷跳躍／爬繩造成墜落。
        /// </summary>
        public void SetForcedGoal(string nodeId)
        {
            lock (_randomLock)
            {
                _forcedGoalNodeId = nodeId;
                _forcedGoalArrived = false;

                var state = CurrentPathState;
                if (state != null && !HasActiveNavigationFlight)
                    ResetPathForReplanLocked(state);
            }

            Logger.Info($"[路徑追蹤] 已注入強制目標 {nodeId}（休息導航）");
        }

        /// <summary>清除強制目標並重置路徑，下一幀恢復隨機巡邏。</summary>
        public void ClearForcedGoal()
        {
            lock (_randomLock)
            {
                if (_forcedGoalNodeId == null && !_forcedGoalArrived) return;

                _forcedGoalNodeId = null;
                _forcedGoalArrived = false;

                var state = CurrentPathState;
                if (state != null)
                    ResetPathForReplanLocked(state);
            }

            // 休息靜止可能累積卡點計時／熔斷，恢復巡邏前先清乾淨。
            _lastMovementUtc = DateTime.UtcNow;
            ResetRescueCircuitBreaker();
            Logger.Info("[路徑追蹤] 強制目標已清除，恢復隨機巡邏");
        }

        /// <summary>規劃停擺時重置路徑狀態，讓下一幀重跑強制目標 A*。</summary>
        public void RetryForcedGoalPlanning()
        {
            lock (_randomLock)
            {
                if (_forcedGoalNodeId == null || _forcedGoalArrived) return;

                var state = CurrentPathState;
                if (state == null) return;

                ResetPathForReplanLocked(state);
            }

            Logger.Info("[路徑追蹤] 強制目標規劃重試");
        }

        private static void ResetPathForReplanLocked(PathPlanningState state)
        {
            state.IsPathCompleted = false;
            state.PlannedPath = new List<SdPointF>();
            state.PlannedPathNodes = new List<string>();
            state.CurrentWaypointIndex = 0;
            state.TemporaryTarget = null;
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

            if (_forcedGoalNodeId != null)
            {
                SelectForcedGoalTarget(startNode);
                return;
            }

            var candidateNodes = BuildPatrolGoalCandidates(startNode);

            if (candidateNodes.Count == 0)
            {
                CurrentPathState.IsPathCompleted = true;
                return;
            }

            ApplyRandomSelectionAndPathfind(startNode, candidateNodes);
        }

        /// <summary>強制目標規劃：候選只有一個節點，沿用巡邏規劃管線與到達語意。</summary>
        private void SelectForcedGoalTarget(NavigationNode startNode)
        {
            var goal = _navGraph!.GetNode(_forcedGoalNodeId!);
            if (goal == null)
            {
                Logger.Warning($"[路徑追蹤] 強制目標 {_forcedGoalNodeId} 不存在於導航圖，視為已到達以免死鎖");
                _forcedGoalArrived = true;
                CurrentPathState!.IsPathCompleted = true;
                return;
            }

            if (goal.Id == startNode.Id)
            {
                _forcedGoalArrived = true;
                CurrentPathState!.IsPathCompleted = true;
                _lastMovementUtc = DateTime.UtcNow;
                Logger.Info($"[路徑追蹤] 已位於強制目標 {goal.Id}，直接回報到達");
                return;
            }

            ApplyRandomSelectionAndPathfind(startNode, new List<NavigationNode> { goal });
        }

        /// <summary>
        /// 巡邏終點：排除繩索／跳點切口節點（垂直通道樞紐），避免一直走「切口→平台一端」的短段。
        /// 拓撲仍保留這些節點供轉乘；只是不當巡邏目標。
        /// </summary>
        private List<NavigationNode> BuildPatrolGoalCandidates(NavigationNode startNode)
        {
            if (_navGraph == null)
                return new List<NavigationNode>();

            var allPlatform = _navGraph.GetAllNodes()
                .Where(n => n.Type == NavigationNodeType.Platform && n.Id != startNode.Id)
                .ToList();

            var transitHubIds = CollectVerticalTransitHubIds();
            var nonHub = allPlatform
                .Where(n => !transitHubIds.Contains(n.Id))
                .ToList();

            // 安全區是通道，但有一般平台可當巡邏終點時優先排除。
            var preferred = nonHub.Where(n => !n.IsSafeZone).ToList();
            if (preferred.Count > 0)
                return preferred;

            if (nonHub.Count > 0)
                return nonHub;

            var nonSafeFallback = allPlatform.Where(n => !n.IsSafeZone).ToList();
            if (nonSafeFallback.Count > 0)
                return nonSafeFallback;

            Logger.Debug("[路徑規劃] 無可排除垂直樞紐／安全區的巡邏終點，回退為全部平台節點");
            return allPlatform;
        }

        private HashSet<string> CollectVerticalTransitHubIds()
        {
            var hubs = new HashSet<string>(StringComparer.Ordinal);
            if (_navGraph == null)
                return hubs;

            foreach (var node in _navGraph.GetAllNodes())
            {
                foreach (var edge in _navGraph.GetOutgoingEdges(node.Id))
                {
                    if (!IsVerticalTransitAction(edge.ActionType))
                        continue;

                    hubs.Add(edge.FromNodeId);
                    hubs.Add(edge.ToNodeId);
                }
            }

            return hubs;
        }

        private static bool IsVerticalTransitAction(NavigationActionType action) =>
            action is NavigationActionType.ClimbUp
                or NavigationActionType.ClimbDown
                or NavigationActionType.Jump
                or NavigationActionType.SideJump
                or NavigationActionType.JumpDown;

        private void ApplyRandomSelectionAndPathfind(NavigationNode startNode, List<NavigationNode> candidateNodes)
        {
            if (CurrentPathState == null || _navGraph == null || candidateNodes.Count == 0) return;

            lock (_randomLock)
            {
                var reachablePaths = new List<(NavigationNode Node, NavigationPath Path, float Weight)>();

                foreach (var goalNode in candidateNodes)
                {
                    var pathObj = _navGraph.FindPath(startNode.Id, goalNode.Id);
                    if (pathObj == null || pathObj.Edges.Count == 0) continue;

                    float weight = ScorePatrolCandidate(startNode, goalNode, pathObj);
                    reachablePaths.Add((goalNode, pathObj, weight));
                }

                if (reachablePaths.Count == 0)
                {
                    CurrentPathState.IsPathCompleted = true;
                    return;
                }

                // 同平台連續太多次：有其他平台可達就強制換層（通道仍保留，只改終點偏好）。
                if (_samePlatformPatrolStreak >= MaxSamePlatformStreakBeforeForceSwitch &&
                    !string.IsNullOrEmpty(_lastPatrolGoalPlatformId))
                {
                    var switchAway = reachablePaths
                        .Where(c => !IsSamePlatform(c.Node.PlatformId, _lastPatrolGoalPlatformId))
                        .ToList();
                    if (switchAway.Count > 0)
                    {
                        Logger.Info(
                            $"[路徑規劃] 同平台連續 {_samePlatformPatrolStreak} 次，強制換離 {_lastPatrolGoalPlatformId}");
                        reachablePaths = switchAway;
                    }
                }

                var (selectedGoal, selectedPath) = PickWeightedCandidate(reachablePaths);
                RecordPatrolGoalPlatform(selectedGoal.PlatformId);

                var newPlannedPath = new List<SdPointF> { new SdPointF(startNode.Position.X, startNode.Position.Y) };
                var newNodeIds = new List<string> { startNode.Id };

                foreach (var edge in selectedPath.Edges)
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

                Logger.Info(
                    $"[路徑規劃] 找到路線！{startNode.Id} -> {selectedGoal.Id}，" +
                    $"成本={selectedPath.TotalCost:F1}，保留原始節點共 {CurrentPathState.PlannedPath.Count} 個");
            }
        }

        private void RecordPatrolGoalPlatform(string? platformId)
        {
            if (string.IsNullOrEmpty(platformId))
            {
                _lastPatrolGoalPlatformId = null;
                _samePlatformPatrolStreak = 0;
                return;
            }

            if (IsSamePlatform(platformId, _lastPatrolGoalPlatformId))
                _samePlatformPatrolStreak++;
            else
            {
                _lastPatrolGoalPlatformId = platformId;
                _samePlatformPatrolStreak = 1;
            }
        }

        private static bool IsSamePlatform(string? a, string? b) =>
            !string.IsNullOrEmpty(a) &&
            !string.IsNullOrEmpty(b) &&
            string.Equals(a, b, StringComparison.Ordinal);

        /// <summary>
        /// 巡邏終點權重（同一尺度）：
        /// - 基底：1/(成本+1)，Walk 與 Jump 路徑可比較
        /// - 純 Walk 再乘偏好倍率（多走路線標記，但不獨占）
        /// - 終點／途經安全區：降權（通道仍可走）
        /// - 同平台連抽：指數降權，避免單一平台來回太多次
        /// </summary>
        private float ScorePatrolCandidate(NavigationNode start, NavigationNode goal, NavigationPath path)
        {
            // 統一用反成本，避免「同層長距離 Walk 權重 177、跨層 Jump 權重 0.02」導致永不上樓。
            float weight = 1.0f / (path.TotalCost + 1.0f);

            bool walkOnly = path.Edges.TrueForAll(e => e.ActionType == NavigationActionType.Walk);
            if (walkOnly)
                weight *= WalkOnlyPreferenceMultiplier;

            int nonWalkHops = path.Edges.Count(e => e.ActionType != NavigationActionType.Walk);
            if (nonWalkHops > 0)
                weight /= (1.0f + 0.35f * nonWalkHops);

            if (goal.IsSafeZone)
                weight *= SafeZoneGoalWeight;

            if (IsSamePlatform(goal.PlatformId, start.PlatformId))
                weight *= SamePlatformStayPenalty;

            if (IsSamePlatform(goal.PlatformId, _lastPatrolGoalPlatformId) && _samePlatformPatrolStreak > 0)
                weight *= MathF.Pow(SamePlatformRepeatPenalty, _samePlatformPatrolStreak);

            if (_navGraph != null)
            {
                int safeTransit = 0;
                foreach (var edge in path.Edges)
                {
                    var to = _navGraph.GetNode(edge.ToNodeId);
                    if (to != null && to.IsSafeZone)
                        safeTransit++;
                }

                for (int i = 0; i < safeTransit; i++)
                    weight *= SafeZoneTransitWeightFactor;
            }

            return Math.Max(weight, 0.0001f);
        }

        private (NavigationNode Node, NavigationPath Path) PickWeightedCandidate(
            List<(NavigationNode Node, NavigationPath Path, float Weight)> candidates)
        {
            float total = candidates.Sum(c => c.Weight);
            float roll = (float)_random.NextDouble() * total;
            float acc = 0f;
            foreach (var c in candidates)
            {
                acc += c.Weight;
                if (roll <= acc)
                    return (c.Node, c.Path);
            }

            var last = candidates[^1];
            return (last.Node, last.Path);
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
