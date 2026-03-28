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
    /// <summary>
    /// 導航有限狀態機 (FSM) 實作
    /// 負責接收大腦的指令，將邊界條件 (Edge) 交由真正的執行器處理，並嚴格管控狀態。
    /// </summary>
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

        /// <summary>
        /// 嘗試指示狀態機開始執行一段導航邊。
        /// 匹配 INavigationStateMachine 介面
        /// </summary>
        public bool TryStartNavigation(NavigationEdge edge, SdPointF currentPos, SdPointF targetPos)
        {
            lock (_stateLock)
            {
                if (_currentState != NavigationState.Idle && _currentState != NavigationState.Reached_Waypoint)
                {
                    return false;
                }

                _currentTaskCts?.Dispose();
                _currentTaskCts = new CancellationTokenSource();

                NavigationState nextState = edge.ActionType switch
                {
                    NavigationActionType.Walk => NavigationState.Moving_Horizontal,
                    NavigationActionType.ClimbUp => NavigationState.Moving_Vertical,
                    NavigationActionType.ClimbDown => NavigationState.Moving_Vertical,
                    NavigationActionType.Jump => NavigationState.Jumping,
                    NavigationActionType.JumpLeft => NavigationState.Jumping,
                    NavigationActionType.JumpRight => NavigationState.Jumping,
                    NavigationActionType.JumpDown => NavigationState.Jumping,
                    NavigationActionType.Teleport => NavigationState.Transitioning,
                    _ => NavigationState.Moving_Horizontal
                };

                ChangeState(nextState);

                _ = InternalExecuteEdgeAsync(edge, currentPos, targetPos, _currentTaskCts.Token, nextState);

                return true;
            }
        }

        private async Task InternalExecuteEdgeAsync(NavigationEdge edge, SdPointF currentPos, SdPointF targetPos, CancellationToken token, NavigationState actionState)
        {
            try
            {
                Logger.Info($"[FSM] 開始執行 Edge {edge.ActionType}，目標: ({targetPos.X}, {targetPos.Y})");

                var exeResult = await _executor.ExecuteActionAsync(
                    edge,
                    (System.Drawing.PointF)currentPos,
                    (System.Drawing.PointF)targetPos,
                    () => _tracker?.IsPlayerAtTarget() ?? false,
                    token);

                if (exeResult == NavigationExecutor.ExecutionResult.Failed || exeResult == NavigationExecutor.ExecutionResult.Error)
                {
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
                        }
                    }
                    else
                    {
                        Logger.Warning("[FSM] 未配置 PathPlanningTracker，無法執行救援路徑規劃。");
                    }
                    return;
                }

                // 僅當執行器明確回報 Completed 才允許推進 waypoint。
                // 禁止以「動作種類」(Jump/Transition) 直接判定成功，避免假成功導致掉層與路徑飄移。
                if (!token.IsCancellationRequested &&
                    exeResult == NavigationExecutor.ExecutionResult.Completed)
                {
                    if (exeResult == NavigationExecutor.ExecutionResult.Completed)
                    {
                        Logger.Debug("[FSM] 接收到執行層 Success 訊號，立即進入 Reached_Waypoint");
                    }
                    ChangeState(NavigationState.Reached_Waypoint);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("[FSM] 任務已被取消 (例外流退避)。");
            }
            catch (Exception ex)
            {
                Logger.Error($"[FSM] 執行 Edge 發生錯誤: {ex.Message}");
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

        /// <summary>
        /// 緊急中斷所有的導航任務
        /// </summary>
        public void CancelNavigation(string reason = "使用者強制中斷")
        {
            lock (_stateLock)
            {
                if (_currentState == NavigationState.Idle) return;

                Logger.Info($"[FSM] 收到中斷請求，原因：{reason}");

                _currentTaskCts?.Cancel();

                ChangeState(NavigationState.Idle);
            }
        }

        /// <summary>
        /// 通知狀態機角色已經到達目標容許範圍內。
        /// </summary>
        public void NotifyTargetReached()
        {
            lock (_stateLock)
            {
                if (_currentState == NavigationState.Moving_Horizontal || _currentState == NavigationState.Moving_Vertical)
                {
                    bool ssotReached = _tracker?.IsPlayerAtTarget() ?? false;
                    if (!ssotReached)
                    {
                        Logger.Debug("[FSM] 外部通知到達被忽略：SSOT(Hitbox) 尚未成立。");
                        return;
                    }

                    Logger.Debug("[FSM] 外部通知已到達目標，且 SSOT 成立，切換至 Reached_Waypoint");
                    ChangeState(NavigationState.Reached_Waypoint);
                }
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
