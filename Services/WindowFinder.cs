using ArtaleAI.Config;
using ArtaleAI.Utils;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 視窗搜尋工具 - 負責尋找遊戲視窗並建立 GraphicsCaptureItem
    /// 使用 Windows.Graphics.Capture API 進行視窗擷取
    /// </summary>
    public static class WindowFinder
    {
        /// <summary>
        /// GraphicsCaptureItem COM 介面（Windows Runtime）
        /// </summary>
        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            [PreserveSig]
            int CreateForWindow([In] IntPtr window, [In] ref Guid iid, [Out] out IntPtr result);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // SetWindowPos 標誌常數
        private const uint SWP_NOMOVE = 0x0002;      // 不改變位置
        private const uint SWP_NOZORDER = 0x0004;   // 不改變層級
        private const uint SWP_SHOWWINDOW = 0x0040;  // 顯示視窗

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IGraphicsCaptureItemInterop factory);

        [DllImport("combase.dll")]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        /// <summary>
        /// 嘗試為指定視窗標題建立 GraphicsCaptureItem
        /// 使用 Windows Runtime API 建立畫面擷取物件
        /// </summary>
        /// <param name="windowTitle">視窗標題（完整名稱）</param>
        /// <param name="progressReporter">進度回報回調函數（可選）</param>
        /// <returns>成功時返回 GraphicsCaptureItem，失敗時返回 null</returns>
        public static GraphicsCaptureItem? TryCreateItemForWindow(string windowTitle, Action<string>? progressReporter = null)
        {
            var hwnd = FindWindow(null, windowTitle);
            if (hwnd == IntPtr.Zero) return null;

            try
            {
                var factory_iid = typeof(IGraphicsCaptureItemInterop).GUID;
                var className = "Windows.Graphics.Capture.GraphicsCaptureItem";
                var hr = WindowsCreateString(className, className.Length, out var hstring);
                if (hr != 0) return null;

                var result = RoGetActivationFactory(hstring, ref factory_iid, out var factory);
                WindowsDeleteString(hstring);
                if (result != 0) return null;

                var item_iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                var createResult = factory.CreateForWindow(hwnd, ref item_iid, out var item_ptr);
                if (createResult != 0 || item_ptr == IntPtr.Zero) return null;

                // 確保使用正確的 WinRT 轉換方式
                var item = MarshalInspectable<GraphicsCaptureItem>.FromAbi(item_ptr);
                Marshal.Release(item_ptr);
                return item;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 使用多種方式嘗試建立 GraphicsCaptureItem（自動回退機制）
        /// 依序嘗試：1.預設視窗標題 2.上次記錄的視窗名稱 3.透過程序名稱搜尋
        /// </summary>
        /// <param name="config">應用程式設定（包含視窗標題等資訊）</param>
        /// <param name="progressReporter">進度回報回調函數（可選）</param>
        /// <returns>成功時返回 GraphicsCaptureItem，所有方式都失敗時返回 null</returns>
        public static GraphicsCaptureItem? TryCreateItemWithFallback(AppConfig config, Action<string>? progressReporter = null)
        {
            progressReporter?.Invoke("=== 開始自動尋找視窗 ===");
            progressReporter?.Invoke($"預設視窗標題: '{AppConfig.Instance.GameWindowTitle}'");
            progressReporter?.Invoke($"上次記錄視窗: '{AppConfig.Instance.LastSelectedWindowName}'");
            progressReporter?.Invoke($"上次記錄程序: '{AppConfig.Instance.LastSelectedProcessName}'");

            // 1. 優先嘗試原本的視窗標題
            var item = TryCreateItemForWindow(AppConfig.Instance.GameWindowTitle, progressReporter);
            if (item != null)
            {
                progressReporter?.Invoke($"使用預設視窗標題找到: {AppConfig.Instance.GameWindowTitle}");
                return item;
            }
            progressReporter?.Invoke($"預設視窗標題找不到: {AppConfig.Instance.GameWindowTitle}");

            // 2. 嘗試上次成功的視窗名稱
            if (!string.IsNullOrEmpty(AppConfig.Instance.LastSelectedWindowName))
            {
                item = TryCreateItemForWindow(AppConfig.Instance.LastSelectedWindowName, progressReporter);
                if (item != null)
                {
                    progressReporter?.Invoke($"使用上次記錄的視窗: {AppConfig.Instance.LastSelectedWindowName}");
                    return item;
                }
                progressReporter?.Invoke($"上次記錄視窗找不到: {AppConfig.Instance.LastSelectedWindowName}");
            }
            else
            {
                progressReporter?.Invoke("上次記錄視窗名稱為空");
            }

            // 3. 嘗試透過程序名稱查找
            if (!string.IsNullOrEmpty(AppConfig.Instance.LastSelectedProcessName))
            {
                try
                {
                    var processes = Process.GetProcessesByName(AppConfig.Instance.LastSelectedProcessName);
                    foreach (var process in processes)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(process.MainWindowTitle))
                        {
                            item = TryCreateItemForWindow(process.MainWindowTitle, progressReporter);
                            if (item != null)
                            {
                                progressReporter?.Invoke($"透過程序名稱找到視窗: {AppConfig.Instance.LastSelectedProcessName}");
                                return item;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    progressReporter?.Invoke($"透過程序名稱查找失敗: {ex.Message}");
                }
            }

            progressReporter?.Invoke("所有自動方式都失敗，需要手動選擇");
            return null;
        }

        /// <summary>
        /// 強制重置遊戲視窗大小到標準尺寸
        /// 解決視窗大小變化導致的座標偏移和圖像辨識失敗問題
        /// </summary>
        /// <param name="windowTitle">視窗標題</param>
        /// <param name="targetClientWidth">目標內容區域寬度（預設 1600）</param>
        /// <param name="targetClientHeight">目標內容區域高度（預設 900）</param>
        /// <param name="progressReporter">進度回報回調函數（可選）</param>
        /// <returns>成功時返回 true，失敗時返回 false</returns>
        public static bool ForceGameWindowSize(string windowTitle, int targetClientWidth = 1600, int targetClientHeight = 900, Action<string>? progressReporter = null)
        {
            var hwnd = FindWindow(null, windowTitle);
            if (hwnd == IntPtr.Zero)
            {
                progressReporter?.Invoke($"找不到視窗: {windowTitle}");
                return false;
            }

            return ForceGameWindowSize(hwnd, targetClientWidth, targetClientHeight, progressReporter);
        }

        /// <summary>
        /// 強制重置遊戲視窗大小到標準尺寸（使用視窗句柄）
        /// </summary>
        /// <param name="gameWindowHandle">遊戲視窗句柄</param>
        /// <param name="targetClientWidth">目標內容區域寬度（預設 1600）</param>
        /// <param name="targetClientHeight">目標內容區域高度（預設 900）</param>
        /// <param name="progressReporter">進度回報回調函數（可選）</param>
        /// <returns>成功時返回 true，失敗時返回 false</returns>
        public static bool ForceGameWindowSize(IntPtr gameWindowHandle, int targetClientWidth = 1600, int targetClientHeight = 900, Action<string>? progressReporter = null)
        {
            if (gameWindowHandle == IntPtr.Zero)
            {
                progressReporter?.Invoke("視窗句柄無效");
                return false;
            }

            try
            {
                // 1. 取得目前的視窗大小（含邊框）和內容區域大小（不含邊框）
                RECT windowRect, clientRect;
                if (!GetWindowRect(gameWindowHandle, out windowRect))
                {
                    progressReporter?.Invoke("無法取得視窗大小");
                    return false;
                }

                if (!GetClientRect(gameWindowHandle, out clientRect))
                {
                    progressReporter?.Invoke("無法取得視窗內容區域大小");
                    return false;
                }

                // 2. 計算邊框的厚度
                int currentClientWidth = clientRect.Right - clientRect.Left;
                int currentClientHeight = clientRect.Bottom - clientRect.Top;
                int currentWindowWidth = windowRect.Right - windowRect.Left;
                int currentWindowHeight = windowRect.Bottom - windowRect.Top;
                int borderThicknessX = currentWindowWidth - currentClientWidth;
                int borderThicknessY = currentWindowHeight - currentClientHeight;

                // 3. 檢查是否需要調整（如果已經是目標大小，跳過）
                if (currentClientWidth == targetClientWidth && currentClientHeight == targetClientHeight)
                {
                    progressReporter?.Invoke($"視窗大小已是標準尺寸: {targetClientWidth}x{targetClientHeight}");
                    return true;
                }

                // 4. 計算「目標視窗總大小」= 目標內容大小 + 邊框厚度
                int finalWidth = targetClientWidth + borderThicknessX;
                int finalHeight = targetClientHeight + borderThicknessY;

                // 5. 強制設定視窗大小（不改變位置和層級）
                bool success = SetWindowPos(
                    gameWindowHandle,
                    IntPtr.Zero,
                    0, 0,  // X, Y（使用 SWP_NOMOVE 時會被忽略）
                    finalWidth,
                    finalHeight,
                    SWP_NOMOVE | SWP_NOZORDER | SWP_SHOWWINDOW
                );

                if (success)
                {
                    progressReporter?.Invoke($"✅ 視窗大小已重置: {finalWidth}x{finalHeight} (內容區域: {targetClientWidth}x{targetClientHeight})");
                    Logger.Info($"[視窗管理] 強制重置視窗大小為: {finalWidth}x{finalHeight} (內容區域: {targetClientWidth}x{targetClientHeight})");
                    return true;
                }
                else
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    progressReporter?.Invoke($"❌ 設定視窗大小失敗，錯誤碼: {errorCode}");
                    Logger.Error($"[視窗管理] SetWindowPos 失敗，錯誤碼: {errorCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                progressReporter?.Invoke($"重置視窗大小時發生錯誤: {ex.Message}");
                Logger.Error($"[視窗管理] 重置視窗大小錯誤: {ex.Message}");
                return false;
            }
        }

    }
}
