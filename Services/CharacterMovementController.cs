using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using ArtaleAI.Models.Config;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Utils;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
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

        public void ReleaseAllKeys()
        {
            StopMovement();
        }

        /// <summary>由 <see cref="NavigationSettings.RopeHitboxWidth"/> 計算出的半徑，供繩索微調對齊 X。</summary>
        private static float GetRopeAlignTolerancePx()
        {
            try
            {
                return (float)(AppConfig.Instance.Navigation.RopeHitboxWidth / 2.0);
            }
            catch (InvalidOperationException)
            {
                return 3.0f;
            }
        }

        /// <summary>長按走向目標 X；以 <paramref name="isReachedExternally"/> 為到達。</summary>
        public async Task<MovementResult> MoveToTargetAsync(SdPointF targetPos, Func<SdPointF?> getPlayerPosition, float fallToleranceY, Func<bool> isReachedExternally, CancellationToken cancellationToken = default)
        {
            if (_isDisposed) return MovementResult.Failed;
            ArgumentNullException.ThrowIfNull(isReachedExternally);

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

            var visionLossWatcher = new System.Diagnostics.Stopwatch();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
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

                    if (Math.Abs(currentPos.Y - initialY) > fallToleranceY)
                    {
                        Logger.Warning($"[移動] 偵測到墜落 (Y偏移:{Math.Abs(currentPos.Y - initialY):F1})，中止導航。");
                        return MovementResult.Failed;
                    }

                    if (isReachedExternally())
                    {
                        StopMovement();
                        Logger.Info("[移動] 碰撞熔斷觸發：進入目標 Hitbox。");
                        break;
                    }

                    float currentDx = targetPos.X - currentPos.X;
                    if (Math.Sign(currentDx) != expectedSignX && Math.Sign(currentDx) != 0)
                    {
                        StopMovement();
                        Logger.Warning("[移動] 偵測到越界，停止並等待慣性消散...");
                        break;
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


        private async Task<bool> WaitForHitboxAsync(Func<bool> isReachedExternally, int timeoutMs, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs && !ct.IsCancellationRequested)
            {
                if (isReachedExternally())
                {
                    return true;
                }

                await WaitForNextFrameAsync(ct);
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

        private async Task SendKeyPressAsync(ushort vkCode, int durationMs, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                SendKeyInput(vkCode, false);
                await Task.Delay(durationMs, cancellationToken);
                SendKeyInput(vkCode, true);
            }
            catch (OperationCanceledException)
            {
                SendKeyInput(vkCode, true);
            }
        }

        public bool HoldKey(ushort key)
        {
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

        public async Task<bool> ClimbRopeAsync(PointF playerPos, float ropeX, float targetY, Func<PointF> getPlayerPosition, Func<bool>? isReachedExternally = null, CancellationToken ct = default, bool? forceDirectionIsUp = null)
        {
            FocusGameWindow();

            await Task.Delay(100, ct); // 原子化穩定等待 (固定值)

            var pStable = getPlayerPosition();
            float dx = ropeX - pStable.X;
            float adx = Math.Abs(dx);
            float ropeSnapTol = GetRopeAlignTolerancePx();
            for (int i = 0; i < 6 && adx > ropeSnapTol && adx <= 8.5f && !ct.IsCancellationRequested; i++)
            {
                ushort k = pStable.X > ropeX ? VK_LEFT : VK_RIGHT;
                await TapKeyAsync(k, 20, 70, ct);
                await WaitForNextFrameAsync(ct);
                pStable = getPlayerPosition();
                dx = ropeX - pStable.X;
                adx = Math.Abs(dx);
            }

            pStable = getPlayerPosition();
            dx = ropeX - pStable.X;
            bool climbUp = forceDirectionIsUp ?? (targetY < pStable.Y);
            ushort climbKey = climbUp ? VK_UP : VK_DOWN;

            if (!climbUp)
            {
                SendKeyInput(VK_DOWN, false);
                await Task.Delay(150, ct);  
            }
            else if (Math.Abs(dx) <= 8.5f)
            {
                SendKeyInput(VK_MENU, false);  
                await Task.Delay(100, ct);     
                SendKeyInput(VK_UP, false);    
                await Task.Delay(60, ct);      
                SendKeyInput(VK_MENU, true);   
                await Task.Delay(200, ct);     
            }
            else
            {
                Logger.Warning($"[爬繩] 對位偏移過大 ({dx:F1}px)，拒絕發動側跳，回傳 Failed。");
                return false;
            }

            bool stillOnRope = true;
            bool success = false;
            var climbPhase = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                SendKeyInput(climbKey, false);
                while (stillOnRope && !ct.IsCancellationRequested)
                {
                    if (climbPhase.ElapsedMilliseconds > 12000)
                    {
                        Logger.Warning("[爬繩] 爬升階段逾時（12s），停止以免 FSM 長時間卡在 Moving_Vertical。");
                        break;
                    }

                    await WaitForNextFrameAsync(ct);
                    var currentPos = getPlayerPosition();
                    float currentDx = Math.Abs(ropeX - currentPos.X);

                    if (isReachedExternally?.Invoke() == true)
                    {
                        Logger.Info("[爬繩] 外部熔斷觸發：大腦判定已到達目標。");
                        success = true;
                        break;
                    }

                    if (currentDx > 10.0f) break;
                    SendKeyInput(climbKey, false);
                }
            }
            finally
            {
                StopMovement();
                SendKeyInput(VK_UP, true);
                SendKeyInput(VK_DOWN, true);
            }

            await Task.Delay(200, ct);
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
