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
    /// <summary>
    /// 導航動作執行器 — Application 層的核心協調器。
    /// </summary>
    public class NavigationExecutor
    {
        private readonly IKeyboardService _keyboard;
        private readonly IPlayerPositionProvider _positionProvider;
        private readonly CharacterMovementController _movementController;
        private GamePipeline? _syncProvider; // 視覺同步提供者

        private const ushort VK_LEFT = 0x25;
        private const ushort VK_UP = 0x26;
        private const ushort VK_RIGHT = 0x27;
        private const ushort VK_DOWN = 0x28;
        private const ushort VK_LMENU = 0xA4;  

        private const float FALL_Y_TOLERANCE = 15.0f;

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
                    await ExecuteDirectionalJumpAsync(-1, currentPos, targetPos, ct),
                NavigationActionType.JumpRight =>
                    await ExecuteDirectionalJumpAsync(1, currentPos, targetPos, ct),
                NavigationActionType.Jump =>
                    await ExecuteDirectionalJumpAsync(0, currentPos, targetPos, ct),
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

        /// <summary>
        /// 配合預測性煞車架構進行重構：移除固定延遲。
        /// SSOT：結合大腦實時感知 (isReached) 進行物理熔斷。
        /// </summary>
        private async Task<bool> MoveToTargetWithCorrectionAsync(
            SdPointF targetPos, float fallToleranceY, Func<bool> isReached, CancellationToken ct)
        {
            var moveResult = await _movementController.MoveToTargetAsync(
                targetPos,
                () => _positionProvider.GetCurrentPosition(),
                fallToleranceY,
                isReached,
                ct);

            // 移動層若已回報 Failed / NeedsCorrection，不可僅以 isReached() 覆寫（避免假陰性/假陽性與執行結果不一致）。
            if (moveResult != MovementResult.Success)
                return false;

            return isReached();
        }

        private async Task<ExecutionResult> ExecuteClimbAsync(
            NavigationEdge edge, SdPointF currentPos, SdPointF targetPos,
            bool isUp, Func<bool> isReached, CancellationToken ct)
        {
            // 繩索無路徑節點 Hitbox；此段 SSOT 僅能由呼叫端委派表達。X 對齊容許量與設定的走路放鍵距離取較小者，避免預設 10px 過寬導致無法貼繩。
            float ropeAlignTol = Math.Min((float)AppConfig.Instance.Navigation.WaypointReachDistance, 2.0f);
            float ropeX = ExtractRopeX(edge, targetPos.X);
            float ropeTargetY = targetPos.Y;
            float distanceToRopeX = Math.Abs(currentPos.X - ropeX);

            if (distanceToRopeX > ropeAlignTol)
            {
                var ropeTarget = new SdPointF(ropeX, currentPos.Y);

                bool IsAlignedToRopeX()
                {
                    var p = _positionProvider.GetCurrentPosition();
                    return p.HasValue && Math.Abs(p.Value.X - ropeX) <= ropeAlignTol;
                }

                bool moveSuccess = await MoveToTargetWithCorrectionAsync(
                    ropeTarget,
                    FALL_Y_TOLERANCE,
                    IsAlignedToRopeX,
                    ct);

                if (!moveSuccess) return ExecutionResult.Failed;
                // 🚀 移除 100ms 冗餘延後，MoveToTargetWithCorrectionAsync 已保證穩定
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
            int direction, SdPointF currentPos, SdPointF targetPos, CancellationToken ct)
        {
            // 方案 B + 執行層防呆：
            // 若邊的 Jump 方向與幾何目標方向衝突，優先採用幾何方向，避免資料誤標導致反向跳躍。
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

            ushort directionKey = effectiveDirection < 0 ? VK_LEFT : (effectiveDirection > 0 ? VK_RIGHT : (ushort)0);
            if (directionKey != 0) _keyboard.SendKey(directionKey, false);
            int windupMs = Math.Max(0, AppConfig.Instance.Navigation.DirectionalJumpDirectionHoldBeforeAltMs);
            await Task.Delay(windupMs, ct);
            // 先按下跳鍵，再放開，避免出現「只有方向鍵、沒有真正起跳」的假跳躍。
            _keyboard.SendKey(VK_LMENU, false);
            int altHold = Math.Max(15, AppConfig.Instance.Navigation.DirectionalJumpAltHoldMs);
            await Task.Delay(altHold, ct);
            _keyboard.SendKey(VK_LMENU, true);
            int landingWaitMs = Math.Max(500, AppConfig.Instance.Navigation.DirectionalJumpLandingWaitTimeoutMs);
            bool landedByProximity = await WaitForLandingAsync(targetPos.Y, landingWaitMs, ct);
            if (!landedByProximity)
                Logger.Warning($"[跳躍] WaitForLanding 逾時（{landingWaitMs}ms），將放開方向鍵並進行著陸驗收。targetY={targetPos.Y:F1}");
            if (directionKey != 0) _keyboard.SendKey(directionKey, true); // 在空中位移結束後放鍵

            // 著陸後短暫穩定：再讀座標驗收，避免慣性／平台邊緣滑動導致誤判 Completed
            int settleMs = AppConfig.Instance.Navigation.PostLandingSettleMs;
            if (settleMs > 0)
                await Task.Delay(settleMs, ct);

            var landPos = _positionProvider.GetCurrentPosition();
            if (landPos.HasValue)
            {
                float landDy = Math.Abs(landPos.Value.Y - targetPos.Y);
                if (landDy > FALL_Y_TOLERANCE)
                {
                    Logger.Warning($"[跳躍] 著地 Y 偏差過大，判定失敗。landDy={landDy:F1}, tol={FALL_Y_TOLERANCE:F1}, landY={landPos.Value.Y:F1}, targetY={targetPos.Y:F1}");
                    return ExecutionResult.Failed;
                }

                // 跳躍必須同時滿足 X 向落點；閾值取 WaypointReachDistance 與設定之下限的較大者（取代硬編碼 4px）。
                float xTolerance = (float)Math.Max(
                    AppConfig.Instance.Navigation.WaypointReachDistance,
                    AppConfig.Instance.Navigation.JumpLandingTolerancePx);
                float landDx = Math.Abs(landPos.Value.X - targetPos.X);
                if (landDx > xTolerance)
                {
                    Logger.Warning($"[跳躍] 著地 X 偏差過大，判定失敗。landDx={landDx:F1}, tol={xTolerance:F1}, landX={landPos.Value.X:F1}, targetX={targetPos.X:F1}");
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

        /// <returns>true 表示在逾時前已偵測到穩定著陸帶；false 表示逾時（仍須做後續座標驗收）。</returns>
        private async Task<bool> WaitForLandingAsync(float targetY, int timeoutMs, CancellationToken ct)
        {
            var startTime = DateTime.UtcNow;
            float lastY = float.MinValue;
            int stableCounter = 0;
            const int StableThreshold = 3;
            float initialY = _positionProvider.GetCurrentPosition()?.Y ?? -9999f;
            bool hasTakenOff = false;
            DateTime? takeoffUtc = null;
            int minAirborneMs = Math.Max(0, AppConfig.Instance.Navigation.MinAirborneMsBeforeLanding);
            float yBand = (float)Math.Max(1.0, AppConfig.Instance.Navigation.DirectionalJumpLandingYProximityPx);
            float stableYDelta = (float)Math.Max(0.05, AppConfig.Instance.Navigation.DirectionalJumpLandingStableYDeltaPx);

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
                        // 首幀 lastY 為 MinValue，差值極大，自然不會誤累積；之後以較寬鬆 delta 容忍板緣微滑（見設定說明）。
                        if (Math.Abs(currentY - lastY) < stableYDelta)
                        {
                            stableCounter++;
                            if (stableCounter >= StableThreshold) return true;
                        }
                        else stableCounter = 0;
                    }
                    lastY = currentY;
                }
                await WaitForNextFrameAsync(ct); // 配合視覺同步偵測著地
            }

            return false;
        }
    }
}
