using System;
using System.Threading;
using System.Threading.Tasks;
using ArtaleAI.Vision;
using ArtaleAI.Domain.Navigation;
using ArtaleAI.Shared;
using ArtaleAI.Models.Config;
using ArtaleAI.Contracts;
using ArtaleAI.Application.Movement;
using ArtaleAI.Application.Pipeline;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Application.Navigation
{
    /// <summary>將 <see cref="NavigationEdge"/> 轉為實際按鍵與移動，並與視覺幀同步。</summary>
    public class NavigationExecutor
    {
        private readonly IKeyboardService _keyboard;
        private readonly IPlayerPositionProvider _positionProvider;
        private readonly CharacterMovementController _movementController;
        private GamePipeline? _syncProvider;

        private const ushort VK_LEFT = 0x25;
        private const ushort VK_UP = 0x26;
        private const ushort VK_RIGHT = 0x27;
        private const ushort VK_DOWN = 0x28;
        private const ushort VK_JUMP = 0x12; // Alt，與 CharacterMovementController 爬繩一致

        private const float FALL_Y_TOLERANCE = 15.0f;
        private const float JumpVerticalDirectionThresholdPx = 1.5f;

        private const int SideJumpPostLandingSettleMs = 200;
        private const int SideJumpMinAirborneMsBeforeLanding = 450;
        private const float SideJumpLandingYProximityPx = 3.2f;
        private const float SideJumpLandingStableYDeltaPx = 0.85f;

        public enum ExecutionResult
        {
            Completed,
            MovedToward,
            Skipped,
            Failed,
            Error
        }

        private PathPlanningTracker? _pathTracker;

        public NavigationExecutor(
            IKeyboardService keyboard,
            IPlayerPositionProvider positionProvider,
            CharacterMovementController movementController)
        {
            _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
            _positionProvider = positionProvider ?? throw new ArgumentNullException(nameof(positionProvider));
            _movementController = movementController ?? throw new ArgumentNullException(nameof(movementController));
        }

        public void SetSyncProvider(GamePipeline provider)
        {
            _syncProvider = provider;
        }

        public void SetPathTracker(PathPlanningTracker? tracker)
        {
            _pathTracker = tracker;
        }

        private async Task WaitForNextFrameAsync(CancellationToken ct)
        {
            if (_syncProvider != null) await _syncProvider.WaitForNextFrameAsync(ct);
            else await Task.Delay(20, ct);
        }

        public async Task<ExecutionResult> ExecuteActionAsync(
            NavigationEdge edge,
            System.Drawing.PointF currentPos,
            System.Drawing.PointF targetPos,
            Func<bool> isReached,
            CancellationToken ct = default)
        {
            if (edge == null)
            {
                return await ExecuteWalkAsync(currentPos, targetPos, isReached, ct);
            }

            return edge.ActionType switch
            {
                NavigationActionType.ClimbUp =>
                    await ExecuteClimbAsync(edge, currentPos, targetPos, isUp: true, isReached, ct),
                NavigationActionType.ClimbDown =>
                    await ExecuteClimbAsync(edge, currentPos, targetPos, isUp: false, isReached, ct),
                NavigationActionType.JumpDown =>
                    await ExecuteJumpDownAsync(currentPos, targetPos, isReached, ct),
                NavigationActionType.SideJump =>
                    await ExecuteDirectionalJumpAsync(targetPos.X > currentPos.X ? 1 : -1, currentPos, targetPos, isReached, ct),
                NavigationActionType.Jump =>
                    await ExecuteJumpAsync(currentPos, targetPos, isReached, ct),
                NavigationActionType.Teleport =>
                    await ExecuteTeleportAsync(ct),
                _ => await ExecuteWalkAsync(currentPos, targetPos, isReached, ct)
            };
        }


        private async Task<ExecutionResult> ExecuteWalkAsync(
            SdPointF currentPos, SdPointF targetPos, Func<bool> isReached, CancellationToken ct)
        {
            bool success = await MoveToTargetWithCorrectionAsync(
               targetPos,
               FALL_Y_TOLERANCE,
               isReached,
               ct);
            return success ? ExecutionResult.Completed : ExecutionResult.Failed;
        }

        /// <summary>走路至目標；長按後必經微步 X 對齊，再以 Hitbox 驗收 waypoint。</summary>
        private async Task<bool> MoveToTargetWithCorrectionAsync(
            SdPointF targetPos, float fallToleranceY, Func<bool> isReached, CancellationToken ct)
        {
            var walkContext = _pathTracker?.GetWalkPlatformContextForActiveFlight();

            var moveResult = await _movementController.MoveToTargetAsync(
                targetPos,
                () => _positionProvider.GetCurrentPosition(),
                fallToleranceY,
                ct,
                walkContext);

            if (moveResult == MovementResult.Failed)
                return false;

            bool aligned = await _movementController.AlignHorizontallyAsync(
                targetPos.X,
                () => _positionProvider.GetCurrentPosition(),
                ct,
                walkContext);

            if (!aligned)
            {
                var pos = _positionProvider.GetCurrentPosition();
                if (pos.HasValue)
                    Logger.Warning($"[導航] 微步 X 對齊未完全達標 playerX={pos.Value.X:F1} targetX={targetPos.X:F1}");
            }

            bool arrived = isReached();
            var p = _positionProvider.GetCurrentPosition();
            if (p.HasValue)
            {
                if (arrived)
                    _pathTracker?.LogFrozenFlightArrivalDiagnostic(p.Value, asWarning: false);
                else
                {
                    Logger.Info($"[導航] Walk 驗收未通過 player=({p.Value.X:F1},{p.Value.Y:F1}) target=({targetPos.X:F1},{targetPos.Y:F1})");
                    _pathTracker?.LogFrozenFlightArrivalDiagnostic(p.Value, asWarning: true);
                }
            }

            return arrived;
        }

        private async Task<ExecutionResult> ExecuteClimbAsync(
            NavigationEdge edge, SdPointF currentPos, SdPointF targetPos,
            bool isUp, Func<bool> isReached, CancellationToken ct)
        {
            const float ropeAlignThreshold = 1.0f;
            if (!NavigationRopeHelper.TryExtractRopeX(edge, out float ropeX))
            {
                Logger.Error(
                    $"[爬梯] Climb 邊缺少 ropeX metadata：{edge.FromNodeId}->{edge.ToNodeId}");
                return ExecutionResult.Error;
            }
            float ropeTargetY = targetPos.Y;
            float distanceToRopeX = Math.Abs(currentPos.X - ropeX);

            if (distanceToRopeX > ropeAlignThreshold)
            {
                Logger.Info($"[爬梯前置] 水平位移至梯子 (X={ropeX:F1})，當前 X={currentPos.X:F1}");
                var ropeTarget = new SdPointF(ropeX, currentPos.Y);

                bool IsAlignedToRopeX()
                {
                    var p = _positionProvider.GetCurrentPosition();
                    return p.HasValue && Math.Abs(p.Value.X - ropeX) <= ropeAlignThreshold;
                }

                bool moveSuccess = await MoveToTargetWithCorrectionAsync(
                    ropeTarget,
                    FALL_Y_TOLERANCE,
                    IsAlignedToRopeX,
                    ct);

                if (!moveSuccess) return ExecutionResult.Failed;
            }

            var playerPosF = _positionProvider.GetCurrentPosition() ?? currentPos;
            float climbDistance = Math.Abs(ropeTargetY - playerPosF.Y);
            bool climbSuccess = await _movementController.ClimbRopeAsync(
                playerPosF,
                ropeX,
                ropeTargetY,
                () => _positionProvider.GetCurrentPosition() ?? playerPosF,
                isReached, 
                ct,
                isUp
            );

            return climbSuccess ? ExecutionResult.Completed : ExecutionResult.Failed;
        }

        /// <summary>與 SideJump 左右推斷對稱：targetY 明顯較低（螢幕座標較大）→ 下跳。</summary>
        private static bool IsDownwardJump(SdPointF from, SdPointF to)
            => to.Y > from.Y + JumpVerticalDirectionThresholdPx;

        private async Task<ExecutionResult> ExecuteJumpAsync(
            SdPointF currentPos, SdPointF targetPos, Func<bool> isReached, CancellationToken ct)
        {
            if (IsDownwardJump(currentPos, targetPos))
            {
                Logger.Info($"[跳躍] 幾何判定為下跳 currentY={currentPos.Y:F1} targetY={targetPos.Y:F1}");
                return await ExecuteJumpDownAsync(currentPos, targetPos, isReached, ct);
            }

            return await ExecuteDirectionalJumpAsync(0, currentPos, targetPos, isReached, ct);
        }

        private const int JumpDownDirectionLeadMs = 45;
        private const int JumpDownMinAirborneMsBeforeLanding = 120;
        private const float JumpDownTakeoffDeltaPx = 2.0f;

        /// <summary>下跳：先按住 ↓，lead 後再按 Alt；Alt 放開後才放 ↓（與 SideJump 方向 lead 對稱）。</summary>
        private async Task<ExecutionResult> ExecuteJumpDownAsync(
            SdPointF currentPos, SdPointF targetPos, Func<bool> isReached, CancellationToken ct)
        {
            _movementController.StopMovement();
            await Task.Delay(50, ct);

            _movementController.FocusGameWindow();

            _keyboard.HoldKey(VK_DOWN);
            await Task.Delay(JumpDownDirectionLeadMs, ct);

            _keyboard.SendKey(VK_JUMP, false);

            int jumpHold = AppConfig.Instance.Navigation.SideJumpAltHoldMs;
            await Task.Delay(jumpHold, ct);

            _keyboard.SendKey(VK_JUMP, true);
            await Task.Delay(65, ct);
            _keyboard.SendKey(VK_DOWN, true);

            int landingWaitMs = AppConfig.Instance.Navigation.StuckDetectionMs;
            bool landedByProximity = await WaitForLandingAsync(
                targetPos.Y,
                landingWaitMs,
                ct,
                JumpDownMinAirborneMsBeforeLanding,
                JumpDownTakeoffDeltaPx);

            if (!landedByProximity)
                Logger.Warning($"[下跳] WaitForLanding 逾時（{landingWaitMs}ms），將進行著陸驗收。targetY={targetPos.Y:F1}");

            if (SideJumpPostLandingSettleMs > 0)
                await Task.Delay(SideJumpPostLandingSettleMs, ct);

            var landPos = _positionProvider.GetCurrentPosition();
            if (landPos.HasValue)
            {
                float fallYTol = FALL_Y_TOLERANCE;
                float currentLandY = landPos.Value.Y;
                float absYDiff = Math.Abs(currentLandY - targetPos.Y);

                bool isDownwardMission = targetPos.Y > currentPos.Y;
                bool landedOnLowerPlatform = currentLandY >= targetPos.Y - 2.0f;

                if (isDownwardMission && landedOnLowerPlatform && isReached())
                {
                    Logger.Info($"[下跳] 偵測到下跳成功！落點({currentLandY:F1}) 到達目標層({targetPos.Y:F1})。");
                    return ExecutionResult.Completed;
                }

                if (absYDiff > fallYTol)
                {
                    Logger.Warning($"[下跳] 著地 Y 偏差過大，判定失敗。diff={absYDiff:F1}, tol={fallYTol:F1}");
                    return ExecutionResult.Failed;
                }

                if (!isReached())
                {
                    _pathTracker?.LogFrozenFlightArrivalDiagnostic(landPos.Value, asWarning: true);
                    return ExecutionResult.Failed;
                }
            }

            return ExecutionResult.Completed;
        }

        private async Task<ExecutionResult> ExecuteDirectionalJumpAsync(
            int direction, SdPointF currentPos, SdPointF targetPos, Func<bool> isReached, CancellationToken ct)
        {
            int effectiveDirection = direction;
            float dxToTarget = targetPos.X - currentPos.X;
            if (Math.Abs(dxToTarget) > 1.0f)
            {
                int geometricDirection = dxToTarget > 0 ? 1 : -1;
                if (direction != 0 && direction != geometricDirection)
                {
                    Logger.Warning($"[跳躍] 邊方向與目標方向衝突，已自動修正。edgeDir={direction}, geoDir={geometricDirection}, currentX={currentPos.X:F1}, targetX={targetPos.X:F1}");
                }
                effectiveDirection = geometricDirection;
            }

            _movementController.StopMovement();
            await Task.Delay(50, ct); // 原子化靜止等待 (固定值)
            
            ushort targetKey = effectiveDirection < 0 ? VK_LEFT : (effectiveDirection > 0 ? VK_RIGHT : (ushort)0);
            
            _movementController.FocusGameWindow();

            if (targetKey != 0)
            {
                _keyboard.SendKey(targetKey, false);
            }
            
            await Task.Delay(45, ct); // 原子化 Direction-Lead (固定值)

            _keyboard.SendKey(VK_JUMP, false);
            
            int jumpHold = AppConfig.Instance.Navigation.SideJumpAltHoldMs;
            await Task.Delay(jumpHold, ct);

            _keyboard.SendKey(VK_JUMP, true);

            await Task.Delay(65, ct); // 原子化 Release-Direction (固定值)
            
            if (targetKey != 0)
                _keyboard.SendKey(targetKey, true);

            int landingWaitMs = AppConfig.Instance.Navigation.StuckDetectionMs;
            bool landedByProximity = await WaitForLandingAsync(targetPos.Y, landingWaitMs, ct);
            
            if (!landedByProximity)
                Logger.Warning($"[跳躍] WaitForLanding 逾時（{landingWaitMs}ms），將進行著陸驗收。targetY={targetPos.Y:F1}");

            if (SideJumpPostLandingSettleMs > 0)
                await Task.Delay(SideJumpPostLandingSettleMs, ct);

            var landPos = _positionProvider.GetCurrentPosition();
            if (landPos.HasValue)
            {
                float fallYTol = FALL_Y_TOLERANCE;
                float currentLandY = landPos.Value.Y;
                float absYDiff = Math.Abs(currentLandY - targetPos.Y);

                // [優化] 向上的躍遷寬度判定
                // 如果是「向上跳躍」，且最終落點比目標 Y 軸更「高」(數值更小)，則視為順利到達上層平台
                bool isUpwardMission = targetPos.Y < currentPos.Y;
                bool landedHigherThanTarget = currentLandY <= targetPos.Y + 2.0f; // 包含 2px 微小容差

                if (isUpwardMission && landedHigherThanTarget && isReached())
                {
                    Logger.Info($"[跳躍] 偵測到向上躍遷成功！落點({currentLandY:F1}) 高於目標({targetPos.Y:F1})。判定 Success。");
                    return ExecutionResult.Completed;
                }

                if (absYDiff > fallYTol)
                {
                    Logger.Warning($"[跳躍] 著地 Y 偏差過大，判定失敗。diff={absYDiff:F1}, tol={fallYTol:F1}");
                    return ExecutionResult.Failed;
                }

                if (!isReached())
                {
                    if (landPos.HasValue)
                    {
                        float landXErr = Math.Abs(landPos.Value.X - targetPos.X);
                        Logger.Warning(
                            $"[跳躍診斷] 著地驗收未通過 land=({landPos.Value.X:F1},{landPos.Value.Y:F1}) " +
                            $"target=({targetPos.X:F1},{targetPos.Y:F1}) xErr={landXErr:F2}");
                        _pathTracker?.LogFrozenFlightArrivalDiagnostic(landPos.Value, asWarning: true);
                    }
                    return ExecutionResult.Failed;
                }
            }
            return ExecutionResult.Completed;
        }

        private async Task<ExecutionResult> ExecuteTeleportAsync(CancellationToken ct)
        {
            _keyboard.SendKey(VK_UP, false);
            await Task.Delay(150, ct);
            _keyboard.SendKey(VK_UP, true);
            await Task.Delay(500, ct);  
            return ExecutionResult.Completed;
        }

        private async Task<bool> WaitForLandingAsync(
            float targetY,
            int timeoutMs,
            CancellationToken ct,
            int minAirborneMs = SideJumpMinAirborneMsBeforeLanding,
            float takeoffDeltaPx = 5.0f)
        {
            var startTime = DateTime.UtcNow;
            float lastY = float.MinValue;
            int stableCounter = 0;
            const int StableThreshold = 3;
            float initialY = _positionProvider.GetCurrentPosition()?.Y ?? -9999f;
            bool hasTakenOff = false;
            DateTime? takeoffUtc = null;
            float yBand = SideJumpLandingYProximityPx;
            float stableYDelta = SideJumpLandingStableYDeltaPx;

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs && !ct.IsCancellationRequested)
            {
                var currentPos = _positionProvider.GetCurrentPosition();
                if (currentPos.HasValue)
                {
                    float currentY = currentPos.Value.Y;
                    if (!hasTakenOff && Math.Abs(currentY - initialY) > takeoffDeltaPx)
                    {
                        hasTakenOff = true;
                        takeoffUtc = DateTime.UtcNow;
                    }

                    bool airborneLongEnough = !takeoffUtc.HasValue ||
                        (DateTime.UtcNow - takeoffUtc.Value).TotalMilliseconds >= minAirborneMs;

                    if (hasTakenOff && airborneLongEnough && Math.Abs(currentY - targetY) <= yBand)
                    {
                        if (Math.Abs(currentY - lastY) < stableYDelta)
                        {
                            stableCounter++;
                            if (stableCounter >= StableThreshold) return true;
                        }
                        else stableCounter = 0;
                    }
                    lastY = currentY;
                }
                await WaitForNextFrameAsync(ct);
            }

            return false;
        }
    }
}
