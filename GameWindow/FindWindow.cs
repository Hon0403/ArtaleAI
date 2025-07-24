using ArtaleAI.Config;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace ArtaleAI.GameWindow
{
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

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IGraphicsCaptureItemInterop factory);

        [DllImport("combase.dll")]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        public static GraphicsCaptureItem? TryCreateItemForWindow(string windowTitle, Action<string>? progressReporter = null)
        {
            // 直接使用 FindWindow，不再有命名衝突
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

        public static GraphicsCaptureItem? TryCreateItemWithFallback(AppConfig config, Action<string>? progressReporter = null)
        {
            progressReporter?.Invoke("=== 開始自動尋找視窗 ===");
            progressReporter?.Invoke($"預設視窗標題: '{config.General.GameWindowTitle}'");
            progressReporter?.Invoke($"上次記錄視窗: '{config.General.LastSelectedWindowName}'");
            progressReporter?.Invoke($"上次記錄程序: '{config.General.LastSelectedProcessName}'");

            // 1. 優先嘗試原本的視窗標題
            var item = TryCreateItemForWindow(config.General.GameWindowTitle, progressReporter);
            if (item != null)
            {
                progressReporter?.Invoke($"使用預設視窗標題找到: {config.General.GameWindowTitle}");
                return item;
            }
            progressReporter?.Invoke($"預設視窗標題找不到: {config.General.GameWindowTitle}");

            // 2. 嘗試上次成功的視窗名稱
            if (!string.IsNullOrEmpty(config.General.LastSelectedWindowName))
            {
                item = TryCreateItemForWindow(config.General.LastSelectedWindowName, progressReporter);
                if (item != null)
                {
                    progressReporter?.Invoke($"使用上次記錄的視窗: {config.General.LastSelectedWindowName}");
                    return item;
                }
                progressReporter?.Invoke($"上次記錄視窗找不到: {config.General.LastSelectedWindowName}");
            }
            else
            {
                progressReporter?.Invoke("上次記錄視窗名稱為空");
            }

            // 3. 嘗試透過程序名稱查找
            if (!string.IsNullOrEmpty(config.General.LastSelectedProcessName))
            {
                try
                {
                    var processes = Process.GetProcessesByName(config.General.LastSelectedProcessName);
                    foreach (var process in processes)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(process.MainWindowTitle))
                        {
                            item = TryCreateItemForWindow(process.MainWindowTitle, progressReporter);
                            if (item != null)
                            {
                                progressReporter?.Invoke($"透過程序名稱找到視窗: {config.General.LastSelectedProcessName}");
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
    }
}
