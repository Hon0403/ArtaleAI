using System;
using System.Threading;
using System.Threading.Tasks;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Utils;
using ArtaleAI.Models.Config;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
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
        private const ushort VK_LMENU = 0xA4;  

        private const float FALL_Y_TOLERANCE = 15.0f;

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
                    await ExecuteJumpDownAsync(ct),
                NavigationActionType.JumpLeft =>
                    await ExecuteDirectionalJumpAsync(-1, currentPos, targetPos, isReached, ct),
                NavigationActionType.JumpRight =>
                    await ExecuteDirectionalJumpAsync(1, currentPos, targetPos, isReached, ct),
                NavigationActionType.Jump =>
                    await ExecuteDirectionalJumpAsync(0, currentPos, targetPos, isReached, ct),
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

        /// <summary>走路至目標；移動層非 <see cref="MovementResult.Success"/> 時直接失敗，不以 isReached 覆寫。</summary>
        private async Task<bool> MoveToTargetWithCorrectionAsync(
            SdPointF targetPos, float fallToleranceY, Func<bool> isReached, CancellationToken ct)
        {
            var moveResult = await _movementController.MoveToTargetAsync(
                targetPos,
                () => _positionProvider.GetCurrentPosition(),
                fallToleranceY,
                isReached,
                ct);

            if (moveResult == MovementResult.Failed)
                return false;

            if (!isReached())
            {
                Logger.Info("[導航] 未進 Hitbox，啟動微步對齊...");
                var p = _positionProvider.GetCurrentPosition();
                if (p.HasValue)
                {
                    float dx = targetPos.X - p.Value.X;
                    ushort corrKey = dx > 0 ? VK_RIGHT : VK_LEFT;
                    for (int i = 0; i < 8; i++)
                    {
                        if (isReached()) break;
                        await _keyboard.TapKeyAsync(corrKey, 30, ct);
                        await WaitForNextFrameAsync(ct);
                    }
                }
            }

            return isReached();
        }

        private async Task<ExecutionResult> ExecuteClimbAsync(
            NavigationEdge edge, SdPointF currentPos, SdPointF targetPos,
            bool isUp, Func<bool> isReached, CancellationToken ct)
        {
            const float ropeAlignThreshold = 1.0f; // 統一門檻 (1.0px)
            float ropeX = ExtractRopeX(edge, targetPos.X);
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

        private async Task<ExecutionResult> ExecuteJumpDownAsync(CancellationToken ct)
        {
            _keyboard.SendKey(VK_DOWN, false);      
            await Task.Delay(50, ct);
            _keyboard.SendKey(VK_LMENU, false);     
            await Task.Delay(100, ct);
            _keyboard.SendKey(VK_LMENU, true);      
            await Task.Delay(50, ct);
            _keyboard.SendKey(VK_DOWN, true);        
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

            _keyboard.SendKey(VK_LMENU, false); // Alt
            
            int jumpHold = AppConfig.Instance.Navigation.SideJumpAltHoldMs;
            await Task.Delay(jumpHold, ct);

            _keyboard.SendKey(VK_LMENU, true);

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
                float landDy = Math.Abs(landPos.Value.Y - targetPos.Y);
                
                if (landDy > fallYTol)
                {
                    Logger.Warning($"[跳躍] 著地 Y 偏差過大，判定失敗。landDy={landDy:F1}, tol={fallYTol:F1}");
                    return ExecutionResult.Failed;
                }

                if (!isReached())
                {
                    Logger.Warning($"[跳躍] 著地位置不在目標 Hitbox 內，判定失敗。landPos={landPos.Value:F1}, targetPos={targetPos:F1}");
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

        private static float ExtractRopeX(NavigationEdge edge, float fallbackX)
        {
            if (edge.InputSequence == null) return fallbackX;
            foreach (var seq in edge.InputSequence)
            {
                if (seq.StartsWith("ropeX:") && float.TryParse(seq.Substring(6), out float ropeX))
                    return ropeX;
            }
            return fallbackX;
        }


        private async Task<bool> WaitForLandingAsync(float targetY, int timeoutMs, CancellationToken ct)
        {
            var startTime = DateTime.UtcNow;
            float lastY = float.MinValue;
            int stableCounter = 0;
            const int StableThreshold = 3;
            float initialY = _positionProvider.GetCurrentPosition()?.Y ?? -9999f;
            bool hasTakenOff = false;
            DateTime? takeoffUtc = null;
            int minAirborneMs = SideJumpMinAirborneMsBeforeLanding;
            float yBand = SideJumpLandingYProximityPx;
            float stableYDelta = SideJumpLandingStableYDeltaPx;

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs && !ct.IsCancellationRequested)
            {
                var currentPos = _positionProvider.GetCurrentPosition();
                if (currentPos.HasValue)
                {
                    float currentY = currentPos.Value.Y;
                    if (!hasTakenOff && Math.Abs(currentY - initialY) > 5.0f)
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
