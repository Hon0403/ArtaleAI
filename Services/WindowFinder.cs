using ArtaleAI.Models.Config;
using ArtaleAI.Utils;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace ArtaleAI.Services
{
    /// <summary>以視窗標題／歷史程序建立 <see cref="GraphicsCaptureItem"/>，並可重設客戶區尺寸。</summary>
    public static class WindowFinder
    {
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

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IGraphicsCaptureItemInterop factory);

        [DllImport("combase.dll")]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

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

                var item = MarshalInspectable<GraphicsCaptureItem>.FromAbi(item_ptr);
                Marshal.Release(item_ptr);
                return item;
            }
            catch
            {
                return null;
            }
        }
        /// <summary>依序：設定標題 → 上次視窗標題 → 上次程序主視窗標題。</summary>
        public static GraphicsCaptureItem? TryCreateItemWithFallback(AppConfig config, Action<string>? progressReporter = null)
        {
            progressReporter?.Invoke("=== 開始自動尋找視窗 ===");
            progressReporter?.Invoke($"預設視窗標題: '{AppConfig.Instance.General.GameWindowTitle}'");
            progressReporter?.Invoke($"上次記錄視窗: '{AppConfig.Instance.General.LastSelectedWindowName}'");
            progressReporter?.Invoke($"上次記錄程序: '{AppConfig.Instance.General.LastSelectedProcessName}'");

            var item = TryCreateItemForWindow(AppConfig.Instance.General.GameWindowTitle, progressReporter);
            if (item != null)
            {
                progressReporter?.Invoke($"使用預設視窗標題找到: {AppConfig.Instance.General.GameWindowTitle}");
                return item;
            }
            progressReporter?.Invoke($"預設視窗標題找不到: {AppConfig.Instance.General.GameWindowTitle}");

            if (!string.IsNullOrEmpty(AppConfig.Instance.General.LastSelectedWindowName))
            {
                item = TryCreateItemForWindow(AppConfig.Instance.General.LastSelectedWindowName, progressReporter);
                if (item != null)
                {
                    progressReporter?.Invoke($"使用上次記錄的視窗: {AppConfig.Instance.General.LastSelectedWindowName}");
                    return item;
                }
                progressReporter?.Invoke($"上次記錄視窗找不到: {AppConfig.Instance.General.LastSelectedWindowName}");
            }
            else
            {
                progressReporter?.Invoke("上次記錄視窗名稱為空");
            }

            if (!string.IsNullOrEmpty(AppConfig.Instance.General.LastSelectedProcessName))
            {
                try
                {
                    var processes = Process.GetProcessesByName(AppConfig.Instance.General.LastSelectedProcessName);
                    foreach (var process in processes)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(process.MainWindowTitle))
                        {
                            item = TryCreateItemForWindow(process.MainWindowTitle, progressReporter);
                            if (item != null)
                            {
                                progressReporter?.Invoke($"透過程序名稱找到視窗: {AppConfig.Instance.General.LastSelectedProcessName}");
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
        /// <summary>將客戶區調為指定像素（保留邊框厚度），利於固定解析度辨識。</summary>
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

        public static bool ForceGameWindowSize(IntPtr gameWindowHandle, int targetClientWidth = 1600, int targetClientHeight = 900, Action<string>? progressReporter = null)
        {
            if (gameWindowHandle == IntPtr.Zero)
            {
                progressReporter?.Invoke("視窗句柄無效");
                return false;
            }

            try
            {
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

                int currentClientWidth = clientRect.Right - clientRect.Left;
                int currentClientHeight = clientRect.Bottom - clientRect.Top;
                int currentWindowWidth = windowRect.Right - windowRect.Left;
                int currentWindowHeight = windowRect.Bottom - windowRect.Top;
                int borderThicknessX = currentWindowWidth - currentClientWidth;
                int borderThicknessY = currentWindowHeight - currentClientHeight;

                if (currentClientWidth == targetClientWidth && currentClientHeight == targetClientHeight)
                {
                    progressReporter?.Invoke($"視窗大小已是標準尺寸: {targetClientWidth}x{targetClientHeight}");
                    return true;
                }

                int finalWidth = targetClientWidth + borderThicknessX;
                int finalHeight = targetClientHeight + borderThicknessY;

                bool success = SetWindowPos(
                    gameWindowHandle,
                    IntPtr.Zero,
                    0, 0,
                    finalWidth,
                    finalHeight,
                    SWP_NOMOVE | SWP_NOZORDER | SWP_SHOWWINDOW
                );

                if (success)
                {
                    progressReporter?.Invoke($"視窗大小已重置: {finalWidth}x{finalHeight} (內容區域: {targetClientWidth}x{targetClientHeight})");
                    Logger.Info($"[視窗管理] 強制重置視窗大小為: {finalWidth}x{finalHeight} (內容區域: {targetClientWidth}x{targetClientHeight})");
                    return true;
                }
                else
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    progressReporter?.Invoke($"設定視窗大小失敗，錯誤碼: {errorCode}");
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
