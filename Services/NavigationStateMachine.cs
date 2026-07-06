using System;
using System.Threading;
using System.Threading.Tasks;
using ArtaleAI.Core;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Utils;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
{
    /// <summary>導航 FSM：序列化邊執行、錯誤救援與狀態轉移。</summary>
    public class NavigationStateMachine : INavigationStateMachine
    {
        private NavigationState _currentState = NavigationState.Idle;
        private CancellationTokenSource? _currentTaskCts;
        private readonly object _stateLock = new object();

        private readonly NavigationExecutor _executor;
        private readonly PathPlanningTracker? _tracker;

        public NavigationStateMachine(NavigationExecutor executor, PathPlanningTracker? tracker = null)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _tracker = tracker;
        }

        public NavigationState CurrentState => _currentState;

        public event Action<NavigationState, NavigationState>? OnStateChanged;

        /// <inheritdoc />
        public bool TryStartNavigation(NavigationEdge edge, SdPointF currentPos, SdPointF targetPos)
        {
            lock (_stateLock)
            {
                if (_tracker?.IsRescueCircuitBroken == true)
                {
                    return false;
                }

                if (_currentState != NavigationState.Idle && _currentState != NavigationState.Reached_Waypoint)
                {
                    return false;
                }

                _currentTaskCts?.Dispose();
                _currentTaskCts = new CancellationTokenSource();

                NavigationState nextState = edge.ActionType switch
                {
                    NavigationActionType.ClimbUp or NavigationActionType.ClimbDown =>
                        NavigationState.Moving_Vertical,
                    NavigationActionType.Walk => NavigationState.Moving_Horizontal,
                    NavigationActionType.Jump => NavigationState.Jumping,
                    NavigationActionType.SideJump => NavigationState.Jumping,
                    NavigationActionType.JumpDown => NavigationState.Jumping,
                    NavigationActionType.Teleport => NavigationState.Transitioning,
                    _ => NavigationState.Moving_Horizontal
                };

                ChangeState(nextState);

                Guid flightToken = _tracker?.BeginNavigationFlight(edge) ?? Guid.Empty;

                _ = InternalExecuteEdgeAsync(edge, currentPos, targetPos, _currentTaskCts.Token, flightToken);

                return true;
            }
        }

        private async Task InternalExecuteEdgeAsync(
            NavigationEdge edge,
            SdPointF currentPos,
            SdPointF targetPos,
            CancellationToken token,
            Guid flightToken)
        {
            try
            {
                Logger.Info($"[FSM] 開始執行 Edge {edge.ActionType}，目標: ({targetPos.X}, {targetPos.Y})");

                var exeResult = await _executor.ExecuteActionAsync(
                    edge,
                    (System.Drawing.PointF)currentPos,
                    (System.Drawing.PointF)targetPos,
                    () => _tracker?.IsPlayerAtFrozenFlightTarget() ?? false,
                    token);

                if (exeResult == NavigationExecutor.ExecutionResult.Failed || exeResult == NavigationExecutor.ExecutionResult.Error)
                {
                    bool shouldRescue = _tracker?.ShouldAcceptFailureRescue(flightToken) ?? true;
                    _tracker?.EndNavigationFlight();

                    if (!shouldRescue)
                    {
                        Logger.Debug("[FSM] 忽略過期 executor 失敗回報");
                        return;
                    }

                    Logger.Warning($"[FSM] 執行 Edge {edge.ActionType} 發生中斷 (Error/Failed)。啟動自癒分析...");
                    ChangeState(NavigationState.Error);

                    if (_tracker != null)
                    {
                        bool rescueSuccess = _tracker.TryRescuePath();
                        if (rescueSuccess)
                        {
                            Logger.Info("[FSM] 救援路徑規劃成功，狀態將重置並容許繼續導航。");
                            ChangeState(NavigationState.Idle);
                        }
                        else
                        {
                            Logger.Error("[FSM] 致命錯誤：救援路徑規劃失敗！自動導航中止。");
                            ChangeState(NavigationState.Idle);
                        }
                    }
                    else
                    {
                        Logger.Warning("[FSM] 未配置 PathPlanningTracker，無法執行救援路徑規劃。");
                    }
                    return;
                }

                if (!token.IsCancellationRequested &&
                    exeResult == NavigationExecutor.ExecutionResult.Completed)
                {
                    if (_tracker == null || _tracker.TryAcknowledgeWaypointCompletion(flightToken))
                    {
                        Logger.Debug("[FSM] 執行層完成且 waypoint 完成已確認，進入 Reached_Waypoint");
                        ChangeState(NavigationState.Reached_Waypoint);
                    }
                    else
                    {
                        Logger.Debug("[FSM] 執行層完成但 waypoint 完成已被處理，忽略重複推進");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("[FSM] 任務已被取消 (例外流退避)。");
                _tracker?.EndNavigationFlight();
            }
            catch (Exception ex)
            {
                Logger.Error($"[FSM] 執行 Edge 發生錯誤: {ex.Message}");
                _tracker?.EndNavigationFlight();
                ChangeState(NavigationState.Error);
            }
            finally
            {
                if (_currentState == NavigationState.Reached_Waypoint || _currentState == NavigationState.Error)
                {
                    ChangeState(NavigationState.Idle);
                }
            }
        }

        /// <inheritdoc />
        public void CancelNavigation(string reason = "使用者強制中斷")
        {
            lock (_stateLock)
            {
                if (_currentState == NavigationState.Idle) return;

                Logger.Info($"[FSM] 收到中斷請求，原因：{reason}");

                _currentTaskCts?.Cancel();

                _tracker?.EndNavigationFlight();
                ChangeState(NavigationState.Idle);
            }
        }

        /// <inheritdoc />
        public void NotifyTargetReached()
        {
            lock (_stateLock)
            {
                if (_currentState != NavigationState.Idle)
                    return;

                if (!(_tracker?.TryAcknowledgeWaypointCompletion(
                        Guid.Empty,
                        WaypointCompletionSource.IdleSupplement) ?? false))
                    return;

                Logger.Debug("[FSM] Idle 補推進：驗收成立且無 in-flight，推進 Waypoint");
                ChangeState(NavigationState.Reached_Waypoint);
                ChangeState(NavigationState.Idle);
            }
        }


        private void ChangeState(NavigationState newState)
        {
            lock (_stateLock)
            {
                if (_currentState == newState) return;

                var oldState = _currentState;
                _currentState = newState;

                Logger.Debug($"[FSM] 狀態轉換：{oldState} -> {newState}");
                OnStateChanged?.Invoke(oldState, newState);
            }
        }
    }
}
