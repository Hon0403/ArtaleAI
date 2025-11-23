using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
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
                Debug.WriteLine($"[移動控制] 警告：INPUT 結構大小不匹配，實際={actualSize} bytes, 預期={expectedSize} bytes");
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
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;      // 4 bytes
            // 隱式 padding: 4 bytes (for 8-byte alignment in 64-bit)
            public INPUTUNION U;   // 24 bytes (KEYBDINPUT)
        }

        [StructLayout(LayoutKind.Explicit)]
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
            // 隱式 padding: 4 bytes (for 8-byte alignment in 64-bit)
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

            // 計算移動方向（優先處理較大的偏移）
            bool moveHorizontal = Math.Abs(dx) > Math.Abs(dy);
            
            ushort targetKey = 0;
            if (moveHorizontal)
            {
                // 水平移動
                if (dx > 0)
                    targetKey = VK_RIGHT; // 向右
                else if (dx < 0)
                    targetKey = VK_LEFT;  // 向左
            }
            else
            {
                // 垂直移動
                if (dy > 0)
                    targetKey = VK_DOWN;  // 向下
                else if (dy < 0)
                    targetKey = VK_UP;    // 向上
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

                // 長按模式：持續按住按鍵
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
                            Debug.WriteLine($"[移動控制] 長按方向鍵: dx={dx}, dy={dy}, 距離={distance:F1}px, 按鍵={GetKeyName(targetKey)}");
                        }
                        else
                        {
                            Debug.WriteLine($"[移動控制] 無法按下按鍵: {GetKeyName(targetKey)}, 請檢查遊戲視窗是否已聚焦或是否有權限問題");
                        }
                    }
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
                    Debug.WriteLine("[移動控制] 已釋放按鍵，停止移動");
                }

                _movementCancellationToken?.Cancel();
                _movementCancellationToken?.Dispose();
                _movementCancellationToken = null;
            }
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
                        Debug.WriteLine($"[移動控制] 警告：SetForegroundWindow 失敗，可能被其他視窗阻止");
                    }
                }
                else
                {
                    Debug.WriteLine($"[移動控制] 警告：找不到遊戲視窗: {_gameWindowTitle}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[移動控制] 聚焦遊戲視窗失敗: {ex.Message}");
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
                Debug.WriteLine($"[移動控制] SendInput 失敗：返回={result}, 錯誤碼={errorCode} (0x{errorCode:X8}), 結構大小={inputSize} bytes, 按鍵={(keyUp ? "釋放" : "按下")} {GetKeyName(vkCode)}");
                
                // 常見錯誤碼說明
                switch (errorCode)
                {
                    case 87: // ERROR_INVALID_PARAMETER
                        Debug.WriteLine("[移動控制] 錯誤原因：參數無效，可能是結構體大小不正確或結構體對齊問題");
                        break;
                    case 5: // ERROR_ACCESS_DENIED
                        Debug.WriteLine("[移動控制] 錯誤原因：存取被拒絕，可能需要管理員權限或遊戲視窗未聚焦");
                        break;
                    case 0:
                        Debug.WriteLine("[移動控制] 錯誤原因：可能是輸入被其他線程阻止（如輸入法、鍵盤鉤子）");
                        break;
                    default:
                        Debug.WriteLine($"[移動控制] 未知錯誤碼：{errorCode}");
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

