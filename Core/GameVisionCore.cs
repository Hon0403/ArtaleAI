using ArtaleAI.Config;
using ArtaleAI.GameWindow;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace ArtaleAI.Engine
{
    /// <summary>
    /// 整合的遊戲視覺核心 - 合併 GameDetectionEngine、MapDetector、VisionCore 的功能
    /// </summary>
    public class GameVisionCore : IDisposable
    {
        #region 私有字段
        private readonly Dictionary<string, Mat> _mapTemplates = new();
        private readonly Dictionary<string, List<Mat>> _monsterTemplateCache = new();
        private bool _disposed = false;
        #endregion

        #region 建構函式
        public GameVisionCore()
        {
            LoadMapTemplates();
        }
        #endregion

        #region 血條檢測功能群組

        /// <summary>
        /// 檢測血條位置 - 主要入口
        /// </summary>
        public Rectangle? DetectBloodBar(Mat frameMat, Rectangle? uiExcludeRect, AppConfig config, out int cameraOffsetY)
        {
            // 1. 提取相機區域（內嵌邏輯）
            Mat cameraArea;
            if (uiExcludeRect.HasValue)
            {
                var cameraHeight = uiExcludeRect.Value.Y;
                cameraOffsetY = 0;
                cameraArea = frameMat[new OpenCvSharp.Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
            else
            {
                var totalHeight = frameMat.Height;
                var uiHeight = config.UiHeightFromBottom;
                var cameraHeight = Math.Max(totalHeight - uiHeight, totalHeight / 2);
                cameraOffsetY = 0;
                cameraArea = frameMat[new OpenCvSharp.Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }

            using (cameraArea)
            {
                // 2. 轉換為 HSV 並創建紅色遮罩（內嵌邏輯）
                using var hsvImage = new Mat();
                Cv2.CvtColor(cameraArea, hsvImage, ColorConversionCodes.RGB2HSV);

                using var redMask = new Mat();
                var lowerRed = new Scalar(config.LowerRedHsv[0], config.LowerRedHsv[1], config.LowerRedHsv[2]);
                var upperRed = new Scalar(config.UpperRedHsv[0], config.UpperRedHsv[1], config.UpperRedHsv[2]);
                Cv2.InRange(hsvImage, lowerRed, upperRed, redMask);

                // 3. 找最佳血條（保留，因為邏輯複雜）
                var bestBar = FindBestRedBar(redMask, config);

                //  直接返回轉換後的座標,不需要額外函數
                return bestBar.HasValue
                    ? new Rectangle(bestBar.Value.X, bestBar.Value.Y + cameraOffsetY,
                                   bestBar.Value.Width, bestBar.Value.Height)
                    : null;
            }
        }

        /// <summary>
        /// 完整的血條檢測處理 - 包含相關計算
        /// </summary>
        public (Rectangle? BloodBar, List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            ProcessBloodBarDetection(Mat frameMat, Rectangle? uiExcludeRect)
        {
            var config = AppConfig.Instance;
            var bloodBar = DetectBloodBar(frameMat, uiExcludeRect, config, out _);

            if (bloodBar.HasValue)
            {
                //  一次計算兩種框架
                var (detectionBoxes, attackRangeBoxes) =
                    CalculateBloodBarRelatedBoxes(bloodBar.Value, config);

                return (bloodBar, detectionBoxes, attackRangeBoxes);
            }

            return (null, new List<Rectangle>(), new List<Rectangle>());
        }


        /// <summary>
        /// 計算血條相關的所有框架 (偵測框 + 攻擊範圍框)
        /// </summary>
        public (List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            CalculateBloodBarRelatedBoxes(Rectangle bloodBarRect, AppConfig config)
        {
            // 計算偵測框
            var dotCenterX = bloodBarRect.X + bloodBarRect.Width / 2;
            var dotCenterY = bloodBarRect.Y + bloodBarRect.Height + config.DotOffsetY;

            var detectionBox = new Rectangle(
                dotCenterX - config.DetectionBoxWidth / 2,
                dotCenterY - config.DetectionBoxHeight / 2,
                config.DetectionBoxWidth,
                config.DetectionBoxHeight
            );

            // 計算攻擊範圍框
            var playerCenterX = bloodBarRect.X + bloodBarRect.Width / 2 + config.AttackRange.OffsetX;
            var playerCenterY = bloodBarRect.Y + bloodBarRect.Height + config.AttackRange.OffsetY;

            var attackRangeBox = new Rectangle(
                playerCenterX - config.AttackRange.Width / 2,
                playerCenterY - config.AttackRange.Height / 2,
                config.AttackRange.Width,
                config.AttackRange.Height
            );

            return (
                new List<Rectangle> { detectionBox },
                new List<Rectangle> { attackRangeBox }
            );
        }

        #endregion

        #region 小地圖檢測功能群組

        /// <summary>
        /// 在螢幕上尋找小地圖位置
        /// </summary>
        public Rectangle? FindMinimapOnScreen(Mat fullFrameMat)
        {
            if (fullFrameMat?.Empty() != false) return null;

            try
            {
                var cornerThreshold = AppConfig.Instance.CornerThreshold;
                var topLeft = MatchTemplateInternal(fullFrameMat, "TopLeft", cornerThreshold);
                var bottomRight = MatchTemplateInternal(fullFrameMat, "BottomRight", cornerThreshold);

                if (topLeft.HasValue && bottomRight.HasValue)
                {
                    //  內嵌計算邏輯
                    var tl = topLeft.Value.Location;
                    var br = bottomRight.Value.Location;

                    if (_mapTemplates.TryGetValue("BottomRight", out var brTemplate))
                    {
                        int left = tl.X;
                        int top = tl.Y;
                        int right = br.X + brTemplate.Width;
                        int bottom = br.Y + brTemplate.Height;
                        int width = right - left;
                        int height = bottom - top;

                        if (width >= 50 && width <= 400 && height >= 50 && height <= 400)
                        {
                            return new Rectangle(left, top, width, height);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FindMinimapOnScreen: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 完整的小地圖追蹤處理 - 包含玩家位置檢測
        /// </summary>
        public MinimapTrackingResult? GetMinimapTracking(Mat fullFrameMat)
        {
            if (fullFrameMat?.Empty() != false) return null;

            try
            {
                var minimapRect = FindMinimapOnScreen(fullFrameMat);
                if (!minimapRect.HasValue) return null;

                using var minimapMat = new Mat(fullFrameMat, new OpenCvSharp.Rect(
                    minimapRect.Value.X, minimapRect.Value.Y,
                    minimapRect.Value.Width, minimapRect.Value.Height));

                //  直接內嵌玩家位置檢測
                System.Drawing.Point? playerPos = null;
                try
                {
                    var threshold = AppConfig.Instance.PlayerPositionThreshold;
                    var result = MatchTemplateInternal(minimapMat, "PlayerMarker", threshold);
                    playerPos = result?.Location;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"玩家位置檢測錯誤: {ex.Message}");
                }

                //  直接內嵌其他玩家檢測
                var otherPlayers = new List<System.Drawing.Point>();
                if (AppConfig.Instance.EnableOtherPlayersDetection == true)
                {
                    try
                    {
                        var threshold = AppConfig.Instance.PlayerPositionThreshold;
                        var result = MatchTemplateInternal(minimapMat, "OtherPlayers", threshold);
                        if (result.HasValue)
                            otherPlayers.Add(result.Value.Location);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"其他玩家檢測錯誤: {ex.Message}");
                    }
                }

                return new MinimapTrackingResult(
                    playerPos,
                    otherPlayers,
                    DateTime.Now,
                    1.0
                )
                {
                    MinimapBounds = minimapRect.Value
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"小地圖追蹤錯誤: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 怪物檢測功能群組

        /// <summary>
        /// 使用模板匹配尋找怪物
        /// </summary>
        public List<DetectionResult> FindMonsters(
            Mat sourceMat,
            List<Mat> templateMats,
            MonsterDetectionMode mode,
            double threshold = 0.7,
            string monsterName = "")
        {
            if (templateMats?.Count == 0) return new List<DetectionResult>();

            var allResults = new List<DetectionResult>();

            foreach (var templateMat in templateMats)
            {
                if (templateMat?.Empty() != false) continue;

                try
                {
                    //  直接內嵌匹配邏輯
                    using var result = new Mat();

                    if (mode == MonsterDetectionMode.Grayscale)
                    {
                        using var sourceGray = ConvertToGrayscale(sourceMat);
                        using var templateGray = ConvertToGrayscale(templateMat);
                        Cv2.MatchTemplate(sourceGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                    }
                    else
                    {
                        Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.CCoeffNormed);
                    }

                    if (!result.Empty())
                    {
                        using var mask = new Mat();
                        Cv2.Threshold(result, mask, threshold, 255, ThresholdTypes.Binary);

                        using var nonZeroPoints = new Mat();
                        Cv2.FindNonZero(mask, nonZeroPoints);

                        if (!nonZeroPoints.Empty())
                        {
                            int maxResults = Math.Min(nonZeroPoints.Rows, 10);
                            for (int i = 0; i < maxResults; i++)
                            {
                                var loc = nonZeroPoints.At<OpenCvSharp.Point>(i);
                                float score = result.At<float>(loc.Y, loc.X);

                                allResults.Add(new DetectionResult(
                                    monsterName,
                                    new System.Drawing.Point(loc.X, loc.Y),
                                    new System.Drawing.Size(templateMat.Width, templateMat.Height),
                                    score,
                                    new Rectangle(loc.X, loc.Y, templateMat.Width, templateMat.Height)
                                ));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"模板匹配錯誤: {ex.Message}");
                }
            }

            return allResults;
        }

        /// <summary>
        /// 載入怪物模板
        /// </summary>
        public async Task<List<Mat>> LoadMonsterTemplatesAsync(string monsterName, string monstersDirectory)
        {
            try
            {
                if (_monsterTemplateCache.TryGetValue(monsterName, out var cachedMats))
                    return cachedMats;

                string monsterFolderPath = Path.Combine(monstersDirectory, monsterName);
                if (!Directory.Exists(monsterFolderPath))
                    return new List<Mat>();

                var templateFiles = await Task.Run(() => Directory.GetFiles(monsterFolderPath, "*.png"));
                var loadedMatTemplates = new List<Mat>();

                foreach (var file in templateFiles)
                {
                    try
                    {
                        using var tempBitmap = new Bitmap(file);

                        if (tempBitmap == null || tempBitmap.Width < 5 || tempBitmap.Height < 5)
                            continue;

                        //  直接內嵌轉換邏輯（不拆函數）
                        using var originalMat = BitmapConverter.ToMat(tempBitmap);
                        using var rgbMat = new Mat();
                        Cv2.CvtColor(originalMat, rgbMat, ColorConversionCodes.BGR2RGB);

                        // 確保 3 通道（內嵌）
                        if (rgbMat.Channels() == 3)
                        {
                            loadedMatTemplates.Add(rgbMat.Clone());
                        }
                        else if (rgbMat.Channels() == 1)
                        {
                            using var output = new Mat();
                            Cv2.CvtColor(rgbMat, output, ColorConversionCodes.GRAY2RGB);
                            loadedMatTemplates.Add(output.Clone());
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"載入失敗 {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                _monsterTemplateCache[monsterName] = loadedMatTemplates;
                return loadedMatTemplates;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadMonsterTemplatesAsync 錯誤: {ex.Message}");
                return new List<Mat>();
            }
        }

        #endregion

        #region 私有輔助方法 - 血條相關
        private Rectangle? FindBestRedBar(Mat redMask, AppConfig config)
        {
            Mat? hierarchy = null;
            Mat[]? contours = null;

            try
            {
                hierarchy = new Mat();
                Cv2.FindContours(redMask, out contours, hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                var candidates = new List<(Rectangle rect, int area)>();

                for (int i = 0; i < contours.Length; i++)
                {
                    var contour = contours[i];
                    if (contour?.Empty() != false) continue;

                    try
                    {
                        var boundingRect = Cv2.BoundingRect(contour);
                        var rect = new Rectangle(boundingRect.X, boundingRect.Y,
                            boundingRect.Width, boundingRect.Height);

                        //  內嵌驗證邏輯
                        var width = rect.Width;
                        var height = rect.Height;
                        var area = width * height;

                        if (width >= config.MinBarWidth &&
                            width <= config.MaxBarWidth &&
                            height >= config.MinBarHeight &&
                            height <= config.MaxBarHeight &&
                            area >= config.MinBarArea)
                        {
                            candidates.Add((rect, area));
                        }
                    }
                    finally
                    {
                        contour?.Dispose();
                    }
                }

                if (candidates.Count == 0) return null;

                var bestCandidate = candidates.OrderByDescending(c => c.area).First();
                return bestCandidate.rect;
            }
            finally
            {
                hierarchy?.Dispose();
                if (contours != null)
                {
                    foreach (var contour in contours)
                    {
                        contour?.Dispose();
                    }
                }
            }
        }

        #endregion

        #region 私有輔助方法 - 小地圖相關

        private (System.Drawing.Point Location, double MaxValue)? MatchTemplateInternal(Mat inputMat, string templateName, double threshold, bool useGrayscale = false)
        {
            if (inputMat?.Empty() != false)
                return null;

            if (!_mapTemplates.TryGetValue(templateName, out var template) || template?.Empty() != false)
                return null;

            return MatchTemplate(inputMat, template, threshold, useGrayscale);
        }

        private void LoadMapTemplates()
        {
            var config = AppConfig.Instance;
            var templatePaths = new Dictionary<string, string>
            {
                ["PlayerMarker"] = config.PlayerMarker,
                ["OtherPlayers"] = config.OtherPlayers,
                ["TopLeft"] = config.TopLeft,
                ["TopRight"] = config.TopRight,
                ["BottomLeft"] = config.BottomLeft,
                ["BottomRight"] = config.BottomRight
            };

            foreach (var kvp in templatePaths)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;

                var templatePath = kvp.Value;
                if (!Path.IsPathRooted(templatePath))
                    templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, templatePath);

                if (!File.Exists(templatePath))
                {
                    Debug.WriteLine($"❌ 模板檔案不存在: {kvp.Key} -> {templatePath}");
                    continue;
                }

                try
                {
                    using var originalTemplate = Cv2.ImRead(templatePath, ImreadModes.Color);
                    if (originalTemplate.Empty())
                    {
                        Debug.WriteLine($"❌ 無法載入模板: {kvp.Key}");
                        continue;
                    }

                    using var rgbTemplate = new Mat();
                    Cv2.CvtColor(originalTemplate, rgbTemplate, ColorConversionCodes.BGR2RGB);

                    //  .NET 6 兼容的通道處理
                    Mat finalTemplate;
                    int channels = rgbTemplate.Channels();

                    if (channels == 3)
                    {
                        finalTemplate = rgbTemplate.Clone();
                    }
                    else if (channels == 1)
                    {
                        finalTemplate = new Mat();
                        Cv2.CvtColor(rgbTemplate, finalTemplate, ColorConversionCodes.GRAY2RGB);
                    }
                    else if (channels == 4)
                    {
                        finalTemplate = new Mat();
                        Cv2.CvtColor(rgbTemplate, finalTemplate, ColorConversionCodes.RGBA2RGB);
                    }
                    else
                    {
                        finalTemplate = rgbTemplate.Clone();
                    }

                    _mapTemplates[kvp.Key] = finalTemplate;
                    Debug.WriteLine($" 成功載入模板: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ 載入模板失敗: {kvp.Key} - {ex.Message}");
                }
            }
        }


        #endregion

        /// <summary>
        /// 將BGR圖像轉換為灰階
        /// </summary>
        public static Mat ConvertToGrayscale(Mat rgbMat)
        {
            if (rgbMat == null || rgbMat.Empty())
                throw new ArgumentException("Mat不能為null或空", nameof(rgbMat));

            var grayMat = new Mat();
            Cv2.CvtColor(rgbMat, grayMat, ColorConversionCodes.RGB2GRAY);
            return grayMat;
        }

        /// <summary>
        /// 轉換HSV值為OpenCV Scalar格式
        /// </summary>
        public static Scalar ToOpenCvHsv(int h, int s, int v)
        {
            return new Scalar(h, s, v);
        }

        public static (System.Drawing.Point Location, double MaxValue)? MatchTemplate(Mat inputMat, Mat templateMat, double threshold, bool useGrayscale = false)
        {
            if (inputMat?.Empty() != false || templateMat?.Empty() != false)
                return null;

            try
            {
                using var result = new Mat();
                if (useGrayscale)
                {
                    using var inputGray = ConvertToGrayscale(inputMat);
                    using var templateGray = ConvertToGrayscale(templateMat);
                    OpenCvSharp.Cv2.MatchTemplate(inputGray, templateGray, result, OpenCvSharp.TemplateMatchModes.CCoeffNormed);
                }
                else
                {
                    OpenCvSharp.Cv2.MatchTemplate(inputMat, templateMat, result, OpenCvSharp.TemplateMatchModes.CCoeffNormed);
                }
                OpenCvSharp.Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLoc);
                if (maxValue >= threshold) return (new System.Drawing.Point(maxLoc.X, maxLoc.Y), maxValue);
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析顏色字串為Color物件
        /// </summary>
        public static Color ParseColor(string? colorString)
        {
            if (string.IsNullOrWhiteSpace(colorString))
                return Color.White;

            try
            {
                var parts = colorString.Split(',');
                if (parts.Length == 3)
                {
                    var r = int.Parse(parts[0].Trim());
                    var g = int.Parse(parts[1].Trim());
                    var b = int.Parse(parts[2].Trim());

                    if (r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
                        return Color.FromArgb(r, g, b);
                }
            }
            catch
            {
                // 忽略解析錯誤，返回預設顏色
            }

            return Color.White;
        }

        /// <summary>
        /// 小地圖座標轉螢幕座標
        /// </summary>
        public static PointF MinimapToScreenF(PointF minimapPoint, Rectangle minimapBounds)
        {
            return new PointF(
                minimapBounds.X + minimapPoint.X,
                minimapBounds.Y + minimapPoint.Y
            );
        }

        /// <summary>
        /// 螢幕座標轉小地圖座標
        /// </summary>
        public static PointF ScreenToMinimapF(PointF screenPoint, Rectangle minimapBounds)
        {
            return new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y
            );
        }

        /// <summary>
        /// 應用非最大值抑制 (NMS)
        /// </summary>
        public static List<DetectionResult> ApplyNMS(List<DetectionResult> results, double iouThreshold = 0.3, bool higherIsBetter = true)
        {
            if (results == null || results.Count <= 1)
                return results?.ToList() ?? new List<DetectionResult>();

            var sorted = higherIsBetter
                ? results.OrderByDescending(r => r.Confidence).ToList()
                : results.OrderBy(r => r.Confidence).ToList();

            var kept = new List<DetectionResult>();
            var suppressed = new bool[sorted.Count];

            for (int i = 0; i < sorted.Count; i++)
            {
                if (suppressed[i]) continue;

                kept.Add(sorted[i]);

                var rectA = new Rectangle(sorted[i].Position, sorted[i].Size);

                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (suppressed[j]) continue;

                    var rectB = new Rectangle(sorted[j].Position, sorted[j].Size);
                    var iou = CalculateIoU(rectA, rectB);

                    if (iou > iouThreshold)
                        suppressed[j] = true;
                }
            }

            return kept;
        }

        /// <summary>
        /// 計算兩個矩形的IoU (Intersection over Union)
        /// </summary>
        private static double CalculateIoU(Rectangle rectA, Rectangle rectB)
        {
            var intersection = Rectangle.Intersect(rectA, rectB);
            if (intersection.IsEmpty)
                return 0.0;

            var intersectionArea = intersection.Width * intersection.Height;
            var unionArea = (rectA.Width * rectA.Height) + (rectB.Width * rectB.Height) - intersectionArea;

            return unionArea > 0 ? (double)intersectionArea / unionArea : 0.0;
        }

        /// <summary>
        /// 執行一次性的螢幕捕捉 - 記憶體優化版本
        /// </summary>
        public async Task<MinimapResult?> GetSnapshotAsync(nint windowHandle, AppConfig config, GraphicsCaptureItem? selectedItem, Action<string>? progressReporter)
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

                //  使用 ResourceManager 安全處理 Mat
                using var fullFrame = capturer.TryGetNextMat();
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

                //  安全裁切小地圖
                using var minimapMat = new Mat(fullFrame, new OpenCvSharp.Rect(
                    minimapRect.Value.X, minimapRect.Value.Y,
                    minimapRect.Value.Width, minimapRect.Value.Height));

                using var rgbMat = new Mat();
                Cv2.CvtColor(minimapMat, rgbMat, ColorConversionCodes.BGR2RGB);

                // 使用 RGB 格式的 Mat 轉換為 Bitmap
                var minimapBitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(rgbMat);

                return new MinimapResult(
                    minimapBitmap,           // MinimapImage
                    null,                    // PlayerPosition (如果沒有檢測到設為 null)
                    selectedItem,            // CaptureItem  
                    minimapRect.Value        // MinimapScreenRect
                );
            }
            catch (Exception ex)
            {
                progressReporter?.Invoke($"小地圖檢測失敗: {ex.Message}");
                Debug.WriteLine($"💥 GetSnapshotAsync 異常: {ex}");
                return null;
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
                if (AppConfig.Instance != null)
                {
                    AppConfig.Instance.LastSelectedWindowName = item.DisplayName;
                }

                // 嘗試獲取對應的程序資訊作為備用恢復方式
                try
                {
                    var process = Process.GetProcesses()
                        .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle) &&
                                   p.MainWindowTitle == item.DisplayName)
                        .FirstOrDefault();

                    if (process != null && AppConfig.Instance != null)
                    {
                        AppConfig.Instance.LastSelectedProcessName = process.ProcessName;
                        AppConfig.Instance.LastSelectedProcessId = process.Id;
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

        #region IDisposable
        public void Dispose()
        {
            if (!_disposed)
            {
                // 釋放地圖模板
                foreach (var template in _mapTemplates.Values)
                    template?.Dispose();
                _mapTemplates.Clear();

                // 釋放怪物模板
                foreach (var templates in _monsterTemplateCache.Values)
                {
                    foreach (var mat in templates)
                        mat?.Dispose();
                }
                _monsterTemplateCache.Clear();

                _disposed = true;
            }
        }
        #endregion
    }
}
