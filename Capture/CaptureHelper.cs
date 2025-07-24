using ArtaleAI.Configuration;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace ArtaleAI.Capture
{
    public static class CaptureHelper
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

        // ✅ 精簡版：移除多餘的驗證和詳細日誌
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

                var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(item_ptr);
                Marshal.Release(item_ptr);
                return item;
            }
            catch
            {
                return null;
            }
        }

        // ✅ 精簡版：只保留核心功能
        public static GraphicsCaptureItem? TryCreateItemWithFallback(AppConfig config, Action<string>? progressReporter = null)
        {
            progressReporter?.Invoke("正在連接遊戲視窗...");

            // 1. 嘗試預設視窗標題
            var item = TryCreateItemForWindow(config.General.GameWindowTitle);
            if (item != null)
            {
                progressReporter?.Invoke("✅ 視窗連接成功");
                return item;
            }

            // 2. 嘗試記憶的視窗名稱
            if (!string.IsNullOrEmpty(config.General.LastSelectedWindowName))
            {
                item = TryCreateItemForWindow(config.General.LastSelectedWindowName);
                if (item != null)
                {
                    progressReporter?.Invoke("✅ 視窗連接成功");
                    return item;
                }
            }

            // 3. 嘗試透過程序查找
            if (!string.IsNullOrEmpty(config.General.LastSelectedProcessName))
            {
                try
                {
                    var processes = Process.GetProcessesByName(config.General.LastSelectedProcessName);
                    foreach (var process in processes)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(process.MainWindowTitle))
                        {
                            item = TryCreateItemForWindow(process.MainWindowTitle);
                            if (item != null)
                            {
                                progressReporter?.Invoke("✅ 視窗連接成功");
                                return item;
                            }
                        }
                    }
                }
                catch { }
            }

            progressReporter?.Invoke("❌ 自動連接失敗，需要手動選擇");
            return null;
        }
    }
}
