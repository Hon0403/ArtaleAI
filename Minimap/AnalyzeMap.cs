using ArtaleAI.GameWindow;
using ArtaleAI.Config;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace ArtaleAI.Minimap
{
    /// <summary>
    /// 用於打包小地圖快照及其分析結果的資料結構。
    /// </summary>
    public class MinimapSnapshotResult
    {
        public Bitmap? MinimapImage { get; set; }
        public Point? PlayerPosition { get; set; }
        public GraphicsCaptureItem? CaptureItem { get; set; }
    }

    /// <summary>
    /// 提供與小地圖相關的服務，例如擷取快照並分析。
    /// </summary>
    public static class AnalyzeMap
    {
        /// <summary>
        /// 執行一次性的螢幕捕捉，智慧偵測、裁切小地圖，並分析玩家位置。
        /// </summary>
        /// <param name="windowHandle">主視窗的控制代碼，用於初始化視窗選擇器。</param>
        /// <param name="config">應用程式的設定檔。</param>
        /// <param name="selectedItem">先前已選擇的捕捉目標，如果為 null 則會重新尋找。</param>
        /// <param name="progressReporter">用於回報進度訊息的委派。</param>
        /// <returns>一個包含小地圖快照和分析數據的 MinimapSnapshotResult 物件。</returns>
        /// <exception cref="Exception">當發生無法處理的錯誤時拋出。</exception>
        public static async Task<MinimapSnapshotResult?> GetSnapshotAsync(nint windowHandle, AppConfig config, GraphicsCaptureItem? selectedItem, Action<string>? progressReporter)
        {
            GraphicsCapturer? capturer = null;
            DetectMap? minimapProcessor = null;

            try
            {
                minimapProcessor = new DetectMap(config);

                // 1. 尋找或確認捕捉目標
                if (selectedItem == null)
                {
                    progressReporter?.Invoke("正在嘗試自動找到遊戲視窗...");
                    selectedItem = WindowFinder.TryCreateItemWithFallback(config, progressReporter);

                    if (selectedItem == null)
                    {
                        progressReporter?.Invoke("自動尋找失敗，請手動選擇視窗。");
                        var picker = new GraphicsCapturePicker();
                        InitializeWithWindow.Initialize(picker, windowHandle);
                        selectedItem = await picker.PickSingleItemAsync();

                        if (selectedItem != null)
                        {
                            await SaveWindowSelection(selectedItem, config, progressReporter);
                            progressReporter?.Invoke($"已記住選擇: {selectedItem.DisplayName}");
                        }
                    }
                }

                if (selectedItem == null)
                {
                    progressReporter?.Invoke("未選擇視窗");
                    return null;
                }

                // 2. 建立捕捉器並抓取一幀
                capturer = new GraphicsCapturer(selectedItem);
                await Task.Delay(100); // 讓捕捉穩定

                using (var fullFrame = capturer.TryGetNextFrame())
                {
                    if (fullFrame == null)
                    {
                        progressReporter?.Invoke("無法擷取畫面");
                        return null;
                    }

                    // 3. 智慧偵測與裁切
                    var minimapRect = minimapProcessor.FindMinimapOnScreen(fullFrame);
                    if (!minimapRect.HasValue)
                    {
                        progressReporter?.Invoke("找不到小地圖");
                        throw new Exception("無法偵測到小地圖區域");
                    }

                    var minimapBitmap = fullFrame.Clone(minimapRect.Value, fullFrame.PixelFormat);
                    var playerPosition = minimapProcessor.FindPlayerPosition(minimapBitmap);

                    // 5. 打包成結構化結果返回
                    return new MinimapSnapshotResult
                    {
                        MinimapImage = minimapBitmap,
                        PlayerPosition = playerPosition,
                        CaptureItem = selectedItem
                    };
                }
            }
            finally
            {
                // 確保所有在這個任務中建立的資源都被釋放
                capturer?.Dispose();
                minimapProcessor?.Dispose();
            }
        }

        /// <summary>
        /// 保存用戶手動選擇的視窗資訊（視窗記憶功能的核心組件）
        /// </summary>
        private static async Task SaveWindowSelection(GraphicsCaptureItem item, AppConfig config, Action<string>? progressReporter)
        {
            try
            {
                progressReporter?.Invoke("正在保存視窗選擇到記憶中...");

                // 保存視窗名稱
                config.General.LastSelectedWindowName = item.DisplayName;

                // 嘗試獲取對應的程序資訊作為備用恢復方式
                try
                {
                    var process = Process.GetProcesses()
                        .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle) &&
                                   p.MainWindowTitle == item.DisplayName)
                        .FirstOrDefault();

                    if (process != null)
                    {
                        config.General.LastSelectedProcessName = process.ProcessName;
                        config.General.LastSelectedProcessId = process.Id;
                        progressReporter?.Invoke($"已記錄程序資訊: {process.ProcessName}");
                    }
                }
                catch (Exception ex)
                {
                    progressReporter?.Invoke($"程序資訊獲取失敗: {ex.Message}");
                    // 不影響主要功能，繼續執行
                }

                // 持久化到配置檔案
                ConfigSaver.SaveConfig(config);
                progressReporter?.Invoke("視窗記憶已保存，下次啟動將自動連接");
            }
            catch (Exception ex)
            {
                progressReporter?.Invoke($"保存視窗選擇時發生錯誤: {ex.Message}");
                // 不重新拋出異常，因為這不應該影響主要的捕捉功能
            }
        }

    }
}
