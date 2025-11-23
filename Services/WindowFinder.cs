using ArtaleAI.Config;
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
    }
}
