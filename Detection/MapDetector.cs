using ArtaleAI.Config;
using ArtaleAI.GameWindow;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using System.Diagnostics;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace ArtaleAI.Detection
{
    public class MapDetector : IDisposable
    {
        private readonly AppConfig _config;
        private readonly Dictionary<string, Mat> _templates;

        public MapDetector(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _templates = new Dictionary<string, Mat>();
            LoadAllTemplates();
        }

        #region 模板載入

        /// <summary>
        /// 載入所有模板 - 三通道版本
        /// </summary>
        private void LoadAllTemplates()
        {
            var minimap = _config.Templates?.Minimap;
            var corners = minimap?.Corners;

            if (minimap == null)
            {
                Console.WriteLine("⚠️ Minimap 配置為空，跳過模板載入");
                return;
            }

            var templateConfigs = new Dictionary<string, TemplateConfig?>
            {
                ["PlayerMarker"] = minimap.PlayerMarker,
                ["OtherPlayers"] = minimap.OtherPlayers,
                ["TopLeft"] = corners?.TopLeft,
                ["TopRight"] = corners?.TopRight,
                ["BottomLeft"] = corners?.BottomLeft,
                ["BottomRight"] = corners?.BottomRight
            };

            foreach (var kvp in templateConfigs)
            {
                var templateConfig = kvp.Value;
                // 檢查 TemplateConfig 是否為 null 或路徑是否為空
                if (templateConfig?.Path == null)
                {
                    Console.WriteLine($"⚠️ 跳過模板 {kvp.Key}：配置為空或路徑未設定");
                    continue;
                }

                // 處理相對路徑
                string templatePath = templateConfig.Path;
                if (!Path.IsPathRooted(templatePath))
                {
                    templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, templateConfig.Path);
                }

                if (File.Exists(templatePath))
                {
                    try
                    {
                        var originalTemplate = Cv2.ImRead(templatePath, ImreadModes.Unchanged);
                        if (!originalTemplate.Empty())
                        {
                            var template = UtilityHelper.EnsureThreeChannels(originalTemplate);
                            originalTemplate.Dispose();

                            _templates[kvp.Key] = template;
                            Console.WriteLine($"✅ 已載入三通道模板: {kvp.Key} ({template.Width}x{template.Height}, {template.Channels()} 通道)");
                        }
                        else
                        {
                            originalTemplate.Dispose();
                            Console.WriteLine($"❌ 模板載入失敗 (空圖片): {kvp.Key} - {templatePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ 載入模板時發生錯誤: {kvp.Key} - {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"❌ 找不到模板檔案: {kvp.Key} - {templatePath}");
                }
            }

            Console.WriteLine($"模板載入完成，成功載入 {_templates.Count} 個三通道模板");
        }

        #endregion

        #region 小地圖檢測

        /// <summary>
        /// 在螢幕上尋找小地圖 - 三通道版本
        /// </summary>
        public Rectangle? FindMinimapOnScreen(Bitmap fullFrameBitmap)
        {
            if (fullFrameBitmap == null)
                return null;

            try
            {
                using var frameMat = UtilityHelper.BitmapToThreeChannelMat(fullFrameBitmap);

                // ✅ 修復：只保留一個定義，使用安全的空值檢查
                var cornerThreshold = _config.Templates.Minimap.CornerThreshold;

                Console.WriteLine($"🔍 開始小地圖檢測 (三通道)");
                Console.WriteLine($"📊 捕捉畫面大小: {fullFrameBitmap.Width}x{fullFrameBitmap.Height}");
                Console.WriteLine($"🎯 使用閾值: {cornerThreshold}");

                var topLeft = MatchTemplateInternal(frameMat, "TopLeft", cornerThreshold, true);
                var bottomRight = MatchTemplateInternal(frameMat, "BottomRight", cornerThreshold, true);

                Console.WriteLine($"🔍 TopLeft 三通道匹配結果: {(topLeft.HasValue ? $"成功 ({topLeft.Value.Location.X}, {topLeft.Value.Location.Y})" : "失敗")}");
                Console.WriteLine($"🔍 BottomRight 三通道匹配結果: {(bottomRight.HasValue ? $"成功 ({bottomRight.Value.Location.X}, {bottomRight.Value.Location.Y})" : "失敗")}");

                if (topLeft.HasValue && bottomRight.HasValue)
                {
                    var tl = topLeft.Value.Location;
                    var br = bottomRight.Value.Location;

                    if (_templates.TryGetValue("BottomRight", out var brTemplate))
                    {
                        int width = br.X + brTemplate.Width - tl.X;
                        int height = br.Y + brTemplate.Height - tl.Y;

                        Console.WriteLine($"📐 計算出的小地圖區域: ({tl.X}, {tl.Y}) -> {width}x{height}");

                        if (width > 0 && height > 0)
                        {
                            Console.WriteLine($"✅ 三通道小地圖檢測成功！");
                            return new Rectangle(tl.X, tl.Y, width, height);
                        }
                    }
                }

                Console.WriteLine($"❌ 三通道小地圖檢測失敗");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 三通道小地圖檢測異常: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 尋找玩家位置 - 三通道版本
        /// </summary>
        public System.Drawing.Point? FindPlayerPosition(Bitmap minimapImage)
        {
            if (minimapImage == null)
                return null;

            try
            {
                // 使用 Utils 轉換為三通道
                using var mat = UtilityHelper.BitmapToThreeChannelMat(minimapImage);
                var playerThreshold = _config.Templates.Minimap.PlayerThreshold;
                var matchResult = MatchTemplateInternal(mat, "PlayerMarker", playerThreshold, false);

                if (matchResult.HasValue && _templates.TryGetValue("PlayerMarker", out var template))
                {
                    var loc = matchResult.Value.Location;
                    var playerPos = new System.Drawing.Point(
                        loc.X + template.Width / 2,
                        loc.Y + template.Height / 2
                    );

                    Console.WriteLine($"✅ 三通道玩家位置檢測成功: ({playerPos.X}, {playerPos.Y})");
                    return playerPos;
                }

                Console.WriteLine($"❌ 三通道玩家位置檢測失敗");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"尋找玩家位置時發生錯誤: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 內部模板匹配方法 - 三通道版本
        /// </summary>
        private (System.Drawing.Point Location, double MaxValue)? MatchTemplateInternal(
            Mat inputMat, string templateName, double threshold, bool useGrayscale)
        {
            if (inputMat?.Empty() != false || !_templates.TryGetValue(templateName, out var template) || template.Empty())
            {
                Console.WriteLine($"⚠️ 模板匹配失敗：輸入或模板無效 ({templateName})");
                return null;
            }

            try
            {
                // 檢查尺寸
                if (template.Width > inputMat.Width || template.Height > inputMat.Height)
                {
                    Console.WriteLine($"⚠️ 模板 {templateName} 尺寸過大");
                    return null;
                }

                using (Mat result = new Mat())
                {
                    if (useGrayscale)
                    {
                        using var inputGray = UtilityHelper.ConvertToGrayscale(inputMat);
                        using var templateGray = UtilityHelper.ConvertToGrayscale(template);
                        Cv2.MatchTemplate(inputGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                    }
                    else
                    {
                        // 彩色匹配，直接使用三通道
                        Cv2.MatchTemplate(inputMat, template, result, TemplateMatchModes.CCoeffNormed);
                    }

                    Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                    Console.WriteLine($"🎯 {templateName} 三通道匹配分數: {maxVal:F4} (閾值: {threshold:F4})");

                    if (maxVal >= threshold)
                    {
                        return (new System.Drawing.Point(maxLoc.X, maxLoc.Y), maxVal);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"模板匹配時發生錯誤 ({templateName}): {ex.Message}");
            }

            return null;
        }

        #endregion

        #region 快照分析功能 (原 MapAnalyzer.cs)

        /// <summary>
        /// 執行一次性的螢幕捕捉，智慧偵測、裁切小地圖，並分析玩家位置。
        /// </summary>
        public async Task<MinimapSnapshotResult?> GetSnapshotAsync(nint windowHandle, AppConfig config, GraphicsCaptureItem? selectedItem, Action<string>? progressReporter)
        {
            GraphicsCapturer? capturer = null;
            try
            {
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
                    var minimapRect = FindMinimapOnScreen(fullFrame);
                    if (!minimapRect.HasValue)
                    {
                        progressReporter?.Invoke("找不到小地圖");
                        throw new Exception("無法偵測到小地圖區域");
                    }

                    var minimapBitmap = fullFrame.Clone(minimapRect.Value, fullFrame.PixelFormat);
                    var playerPosition = FindPlayerPosition(minimapBitmap);

                    // 5. 打包成結構化結果返回
                    return new MinimapSnapshotResult
                    {
                        MinimapImage = minimapBitmap,
                        PlayerPosition = playerPosition,
                        CaptureItem = selectedItem,
                        MinimapScreenRect = minimapRect.Value
                    };
                }
            }
            finally
            {
                // 確保所有在這個任務中建立的資源都被釋放
                capturer?.Dispose();
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
                if (config.General != null)
                {
                    config.General.LastSelectedWindowName = item.DisplayName;
                }

                // 嘗試獲取對應的程序資訊作為備用恢復方式
                try
                {
                    var process = Process.GetProcesses()
                        .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle) &&
                                   p.MainWindowTitle == item.DisplayName)
                        .FirstOrDefault();
                    if (process != null && config.General != null)
                    {
                        config.General.LastSelectedProcessName = process.ProcessName;
                        config.General.LastSelectedProcessId = process.Id;
                        progressReporter?.Invoke($"已記錄程序資訊: {process.ProcessName}");
                    }
                }
                catch (Exception ex)
                {
                    progressReporter?.Invoke($"程序資訊獲取失敗: {ex.Message}");
                }

                progressReporter?.Invoke("視窗記憶已保存，下次啟動將自動連接");
            }
            catch (Exception ex)
            {
                progressReporter?.Invoke($"保存視窗選擇時發生錯誤: {ex.Message}");
                // 不重新拋出異常，因為這不應該影響主要的捕捉功能
            }
        }

        #endregion

        public void Dispose()
        {
            UtilityHelper.SafeDispose(_templates);
            Console.WriteLine("🗑️ MapDetector 三通道模板已釋放");
        }
    }
}
