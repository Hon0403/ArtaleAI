using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using ArtaleAI.Config;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Utils;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 角色移動控制器 - 負責發送鍵盤指令控制遊戲角色移動
    /// </summary>
    public class CharacterMovementController : IDisposable
    {
        /// <summary>
        /// 設定遊戲視窗標題（用於聚焦視窗）
        /// </summary>
        public void SetGameWindowTitle(string windowTitle)
        {
            _gameWindowTitle = windowTitle ?? "";
        }

        private bool _isDisposed = false;
        private CancellationTokenSource? _movementCancellationToken;
        private readonly object _lockObject = new object();
        private ushort _currentPressedKey = 0; // 當前按住的按鍵
        private string _gameWindowTitle = ""; // 遊戲視窗標題
        
        // ============================================================
        // 🔧 邊界保護相關欄位
        // ============================================================
        private PlatformBounds? _platformBounds;
        private DateTime _lastBoundaryHitTime = DateTime.MinValue;
        private int _boundaryCooldownMs = 500;
        private float _bufferZone = 5.0f;
        private float _emergencyZone = 2.0f;
        
        /// <summary>
        /// 邊界觸發事件 - 當角色接近或觸及邊界時觸發
        /// 參數為邊界方向：left, right, top, bottom
        /// </summary>
        public event Action<string>? OnBoundaryHit;
        
        /// <summary>
        /// 目標超出邊界事件 - 當目標點超出安全範圍時觸發
        /// </summary>
        public event Action<SdPoint>? OnTargetOutOfBounds;
        
        /// <summary>
        /// 設定平台邊界（用於防止角色掉落）
        /// </summary>
        /// <param name="bounds">平台邊界資料</param>
        /// <param name="config">邊界處理設定（可選）</param>
        public void SetPlatformBounds(PlatformBounds bounds, PlatformBoundsConfig? config = null)
        {
            _platformBounds = bounds;
            if (config != null)
            {
                _bufferZone = (float)config.BufferZone;
                _emergencyZone = (float)config.EmergencyZone;
                _boundaryCooldownMs = config.CooldownMs;
            }
            Logger.Info($"[移動控制] 設定平台邊界：{bounds}, 緩衝區={_bufferZone}px, 冷卻={_boundaryCooldownMs}ms");
        }
        
        // 修復：快取 INPUT 結構體大小（避免每次調用都重新計算）
        private static readonly int _cachedInputSize = CalculateInputSize();
        
        private static int CalculateInputSize()
        {
            bool is64Bit = IntPtr.Size == 8;
            int expectedSize = is64Bit ? 40 : 28; // Windows API 規範的大小
            int actualSize = Marshal.SizeOf(typeof(INPUT));
            
            #if DEBUG
            if (actualSize != expectedSize)
            {
                Logger.Warning($"[移動控制] INPUT 結構大小不匹配，實際={actualSize} bytes, 預期={expectedSize} bytes");
            }
            #endif
            
            return actualSize == expectedSize ? actualSize : expectedSize;
        }

        #region Windows API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("kernel32.dll")]
        private static extern uint GetLastError();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9; // 還原視窗
        private const int SW_SHOW = 5;    // 顯示視窗

        // 修復：使用正確的結構體定義（符合 Windows API 規範）
        // 參考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-input
        // INPUT 結構在 64-bit 系統應為 40 bytes：4 bytes type + 4 bytes padding + 32 bytes INPUTUNION
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;      // 4 bytes
            public INPUTUNION U;   // 32 bytes (包含 MOUSEINPUT 的大小)
        }

        // INPUTUNION 需要是 MOUSEINPUT, KEYBDINPUT, HARDWAREINPUT 中最大的（MOUSEINPUT = 32 bytes）
        // 使用顯式佈局確保大小正確
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        // 修復：使用正確的結構體定義（符合 Windows API 規範）
        // 參考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;          // 2 bytes
            public ushort wScan;        // 2 bytes
            public uint dwFlags;        // 4 bytes
            public uint time;           // 4 bytes
            public IntPtr dwExtraInfo;  // 8 bytes (64-bit) or 4 bytes (32-bit)
            // KEYBDINPUT total: 20 bytes (64-bit) or 16 bytes (32-bit)
            // 但 INPUTUNION 需要 32 bytes（MOUSEINPUT 的大小）所以使用 Size = 32
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        // 方向鍵虛擬鍵碼（VK）
        private const ushort VK_UP = 0x26;      // ↑ 上
        private const ushort VK_DOWN = 0x28;    // ↓ 下
        private const ushort VK_LEFT = 0x25;    // ← 左
        private const ushort VK_RIGHT = 0x27;   // → 右

        #endregion

        /// <summary>
        /// 移動角色到目標座標
        /// 使用長按模式持續按住方向鍵直到接近目標
        /// </summary>
        /// <param name="currentPos">當前角色位置（小地圖座標）</param>
        /// <param name="targetPos">目標位置（小地圖座標）</param>
        /// <param name="reachDistance">到達距離閾值（像素）</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task MoveToTargetAsync(SdPoint currentPos, SdPoint targetPos, double reachDistance = 5.0, CancellationToken cancellationToken = default)
        {
            if (_isDisposed) return;

            // ============================================================
            // 🔧 三重防護邊界檢查（在任何移動邏輯之前）
            // ============================================================
            if (_platformBounds != null)
            {
                // === 防護 1：角色已超出邊界（緊急停止）===
                if (currentPos.X < _platformBounds.MinX - _emergencyZone ||
                    currentPos.X > _platformBounds.MaxX + _emergencyZone)
                {
                    Logger.Error($"[邊界] 角色超出邊界！X={currentPos.X:F1}, 範圍=[{_platformBounds.MinX:F1}, {_platformBounds.MaxX:F1}]");
                    StopMovement();
                    TriggerBoundaryEvent(currentPos.X < _platformBounds.MinX ? "left" : "right");
                    return;
                }
                
                // === 防護 2：接近邊界時提前警告（緩衝區預警）===
                if (currentPos.X > _platformBounds.MaxX - _bufferZone)
                {
                    Logger.Warning($"[邊界] 接近右邊界（剩餘 {_platformBounds.MaxX - currentPos.X:F1}px），觸發減速");
                    TriggerBoundaryEvent("right");
                    // 不 return，繼續執行移動邏輯讓其自然減速
                }
                else if (currentPos.X < _platformBounds.MinX + _bufferZone)
                {
                    Logger.Warning($"[邊界] 接近左邊界（剩餘 {currentPos.X - _platformBounds.MinX:F1}px），觸發減速");
                    TriggerBoundaryEvent("left");
                }
                
                // === 防護 3：目標點超出邊界 ===
                if (targetPos.X < _platformBounds.MinX || targetPos.X > _platformBounds.MaxX)
                {
                    Logger.Warning($"[邊界] 目標點超出邊界！目標X={targetPos.X}, 範圍=[{_platformBounds.MinX:F1}, {_platformBounds.MaxX:F1}]");
                    OnTargetOutOfBounds?.Invoke(targetPos);
                    StopMovement();
                    return;
                }
            }

            var distance = CalculateDistance(currentPos, targetPos);
            if (distance <= reachDistance)
            {
                // 已到達目標，停止移動
                StopMovement();
                return;
            }

            // 計算方向向量
            var dx = targetPos.X - currentPos.X;
            var dy = targetPos.Y - currentPos.Y;

            // ✅ Y 軸鎖死邏輯：當角色掉下去時（dy > 0），立即停止移動
            const float YAxisMisalignThreshold = 10.0f; // Y 軸誤差閾值
            bool isYAxisMisaligned = Math.Abs(dy) > YAxisMisalignThreshold;
            
            if (isYAxisMisaligned && Math.Abs(dy) > Math.Abs(dx))
            {
                if (dy > 0)  // 掉下去
                {
                    Logger.Warning($"[移動] Y 軸鎖死：角色掉落中 (dy={dy:F1}px)，停止移動");
                    StopMovement();
                    return;  // 直接返回，不執行任何移動
                }
                // dy < 0 表示跳躍中，禁止水平移動但繼續等待
                Logger.Debug($"[移動] Y 軸跳躍中 (dy={dy:F1}px)，暫停水平移動");
                return;
            }

            // 計算移動方向
            // 🔧 平台遊戲專屬邏輯：強制優先水平移動
            // 舊邏輯 (Math.Abs(dx) > Math.Abs(dy)) 會導致當 dx 很小但 dy 稍大時（例如偵測雜訊），
            // 角色誤觸發 下(趴下) 或 上 指令。在橫向捲軸遊戲中，Y 軸通常由重力控制。
            
            ushort targetKey = 0;

            // 只要水平距離大於判定閾值（使用更嚴格的 2.0px 確保貼合），就優先修正水平位置
            if (Math.Abs(dx) > Math.Max(2.0, reachDistance / 2))
            {
                if (dx > 0)
                    targetKey = VK_RIGHT; // 向右
                else
                    targetKey = VK_LEFT;  // 向左
            }
            else
            {
                // 水平已對齊。檢查垂直？
                // 在一般平台移動中，禁止單獨使用 上/下 來移動（除非是梯子或傳送點邏輯，此處暫不處理）
                // 特別是 dy > 0 時（目標在下方），按 "下" 只會趴下，並不能移動，因此忽略。
                
                // 如果未來需要梯子邏輯，可在此處加入
                // if (dy < -LADDER_THRESHOLD) targetKey = VK_UP;
                
                Logger.Debug($"[移動] 水平已對齊 (dx={dx:F1})，忽略垂直差異 (dy={dy:F1})");
            }

            if (targetKey != 0)
            {
                // 修復：先聚焦遊戲視窗（在 lock 外執行，避免阻塞）
                if (!string.IsNullOrEmpty(_gameWindowTitle))
                {
                    FocusGameWindow();
                    // 等待一小段時間確保視窗已聚焦（Windows 需要時間處理）
                    await Task.Delay(10).ConfigureAwait(false);
                }

                // 漸進式減速邏輯
                const double DecelerationDistance = 20.0;
                
                // 長按模式：持續按住按鍵（距離較遠時）
                if (distance > DecelerationDistance)
                {
                    lock (_lockObject)
                    {
                        // 如果方向改變，先釋放舊的按鍵
                        if (_currentPressedKey != 0 && _currentPressedKey != targetKey)
                        {
                            SendKeyInput(_currentPressedKey, true); // 釋放舊按鍵
                            _currentPressedKey = 0;
                            // 注意：在 lock 內不能使用 await，使用同步延遲
                            // 10ms 的延遲很短，不會造成明顯的效能問題
                            System.Threading.Thread.Sleep(10);
                        }

                        // 如果沒有按住按鍵，則按下新按鍵
                        if (_currentPressedKey == 0)
                        {
                            var result = SendKeyInput(targetKey, false); // 按下新按鍵
                            if (result == 1)
                            {
                                _currentPressedKey = targetKey;
                                Logger.Debug($"[移動] 長按方向鍵: dx={dx}, dy={dy}, 距離={distance:F1}px, 按鍵={GetKeyName(targetKey)}");
                            }
                            else
                            {
                                Logger.Warning($"[移動] 無法按下按鍵: {GetKeyName(targetKey)}, 請檢查遊戲視窗是否已聚焦或權限問題");
                            }
                        }
                    }
                }
                else
                {
                    // 點按模式：接近目標時，改為短暫按壓（防過衝）
                    // 先釋放長按的按鍵
                    lock (_lockObject)
                    {
                        if (_currentPressedKey != 0)
                        {
                            SendKeyInput(_currentPressedKey, true);
                            _currentPressedKey = 0;
                            Logger.Debug($"[移動] 進入減速區（剩餘 {distance:F1}px），切換為點按模式");
                        }
                    }
                    
                    // 執行一次點按（Tap）
                    Logger.Debug($"[移動] 點按微調: {GetKeyName(targetKey)}, 距離={distance:F1}px");
                    await SendKeyPressAsync(targetKey, 30, cancellationToken); // 30ms 短按
                    await Task.Delay(50, cancellationToken); // 50ms 等待
                }
            }
        }

        /// <summary>
        /// 取得按鍵名稱（用於調試）
        /// </summary>
        private string GetKeyName(ushort vkCode)
        {
            return vkCode switch
            {
                VK_UP => "↑",
                VK_DOWN => "↓",
                VK_LEFT => "←",
                VK_RIGHT => "→",
                _ => "未知"
            };
        }

        /// <summary>
        /// 停止所有移動
        /// </summary>
        public void StopMovement()
        {
            lock (_lockObject)
            {
                // 釋放當前按住的按鍵
                if (_currentPressedKey != 0)
                {
                    SendKeyInput(_currentPressedKey, true);
                    _currentPressedKey = 0;
                    Logger.Debug("[移動] 已釋放按鍵，停止移動");
                }

                _movementCancellationToken?.Cancel();
                _movementCancellationToken?.Dispose();
                _movementCancellationToken = null;
            }
        }

        /// <summary>
        /// 觸發邊界事件（帶冷卻時間防抖）
        /// </summary>
        /// <param name="direction">邊界方向：left, right, top, bottom</param>
        private void TriggerBoundaryEvent(string direction)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastBoundaryHitTime).TotalMilliseconds < _boundaryCooldownMs)
            {
                // 冷卻中，跳過此次事件
                return;
            }
            
            _lastBoundaryHitTime = now;
            Logger.Debug($"[邊界事件] 觸發邊界：{direction}");
            OnBoundaryHit?.Invoke(direction);
        }

        /// <summary>
        /// 發送按鍵按下和釋放
        /// </summary>
        private async Task SendKeyPressAsync(ushort vkCode, int durationMs, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return;

            try
            {
                // 按下按鍵
                SendKeyInput(vkCode, false);

                // 保持按下的時間
                await Task.Delay(durationMs, cancellationToken);

                // 釋放按鍵
                SendKeyInput(vkCode, true);
            }
            catch (OperationCanceledException)
            {
                // 取消時釋放按鍵
                SendKeyInput(vkCode, true);
            }
        }

        /// <summary>
        /// 聚焦遊戲視窗（確保按鍵發送到正確的視窗）
        /// </summary>
        private void FocusGameWindow()
        {
            if (string.IsNullOrEmpty(_gameWindowTitle)) return;

            try
            {
                var hwnd = FindWindow(null, _gameWindowTitle);
                if (hwnd != IntPtr.Zero)
                {
                    // 還原視窗（如果最小化）
                    ShowWindow(hwnd, SW_RESTORE);
                    // 聚焦視窗
                    bool success = SetForegroundWindow(hwnd);
                    if (!success)
                    {
                        Logger.Warning("[移動] SetForegroundWindow 失敗，可能被其他視窗阻止");
                    }
                }
                else
                {
                    Logger.Warning($"[移動] 找不到遊戲視窗: {_gameWindowTitle}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[移動] 聚焦遊戲視窗失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 發送鍵盤輸入（使用虛擬鍵碼 VK）
        /// </summary>
        /// <returns>發送的輸入數量（應為 1）</returns>
        private uint SendKeyInput(ushort vkCode, bool keyUp)
        {
            // 使用快取的結構體大小（避免每次調用都重新計算）
            int inputSize = _cachedInputSize;
            
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vkCode,
                        wScan = 0,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            var inputs = new INPUT[] { input };
            
            // 清除之前的錯誤碼（調用 GetLastError 會清除錯誤碼）
            GetLastError();
            
            var result = SendInput(1, inputs, inputSize);
            
            if (result != 1)
            {
                var errorCode = GetLastError();
                Logger.Error($"[移動] SendInput 失敗：返回={result}, 錯誤碼={errorCode} (0x{errorCode:X8}), 結構大小={inputSize} bytes, 按鍵={(keyUp ? "釋放" : "按下")} {GetKeyName(vkCode)}");
                
                // 常見錯誤碼說明
                switch (errorCode)
                {
                    case 87: // ERROR_INVALID_PARAMETER
                        Logger.Error("[移動] 錯誤原因：參數無效，可能是結構體大小不正確或對齊問題");
                        break;
                    case 5: // ERROR_ACCESS_DENIED
                        Logger.Error("[移動] 錯誤原因：存取被拒絕，可能需要管理員權限或遊戲視窗未聚焦");
                        break;
                    case 0:
                        Logger.Error("[移動] 錯誤原因：可能是輸入被其他線程阻止（如輸入法、鍵盤鉤子）");
                        break;
                    default:
                        Logger.Error($"[移動] 未知錯誤碼：{errorCode}");
                        break;
                }
            }
            else
            {
                // 成功時也記錄（僅在調試時）
                // Debug.WriteLine($"[移動控制] 成功發送按鍵：{(keyUp ? "釋放" : "按下")} {GetKeyName(vkCode)}");
            }
            
            return result;
        }

        /// <summary>
        /// 計算兩點之間的歐幾里得距離
        /// </summary>
        private double CalculateDistance(SdPoint p1, SdPoint p2)
        {
            var dx = p1.X - p2.X;
            var dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 釋放資源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            StopMovement();
            _isDisposed = true;
        }
    }
}

