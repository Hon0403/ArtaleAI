using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using ArtaleAI.Models.Config;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Domain.Navigation;
using ArtaleAI.Shared;
using ArtaleAI.Contracts;
using ArtaleAI.Application.Pipeline;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Application.Movement
{
    /// <summary>SendInput 封裝與走路／爬繩等連續移動行為。</summary>
    public class CharacterMovementController : IKeyboardService, IDisposable
    {
        public void SetGameWindowTitle(string windowTitle)
        {
            _gameWindowTitle = windowTitle ?? "";
        }

        private bool _isDisposed = false;
        private readonly object _lockObject = new object();
        private ushort _currentPressedKey = 0; 
        private string _gameWindowTitle = ""; 
        private GamePipeline? _syncProvider;

        private static readonly int _cachedInputSize = CalculateInputSize();

        private static int CalculateInputSize()
        {
            bool is64Bit = IntPtr.Size == 8;
            int expectedSize = is64Bit ? 40 : 28; 
            int actualSize = Marshal.SizeOf(typeof(INPUT));
            return actualSize == expectedSize ? actualSize : expectedSize;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("kernel32.dll")]
        private static extern uint GetLastError();



        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9; 


        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;      
            public INPUTUNION U;   
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;          
            public ushort wScan;        
            public uint dwFlags;        
            public uint time;           
            public IntPtr dwExtraInfo;  
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        private const ushort VK_UP = 0x26;      
        private const ushort VK_DOWN = 0x28;    
        private const ushort VK_LEFT = 0x25;    
        private const ushort VK_RIGHT = 0x27;

        public void SetSyncProvider(GamePipeline provider)
        {
            _syncProvider = provider;
            Logger.Info("[MovementController] 已綁定視覺同步提供者");
        }

        private async Task WaitForNextFrameAsync(CancellationToken ct)
        {
            if (_syncProvider != null)
            {
                await _syncProvider.WaitForNextFrameAsync(ct);
            }
            else
            {
                await Task.Delay(16, ct);
            }
        }


        private bool IsNavigationInputBlocked() => _syncProvider?.BlocksNavigationInput == true;

        private async Task YieldNavigationInputAsync(CancellationToken cancellationToken)
        {
            StopMovement();
            await WaitForNextFrameAsync(cancellationToken);
        }


        private static float WalkAlignTolerancePx =>
            (float)AppConfig.Instance.Navigation.WalkAlignTolerancePx;

        private static float WalkBrakeDistancePx =>
            (float)AppConfig.Instance.Navigation.WalkBrakeDistancePx;

        /// <summary>統一的繩索對位門檻半徑 (1.0px)，供垂直移動發動前使用。</summary>
        private static float GetRopeAlignTolerancePx() => WalkAlignTolerancePx;

        /// <summary>長按走向目標 X；以座標對齊與煞車區停止，不再以 Hitbox 熔斷提前停步。</summary>
        public async Task<MovementResult> MoveToTargetAsync(
            SdPointF targetPos,
            Func<SdPointF?> getPlayerPosition,
            float fallToleranceY,
            CancellationToken cancellationToken = default,
            WalkPlatformContext? platformContext = null)
        {
            if (_isDisposed) return MovementResult.Failed;

            if (targetPos.X < 0 || targetPos.Y < 0)
            {
                Logger.Warning($"[移動] 忽略無效目標 {targetPos}，停止移動");
                StopMovement();
                return MovementResult.Failed;
            }

            SdPointF? initialPosNullable = null;
            for (int i = 0; i < 20; i++) 
            {
                initialPosNullable = getPlayerPosition();
                if (initialPosNullable.HasValue) break;
                await WaitForNextFrameAsync(cancellationToken);
            }

            if (!initialPosNullable.HasValue)
            {
                Logger.Warning("[移動] 啟動失敗：無法獲取初始玩家座標。");
                return MovementResult.Failed;
            }

            var initialPos = initialPosNullable.Value;
            float initialY = initialPos.Y;
            float initialDx = targetPos.X - initialPos.X;
            int expectedSignX = Math.Sign(initialDx);
            ushort targetKey = expectedSignX > 0 ? VK_RIGHT : VK_LEFT;
            float initialAdx = Math.Abs(initialDx);
            string walkDir = expectedSignX > 0 ? "右" : (expectedSignX < 0 ? "左" : "無");

            Logger.Info(
                $"[移動診斷] Walk 啟動 from=({initialPos.X:F1},{initialPos.Y:F1}) target=({targetPos.X:F1},{targetPos.Y:F1}) " +
                $"dir={walkDir} distX={initialAdx:F1}px platform={platformContext?.PlatformId ?? "-"} node={platformContext?.NodeId ?? "-"}");

            var visionLossWatcher = new System.Diagnostics.Stopwatch();
            float lastAdx = initialAdx;
            int divergingFrames = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (IsNavigationInputBlocked())
                    {
                        await YieldNavigationInputAsync(cancellationToken);
                        continue;
                    }

                    var currentPosNullable = getPlayerPosition();
                    if (!currentPosNullable.HasValue)
                    {
                        StopMovement();
                        if (!visionLossWatcher.IsRunning) visionLossWatcher.Start();
                        if (visionLossWatcher.ElapsedMilliseconds > 1500)
                        {
                            Logger.Error("[移動] 視覺丟失超過 1.5 秒，緊急停鍵。");
                            return MovementResult.Failed;
                        }
                        await WaitForNextFrameAsync(cancellationToken);
                        continue;
                    }
                    if (visionLossWatcher.IsRunning) visionLossWatcher.Reset();

                    var currentPos = currentPosNullable.Value;
                    float dY = currentPos.Y - initialY;

                    if (Math.Abs(dY) > fallToleranceY)
                    {
                        LogWalkFallDiagnostic(
                            initialPos, currentPos, targetPos, initialY, dY, fallToleranceY,
                            walkDir, expectedSignX, platformContext);
                        return MovementResult.Failed;
                    }

                    float currentDx = targetPos.X - currentPos.X;
                    float adx = Math.Abs(currentDx);

                    if (adx > lastAdx + 0.4f)
                    {
                        divergingFrames++;
                        if (divergingFrames >= 3)
                        {
                            StopMovement();
                            Logger.Warning(
                                $"[移動診斷] 走離目標中止 distX {lastAdx:F1}->{adx:F1} dir={walkDir} " +
                                $"player=({currentPos.X:F1},{currentPos.Y:F1}) targetX={targetPos.X:F1} " +
                                $"attribution=程式-走離目標 platform={platformContext?.PlatformId ?? "-"}");
                            return MovementResult.Failed;
                        }
                    }
                    else
                    {
                        divergingFrames = 0;
                    }
                    lastAdx = adx;

                    LogWalkPlatformDriftIfNeeded(currentPos, platformContext, walkDir, targetPos.X);

                    if (adx <= WalkAlignTolerancePx)
                    {
                        StopMovement();
                        Logger.Info($"[移動] X 軸對齊完成 (誤差:{adx:F2}px)");
                        break;
                    }

                    if (adx <= WalkBrakeDistancePx)
                    {
                        StopMovement();
                        Logger.Info($"[移動] 進入煞車區 (剩餘:{adx:F2}px)，交由微步精準對位。");
                        break;
                    }

                    if (Math.Sign(currentDx) != expectedSignX && Math.Sign(currentDx) != 0)
                    {
                        StopMovement();
                        Logger.Warning(
                            $"[移動診斷] 越界 overshoot adx={adx:F2}px playerX={currentPos.X:F1} targetX={targetPos.X:F1} " +
                            $"attribution=程式-走過頭");
                        return MovementResult.Overshot;
                    }

                    FocusGameWindow();
                    HoldKey(targetKey);
                    await WaitForNextFrameAsync(cancellationToken);
                }

                return MovementResult.Success;
            }
            finally
            {
                StopMovement();
            }
        }





        private static float _lastDriftLogY;

        private static void LogWalkPlatformDriftIfNeeded(
            PointF playerPos,
            WalkPlatformContext? platformContext,
            string walkDir,
            float targetX)
        {
            if (platformContext?.Geometry == null || string.IsNullOrEmpty(platformContext.PlatformId))
                return;

            if (!platformContext.Geometry.TryProjectStandY(
                    platformContext.PlatformId, playerPos.X, out float projectedY, out bool extrapolated))
                return;

            float yDrift = playerPos.Y - projectedY;
            float yTol = (float)AppConfig.Instance.Navigation.SlopeStandYTolerancePx;
            if (Math.Abs(yDrift) <= yTol)
                return;

            if (Math.Abs(playerPos.Y - _lastDriftLogY) < 0.5f)
                return;

            _lastDriftLogY = playerPos.Y;
            string attribution = extrapolated ? "標記-折線外推區" : "程式-離開平台面";
            Logger.Warning(
                $"[移動診斷] 平台Y漂移 player=({playerPos.X:F1},{playerPos.Y:F1}) projectedY={projectedY:F1} " +
                $"yDrift={yDrift:F1} tol={yTol:F1} extrapolated={extrapolated} dir={walkDir} targetX={targetX:F1} " +
                $"platform={platformContext.PlatformId} attribution={attribution}");
        }

        private static void LogWalkFallDiagnostic(
            PointF initialPos,
            PointF currentPos,
            SdPointF targetPos,
            float initialY,
            float dY,
            float fallToleranceY,
            string walkDir,
            int expectedSignX,
            WalkPlatformContext? platformContext)
        {
            float? projectedY = null;
            bool extrapolated = false;
            float? yDrift = null;
            if (platformContext?.Geometry != null &&
                !string.IsNullOrEmpty(platformContext.PlatformId) &&
                platformContext.Geometry.TryProjectStandY(
                    platformContext.PlatformId, currentPos.X, out float py, out extrapolated))
            {
                projectedY = py;
                yDrift = currentPos.Y - py;
            }

            string yAttribution = yDrift.HasValue && Math.Abs(yDrift.Value) > (float)AppConfig.Instance.Navigation.SlopeStandYTolerancePx
                ? (extrapolated ? "標記-邊緣外+離開平台面" : "程式-離開平台面")
                : "程式-垂直偏移";

            Logger.Warning(
                $"[移動診斷] 墜落中止 dY={Math.Abs(dY):F1} tol={fallToleranceY:F1} dir={walkDir} " +
                $"from=({initialPos.X:F1},{initialY:F1}) now=({currentPos.X:F1},{currentPos.Y:F1}) target=({targetPos.X:F1},{targetPos.Y:F1}) " +
                $"projectedY={(projectedY.HasValue ? projectedY.Value.ToString("F1") : "-")} yDrift={(yDrift.HasValue ? yDrift.Value.ToString("F1") : "-")} " +
                $"platform={platformContext?.PlatformId ?? "-"} node={platformContext?.NodeId ?? "-"} attribution={yAttribution}");
        }

        /// <summary>短按微步將 X 對齊至 targetX；每步重算方向以修正 overshoot。</summary>
        public async Task<bool> AlignHorizontallyAsync(
            float targetX,
            Func<SdPointF?> getPlayerPosition,
            CancellationToken cancellationToken = default,
            WalkPlatformContext? platformContext = null)
        {
            const int maxSteps = 16;

            for (int i = 0; i < maxSteps && !cancellationToken.IsCancellationRequested; i++)
            {
                if (IsNavigationInputBlocked())
                {
                    await YieldNavigationInputAsync(cancellationToken);
                    i--;
                    continue;
                }

                var pos = getPlayerPosition();
                if (!pos.HasValue)
                {
                    await WaitForNextFrameAsync(cancellationToken);
                    continue;
                }

                float dx = targetX - pos.Value.X;
                float adx = Math.Abs(dx);
                if (adx <= WalkAlignTolerancePx)
                {
                    Logger.Info($"[移動] 微步對齊完成 (誤差:{adx:F2}px)");
                    return true;
                }

                ushort key = dx > 0 ? VK_RIGHT : VK_LEFT;
                int tapMs = adx > 2f ? 30 : 20;
                FocusGameWindow();
                await TapKeyAsync(key, tapMs, 60, cancellationToken);
                await WaitForNextFrameAsync(cancellationToken);
            }

            var finalPos = getPlayerPosition();
            if (finalPos.HasValue)
            {
                float finalAdx = Math.Abs(targetX - finalPos.Value.X);
                if (finalAdx <= WalkAlignTolerancePx * 1.5f)
                    return true;

                var pos = finalPos.Value;
                Logger.Warning(
                    $"[移動診斷] 微步未達標 remaining={finalAdx:F2}px playerX={pos.X:F1} targetX={targetX:F1} " +
                    $"attribution=程式-X未對齊 platform={platformContext?.PlatformId ?? "-"}");
            }

            return false;
        }

        public void StopMovement()
        {
            lock (_lockObject)
            {
                if (_currentPressedKey != 0)
                {
                    SendKeyInput(_currentPressedKey, true);
                    _currentPressedKey = 0;
                }
            }
        }


        public bool HoldKey(ushort key)
        {
            if (IsNavigationInputBlocked()) return false;

            lock (_lockObject)
            {
                if (_currentPressedKey != 0 && _currentPressedKey != key)
                {
                    SendKeyInput(_currentPressedKey, true);
                    _currentPressedKey = 0;
                    System.Threading.Thread.Sleep(5);
                }

                if (_currentPressedKey == 0)
                {
                    var result = SendKeyInput(key, false);
                    if (result == 1)
                    {
                        _currentPressedKey = key;
                        return true;
                    }
                    return false;
                }
                return true; 
            }
        }

        private async Task TapKeyAsync(ushort key, int pressDurationMs, int intervalMs, CancellationToken ct)
        {
            lock (_lockObject)
            {
                if (_currentPressedKey != 0)
                {
                    SendKeyInput(_currentPressedKey, true);
                    _currentPressedKey = 0;
                }
            }
            SendKeyInput(key, false);
            await Task.Delay(pressDurationMs, ct);
            SendKeyInput(key, true);
            await Task.Delay(intervalMs, ct);
        }

        public void FocusGameWindow()
        {
            if (string.IsNullOrEmpty(_gameWindowTitle)) return;
            try
            {
                var hwnd = FindWindow(null, _gameWindowTitle);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[移動] 聚焦失敗: {ex.Message}");
            }
        }

        void IKeyboardService.SendKey(ushort vkCode, bool keyUp) => SendKeyInput(vkCode, keyUp);

        async Task IKeyboardService.TapKeyAsync(ushort vkCode, int durationMs, CancellationToken ct)
            => await TapKeyAsync(vkCode, durationMs, 20, ct);

        public uint SendKeyInput(ushort vkCode, bool keyUp)
        {
            int inputSize = _cachedInputSize;
            var input = new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vkCode, dwFlags = keyUp ? KEYEVENTF_KEYUP : 0 } } };
            var inputs = new INPUT[] { input };

            var result = SendInput(1, inputs, inputSize);
            return result;
        }

        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_MENU = 0x12;

        public async Task PerformAttackAsync(int cooldownMs, CancellationToken ct = default)
        {
            if (_isDisposed) return;
            StopMovement();
            await Task.Delay(50, ct);
            await TapKeyAsync(VK_CONTROL, 80, 50, ct);
            await Task.Delay(cooldownMs, ct);
        }

        /// <summary>由大腦調用的爬繩主進入點。拆解為對位、抓取、移動監控三個子系統。</summary>
        public async Task<bool> ClimbRopeAsync(PointF playerPos, float ropeX, float targetY, Func<PointF> getPlayerPosition, Func<bool>? isReachedExternally = null, CancellationToken ct = default, bool? forceDirectionIsUp = null)
        {
            FocusGameWindow();
            await Task.Delay(100, ct); 

            // 1. 水平對位階段
            if (!await TryAlignWithRopeAsync(ropeX, getPlayerPosition, ct))
            {
                return false;
            }

            var pStable = getPlayerPosition();
            bool climbUp = forceDirectionIsUp ?? (targetY < pStable.Y);
            float dx = ropeX - pStable.X;

            // 2. 發動抓取階段
            if (!await TryGrabRopeAsync(dx, climbUp, ct))
            {
                return false;
            }

            // 3. 垂直爬升監控階段
            ushort climbKey = climbUp ? VK_UP : VK_DOWN;
            return await MonitorRopeMovementAsync(
                climbKey, ropeX, targetY, getPlayerPosition, isReachedExternally, ct);
        }

        private async Task<bool> TryAlignWithRopeAsync(float ropeX, Func<PointF> getPlayerPosition, CancellationToken ct)
        {
            var pStable = getPlayerPosition();
            float dx = ropeX - pStable.X;
            float adx = Math.Abs(dx);
            float ropeSnapTol = GetRopeAlignTolerancePx();

            // 如果偏離超過門檻但還在側跳範圍內 (8.5px)，嘗試微調
            for (int i = 0; i < 6 && adx > ropeSnapTol && adx <= 8.5f && !ct.IsCancellationRequested; i++)
            {
                ushort k = pStable.X > ropeX ? VK_LEFT : VK_RIGHT;
                await TapKeyAsync(k, 20, 70, ct);
                await WaitForNextFrameAsync(ct);
                pStable = getPlayerPosition();
                dx = ropeX - pStable.X;
                adx = Math.Abs(dx);
            }

            return true; // 即使沒完全對齊，只要在側跳範圍內都嘗試發動
        }

        private async Task<bool> TryGrabRopeAsync(float dx, bool climbUp, CancellationToken ct)
        {
            if (!climbUp)
            {
                // 下跳抓繩
                SendKeyInput(VK_DOWN, false);
                await Task.Delay(150, ct);
                return true;
            }
            else if (Math.Abs(dx) <= 8.5f)
            {
                // 側跳咬繩序列 (Alt -> Up -> Release Alt)
                SendKeyInput(VK_MENU, false);  // Alt (Jump)
                await Task.Delay(100, ct);     
                SendKeyInput(VK_UP, false);    // Up
                await Task.Delay(60, ct);      
                SendKeyInput(VK_MENU, true);   // Release Alt
                await Task.Delay(200, ct);     // 等待動畫銜接
                return true;
            }
            
            Logger.Warning($"[爬繩] 對位偏移過大 ({dx:F1}px)，無法發動側跳。");
            return false;
        }

        private async Task<bool> MonitorRopeMovementAsync(
            ushort climbKey,
            float ropeX,
            float landingTargetY,
            Func<PointF> getPlayerPosition,
            Func<bool>? isReachedExternally,
            CancellationToken ct)
        {
            bool success = false;
            var climbPhase = System.Diagnostics.Stopwatch.StartNew();
            float landingYTol = (float)AppConfig.Instance.Navigation.RopeLandingYTolerancePx;

            try
            {
                SendKeyInput(climbKey, false);
                while (!ct.IsCancellationRequested)
                {
                    if (climbPhase.ElapsedMilliseconds > 12000)
                    {
                        Logger.Warning("[爬繩] 爬升逾時 (12s)，強制中止。");
                        break;
                    }

                    await WaitForNextFrameAsync(ct);
                    var currentPos = getPlayerPosition();
                    float currentDx = Math.Abs(ropeX - currentPos.X);

                    // 1. 外部熔斷：須同時通過 Tracker 驗收且 Y 已進入平台落點帶
                    if (isReachedExternally?.Invoke() == true &&
                        Math.Abs(currentPos.Y - landingTargetY) <= landingYTol)
                    {
                        Logger.Info("[爬繩] 外部熔斷觸發：已到達目標。");
                        success = true;
                        break;
                    }

                    // 2. 脫離判定（如果 X 軸偏移過大，說明掉下去了）
                    if (currentDx > 12.0f)
                    {
                        Logger.Warning($"[爬繩] 偵測到脫離繩索 (X偏移:{currentDx:F1})。");
                        break;
                    }

                    SendKeyInput(climbKey, false); // 持續確保按鍵按下
                }
            }
            finally
            {
                StopMovement();
                SendKeyInput(VK_UP, true);
                SendKeyInput(VK_DOWN, true);
            }

            await Task.Delay(200, ct); // 落地後的小緩衝
            return success;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            StopMovement();
            _isDisposed = true;
        }
    }
}
