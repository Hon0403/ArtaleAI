using ArtaleAI.Config;
using ArtaleAI.GameWindow;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Diagnostics;
using Windows.Graphics.Capture;
using WinRT.Interop;
using SdPoint = System.Drawing.Point;
using CvPoint = OpenCvSharp.Point;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 地圖檢測器 - 記憶體優化版本
    /// </summary>
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

        #region 模板載入 - 記憶體優化版本
        /// <summary>
        /// 載入所有模板 - 記憶體優化版本
        /// </summary>
        private void LoadAllTemplates()
        {
            var minimap = _config.Templates?.Minimap;
            var corners = minimap?.Corners;

            if (minimap == null)
            {
                Debug.WriteLine("⚠️ Minimap 配置為空，跳過模板載入");
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
                if (templateConfig?.Path == null)
                {
                    Debug.WriteLine($"⚠️ 跳過模板 {kvp.Key}：配置為空或路徑未設定");
                    continue;
                }

                LoadSingleTemplate(kvp.Key, templateConfig);
            }

            Debug.WriteLine($"🎯 模板載入完成，成功載入 {_templates.Count} 個BGR格式模板");
        }

        /// <summary>
        /// 載入單個模板 - 記憶體安全版本
        /// </summary>
        private void LoadSingleTemplate(string templateName, TemplateConfig templateConfig)
        {
            string templatePath = templateConfig.Path;
            if (!Path.IsPathRooted(templatePath))
            {
                templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, templateConfig.Path);
            }

            if (!File.Exists(templatePath))
            {
                Debug.WriteLine($"❌ 找不到模板檔案: {templateName} - {templatePath}");
                return;
            }

            try
            {
                // 🚀 使用 ResourceManager 安全載入模板
                ResourceManager.SafeUseMat(
                    Cv2.ImRead(templatePath, ImreadModes.Unchanged),
                    originalTemplate =>
                    {
                        if (!originalTemplate.Empty())
                        {
                            // 使用 OpenCvProcessor 安全版本
                            _templates[templateName] = OpenCvProcessor.EnsureThreeChannels(originalTemplate);
                            Debug.WriteLine($"✅ 已載入BGR模板: {templateName} ({_templates[templateName].Width}x{_templates[templateName].Height}, {_templates[templateName].Channels()} 通道)");
                        }
                    });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 載入模板失敗: {templateName} - {ex.Message}");
            }
        }
        #endregion

        #region 小地圖檢測 - 記憶體優化版本
        /// <summary>
        /// 在螢幕上尋找小地圖 - 記憶體優化版本
        /// </summary>
        public Rectangle? FindMinimapOnScreen(Bitmap fullFrameBitmap)
        {
            if (fullFrameBitmap == null) return null;

            try
            {
                // 🚀 使用 OpenCvProcessor 安全版本
                return OpenCvProcessor.SafeBitmapToThreeChannelMat(fullFrameBitmap, frameMat =>
                {
                    var cornerThreshold = _config.Templates.Minimap.CornerThreshold;
                    Debug.WriteLine($"🔍 開始小地圖檢測（兩角匹配方式）");
                    Debug.WriteLine($"📊 捕捉畫面大小: {fullFrameBitmap.Width}x{fullFrameBitmap.Height}");
                    Debug.WriteLine($"🎯 使用閾值: {cornerThreshold}");

                    // 🎯 兩角匹配：只匹配對角線的兩個角落
                    var topLeft = MatchTemplateInternal(frameMat, "TopLeft", cornerThreshold, false);
                    var bottomRight = MatchTemplateInternal(frameMat, "BottomRight", cornerThreshold, false);

                    Debug.WriteLine($"🔍 TopLeft 匹配結果: {(topLeft.HasValue ? $"成功 ({topLeft.Value.Location.X}, {topLeft.Value.Location.Y})" : "失敗")}");
                    Debug.WriteLine($"🔍 BottomRight 匹配結果: {(bottomRight.HasValue ? $"成功 ({bottomRight.Value.Location.X}, {bottomRight.Value.Location.Y})" : "失敗")}");

                    // 🎯 兩角匹配計算
                    if (topLeft.HasValue && bottomRight.HasValue)
                    {
                        return CalculateMinimapRect(frameMat, topLeft.Value, bottomRight.Value);
                    }

                    Debug.WriteLine($"❌ 角落匹配不足 - TopLeft: {topLeft.HasValue}, BottomRight: {bottomRight.HasValue}");
                    return (Rectangle?)null;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"💥 兩角匹配小地圖檢測異常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 計算小地圖矩形區域
        /// </summary>
        private Rectangle? CalculateMinimapRect(Mat frameMat, (SdPoint Location, double MaxValue) topLeft, (SdPoint Location, double MaxValue) bottomRight)
        {
            var tl = topLeft.Location;
            var br = bottomRight.Location;

            Debug.WriteLine($"📍 TopLeft座標: ({tl.X}, {tl.Y})");
            Debug.WriteLine($"📍 BottomRight座標: ({br.X}, {br.Y})");

            if (_templates.TryGetValue("BottomRight", out var brTemplate))
            {
                // 基本邊界計算
                int left = tl.X;
                int top = tl.Y;
                int right = br.X + brTemplate.Width;
                int bottom = br.Y + brTemplate.Height;
                int width = right - left;
                int height = bottom - top;

                Debug.WriteLine($"📐 計算邊界: 左({left}) 上({top}) 右({right}) 下({bottom})");
                Debug.WriteLine($"📐 計算尺寸: {width}x{height}");

                // 🎯 基本驗證
                if (width > 50 && width < 400 && height > 50 && height < 400)
                {
                    var minimapRect = new Rectangle(left, top, width, height);

                    // 可視化兩角匹配結果
                    VisualizeTwoCornersSafe(frameMat, tl, br, minimapRect);

                    Debug.WriteLine($"✅ 兩角匹配小地圖檢測成功！區域: ({left},{top}) {width}x{height}");
                    return minimapRect;
                }
                else
                {
                    Debug.WriteLine($"❌ 尺寸驗證失敗: {width}x{height}");
                }
            }

            return null;
        }

        /// <summary>
        /// 可視化兩角匹配結果 - 記憶體安全版本
        /// </summary>
        private void VisualizeTwoCornersSafe(Mat frameMat, SdPoint topLeft, SdPoint bottomRight, Rectangle calculatedRect)
        {
            ResourceManager.SafeUseMat(frameMat.Clone(), visMat =>
            {
                // TopLeft - 綠色
                var tlCenter = new CvPoint(topLeft.X, topLeft.Y);
                Cv2.Circle(visMat, tlCenter, 15, new Scalar(0, 255, 0), 3);
                Cv2.Circle(visMat, tlCenter, 5, new Scalar(0, 255, 0), -1);
                Cv2.PutText(visMat, "TopLeft", new CvPoint(topLeft.X + 20, topLeft.Y - 10),
                    HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 255, 0), 2);

                // BottomRight - 紅色
                var brCenter = new CvPoint(bottomRight.X, bottomRight.Y);
                Cv2.Circle(visMat, brCenter, 15, new Scalar(0, 0, 255), 3);
                Cv2.Circle(visMat, brCenter, 5, new Scalar(0, 0, 255), -1);
                Cv2.PutText(visMat, "BottomRight", new CvPoint(bottomRight.X + 20, bottomRight.Y - 10),
                    HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 0, 255), 2);

                // 畫出計算的小地圖邊界 - 白色矩形
                Cv2.Rectangle(visMat,
                    new CvPoint(calculatedRect.X, calculatedRect.Y),
                    new CvPoint(calculatedRect.X + calculatedRect.Width, calculatedRect.Y + calculatedRect.Height),
                    new Scalar(255, 255, 255), 2);

                // 添加尺寸標籤
                Cv2.PutText(visMat, $"Size: {calculatedRect.Width}x{calculatedRect.Height}",
                    new CvPoint(calculatedRect.X, calculatedRect.Y - 15),
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(255, 255, 255), 2);

                // 保存結果
                string fileName = $"debug_two_corners_{DateTime.Now:HHmmss}.png";
                Cv2.ImWrite(fileName, visMat);
                Debug.WriteLine($"✅ 已保存兩角匹配可視化圖片: {fileName}");
            });
        }

        /// <summary>
        /// 內部模板匹配方法 - 記憶體優化版本
        /// </summary>
        private (System.Drawing.Point Location, double MaxValue)? MatchTemplateInternal(
            Mat inputMat, string templateName, double threshold, bool useGrayscale)
        {
            if (inputMat?.Empty() != false || !_templates.TryGetValue(templateName, out var template) || template.Empty())
            {
                Debug.WriteLine($"⚠️ 模板匹配失敗：輸入或模板無效 ({templateName})");
                return null;
            }

            try
            {
                if (template.Width > inputMat.Width || template.Height > inputMat.Height)
                {
                    Debug.WriteLine($"⚠️ 模板 {templateName} 尺寸過大");
                    return null;
                }

                // 🚀 使用 OpenCvProcessor 安全版本
                if (useGrayscale)
                {
                    return OpenCvProcessor.SafeProcessWithGrayscale(inputMat, (original, grayInput) =>
                    {
                        return OpenCvProcessor.SafeConvertToGrayscale(template, grayTemplate =>
                        {
                            return PerformTemplateMatch(grayInput, grayTemplate, templateName, threshold);
                        });
                    });
                }
                else
                {
                    return PerformTemplateMatch(inputMat, template, templateName, threshold);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"模板匹配時發生錯誤 ({templateName}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 執行模板匹配 - 記憶體安全版本
        /// </summary>
        private (System.Drawing.Point Location, double MaxValue)? PerformTemplateMatch(
            Mat inputMat, Mat templateMat, string templateName, double threshold)
        {
            return ResourceManager.SafeUseMat(new Mat(), result =>
            {
                Cv2.MatchTemplate(inputMat, templateMat, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                Debug.WriteLine($"🎯 {templateName} 匹配分數: {maxVal:F4} (閾值: {threshold:F4})");

                if (maxVal >= threshold)
                {
                    return ((System.Drawing.Point Location, double MaxValue)?)(new System.Drawing.Point(maxLoc.X, maxLoc.Y), maxVal);
                }

                return null;
            });
        }
        #endregion

        #region 快照分析功能 - 記憶體優化版本
        /// <summary>
        /// 執行一次性的螢幕捕捉 - 記憶體優化版本
        /// </summary>
        public async Task<MinimapSnapshotResult?> GetSnapshotAsync(nint windowHandle, AppConfig config, GraphicsCaptureItem? selectedItem, Action<string>? progressReporter)
        {
            GraphicsCapturer? capturer = null;
            try
            {
                // 1. 尋找或確認捕捉目標
                selectedItem = await GetOrSelectCaptureItem(windowHandle, config, selectedItem, progressReporter);
                if (selectedItem == null)
                {
                    progressReporter?.Invoke("未選擇視窗");
                    return null;
                }

                // 2. 建立捕捉器並抓取一幀
                capturer = new GraphicsCapturer(selectedItem);
                await Task.Delay(100);

                // 🚀 使用 ResourceManager 安全處理 Bitmap
                return ResourceManager.SafeUseBitmap(capturer.TryGetNextFrame(), fullFrame =>
                {
                    if (fullFrame == null)
                    {
                        progressReporter?.Invoke("無法擷取畫面");
                        return null;
                    }

                    var minimapRect = FindMinimapOnScreen(fullFrame);
                    if (!minimapRect.HasValue)
                    {
                        progressReporter?.Invoke("找不到小地圖");
                        throw new Exception("無法偵測到小地圖區域");
                    }

                    // 🚀 安全裁切小地圖
                    return ResourceManager.SafeUseBitmap(
                        fullFrame.Clone(minimapRect.Value, fullFrame.PixelFormat),
                        minimapBitmap => new MinimapSnapshotResult
                        {
                            MinimapImage = new Bitmap(minimapBitmap), // 創建副本
                            CaptureItem = selectedItem,
                            MinimapScreenRect = minimapRect.Value
                        });
                });
            }
            finally
            {
                capturer?.Dispose();
            }
        }

        /// <summary>
        /// 獲取或選擇捕獲項目
        /// </summary>
        private async Task<GraphicsCaptureItem?> GetOrSelectCaptureItem(nint windowHandle, AppConfig config, GraphicsCaptureItem? selectedItem, Action<string>? progressReporter)
        {
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

            return selectedItem;
        }

        /// <summary>
        /// 保存用戶手動選擇的視窗資訊
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
            }
        }
        #endregion

        public void Dispose()
        {
            CacheManager.SafeDispose(_templates);
            Debug.WriteLine("🗑️ MapDetector 模板已釋放");
        }
    }
}
