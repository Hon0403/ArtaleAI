using ArtaleAI.Models.Config;
using ArtaleAI.Shared;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Capture;
using WinRT;

namespace ArtaleAI.Infrastructure.Capture
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

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AdjustWindowRectEx(ref RECT lpRect, int dwStyle, bool bMenu, int dwExStyle);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int SW_RESTORE = 9;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
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

        /// <summary>與擷取相同的尋窗順序：設定標題 → 上次標題 → 程序主視窗。</summary>
        public static IntPtr FindGameWindowHandle(AppConfig? config = null, Action<string>? progressReporter = null)
        {
            config ??= AppConfig.Instance;
            var general = config.General;

            foreach (var title in new[] { general.GameWindowTitle, general.LastSelectedWindowName })
            {
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var hwnd = FindWindow(null, title);
                if (hwnd != IntPtr.Zero)
                {
                    progressReporter?.Invoke($"找到視窗: {title}");
                    return hwnd;
                }
            }

            if (!string.IsNullOrWhiteSpace(general.LastSelectedProcessName))
            {
                try
                {
                    foreach (var process in Process.GetProcessesByName(general.LastSelectedProcessName))
                    {
                        if (process.MainWindowHandle == IntPtr.Zero)
                            continue;

                        progressReporter?.Invoke(
                            $"透過程序找到視窗: {general.LastSelectedProcessName} ({process.MainWindowTitle})");
                        return process.MainWindowHandle;
                    }
                }
                catch (Exception ex)
                {
                    progressReporter?.Invoke($"程序尋窗失敗: {ex.Message}");
                }
            }

            progressReporter?.Invoke("找不到遊戲視窗句柄");
            return IntPtr.Zero;
        }

        /// <summary>讀取遊戲視窗客戶區寬高（不含標題列／邊框）。</summary>
        public static bool TryGetClientSize(string windowTitle, out int clientWidth, out int clientHeight)
        {
            clientWidth = 0;
            clientHeight = 0;
            var hwnd = FindWindow(null, windowTitle);
            if (hwnd == IntPtr.Zero)
                hwnd = FindGameWindowHandle();
            return hwnd != IntPtr.Zero && TryGetClientSize(hwnd, out clientWidth, out clientHeight);
        }

        public static bool TryGetClientSize(IntPtr gameWindowHandle, out int clientWidth, out int clientHeight)
        {
            clientWidth = 0;
            clientHeight = 0;
            if (gameWindowHandle == IntPtr.Zero)
                return false;

            if (!GetClientRect(gameWindowHandle, out RECT clientRect))
                return false;

            clientWidth = clientRect.Right - clientRect.Left;
            clientHeight = clientRect.Bottom - clientRect.Top;
            return clientWidth > 0 && clientHeight > 0;
        }

        public static bool IsClientSizeMatch(
            int clientWidth,
            int clientHeight,
            int targetClientWidth,
            int targetClientHeight,
            int tolerancePx = 2)
        {
            return Math.Abs(clientWidth - targetClientWidth) <= tolerancePx
                && Math.Abs(clientHeight - targetClientHeight) <= tolerancePx;
        }

        /// <summary>將客戶區調為指定像素（保留邊框厚度），利於固定解析度辨識。</summary>
        public static bool ForceGameWindowSize(
            string windowTitle,
            int targetClientWidth = 1280,
            int targetClientHeight = 720,
            Action<string>? progressReporter = null)
        {
            var hwnd = FindWindow(null, windowTitle);
            if (hwnd == IntPtr.Zero)
                hwnd = FindGameWindowHandle(progressReporter: progressReporter);

            if (hwnd == IntPtr.Zero)
            {
                progressReporter?.Invoke($"找不到視窗: {windowTitle}");
                return false;
            }

            return ForceGameWindowSize(hwnd, targetClientWidth, targetClientHeight, progressReporter);
        }

        public static bool ForceGameWindowSize(
            IntPtr gameWindowHandle,
            int targetClientWidth = 1280,
            int targetClientHeight = 720,
            Action<string>? progressReporter = null)
        {
            if (gameWindowHandle == IntPtr.Zero)
            {
                progressReporter?.Invoke("視窗句柄無效");
                return false;
            }

            try
            {
                if (IsIconic(gameWindowHandle) || IsZoomed(gameWindowHandle))
                {
                    progressReporter?.Invoke("視窗為最小化／最大化，先還原再改尺寸");
                    ShowWindow(gameWindowHandle, SW_RESTORE);
                    Thread.Sleep(80);
                }

                if (!TryGetClientSize(gameWindowHandle, out int currentClientWidth, out int currentClientHeight))
                {
                    progressReporter?.Invoke("無法取得視窗內容區域大小");
                    return false;
                }

                if (IsClientSizeMatch(currentClientWidth, currentClientHeight, targetClientWidth, targetClientHeight))
                {
                    progressReporter?.Invoke($"客戶區已是目標尺寸: {targetClientWidth}x{targetClientHeight}");
                    return true;
                }

                if (!TryComputeOuterSizeForClient(
                        gameWindowHandle,
                        targetClientWidth,
                        targetClientHeight,
                        out int finalWidth,
                        out int finalHeight))
                {
                    progressReporter?.Invoke("無法計算含邊框的外框尺寸");
                    return false;
                }

                progressReporter?.Invoke(
                    $"客戶區 {currentClientWidth}x{currentClientHeight} → 目標 {targetClientWidth}x{targetClientHeight}，外框 {finalWidth}x{finalHeight}");

                bool success = SetWindowPos(
                    gameWindowHandle,
                    IntPtr.Zero,
                    0, 0,
                    finalWidth,
                    finalHeight,
                    SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

                if (!success)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    progressReporter?.Invoke($"SetWindowPos 失敗，錯誤碼: {errorCode}");
                    Logger.Error($"[視窗管理] SetWindowPos 失敗，錯誤碼: {errorCode}");
                    return false;
                }

                Thread.Sleep(120);

                if (!TryGetClientSize(gameWindowHandle, out int afterW, out int afterH))
                    return false;

                if (!IsClientSizeMatch(afterW, afterH, targetClientWidth, targetClientHeight))
                {
                    // 遊戲常鎖解析度／無邊框全螢幕：API 成功但客戶區被立刻拉回
                    string msg =
                        $"強制後客戶區仍為 {afterW}x{afterH}（目標 {targetClientWidth}x{targetClientHeight}）。" +
                        "請在遊戲內改為「視窗模式」並關閉最大化後再試。";
                    progressReporter?.Invoke(msg);
                    Logger.Warning($"[視窗管理] {msg}");
                    return false;
                }

                progressReporter?.Invoke($"客戶區已校正: {afterW}x{afterH}");
                Logger.Info($"[視窗管理] 客戶區已校正為 {afterW}x{afterH}");
                return true;
            }
            catch (Exception ex)
            {
                progressReporter?.Invoke($"重置視窗大小時發生錯誤: {ex.Message}");
                Logger.Error($"[視窗管理] 重置視窗大小錯誤: {ex.Message}");
                return false;
            }
        }

        private static bool TryComputeOuterSizeForClient(
            IntPtr hwnd,
            int targetClientWidth,
            int targetClientHeight,
            out int outerWidth,
            out int outerHeight)
        {
            outerWidth = 0;
            outerHeight = 0;

            int style = GetWindowLong(hwnd, GWL_STYLE);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            var desired = new RECT
            {
                Left = 0,
                Top = 0,
                Right = targetClientWidth,
                Bottom = targetClientHeight
            };

            if (AdjustWindowRectEx(ref desired, style, false, exStyle))
            {
                outerWidth = desired.Right - desired.Left;
                outerHeight = desired.Bottom - desired.Top;
                if (outerWidth >= targetClientWidth && outerHeight >= targetClientHeight)
                    return true;
            }

            // 後備：以目前外框與客戶區差估算邊框（含陰影時較不準，但仍可用）
            if (!GetWindowRect(hwnd, out RECT windowRect))
                return false;

            var origin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(hwnd, ref origin))
                return false;

            if (!TryGetClientSize(hwnd, out int clientW, out int clientH))
                return false;

            int borderLeft = origin.X - windowRect.Left;
            int borderTop = origin.Y - windowRect.Top;
            int borderRight = windowRect.Right - (origin.X + clientW);
            int borderBottom = windowRect.Bottom - (origin.Y + clientH);

            outerWidth = targetClientWidth + borderLeft + borderRight;
            outerHeight = targetClientHeight + borderTop + borderBottom;
            return outerWidth > 0 && outerHeight > 0;
        }
    }
}
