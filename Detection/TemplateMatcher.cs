using ArtaleAI.Config;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 智慧模板匹配器 - 每個辨識模式自動使用最佳遮擋處理
    /// </summary>
    public static class TemplateMatcher
    {
        private static MonsterDetectionSettings? _settings;
        private static TemplateMatchingSettings? _templateMatchingSettings;
        private static AppConfig? _currentConfig;

        /// <summary>
        /// 初始化模板匹配器
        /// </summary>
        public static void Initialize(MonsterDetectionSettings? settings, TemplateMatchingSettings? templateMatchingSettings = null, AppConfig? config = null)
        {
            _settings = settings ?? new MonsterDetectionSettings();
            _templateMatchingSettings = templateMatchingSettings ?? new TemplateMatchingSettings();
            _currentConfig = config; //  儲存配置用於遮擋處理查找

            System.Diagnostics.Debug.WriteLine($" TemplateMatcher 已初始化 (統一配置版本)");
            System.Diagnostics.Debug.WriteLine($" 預設閾值: {_settings.DefaultThreshold}");
            System.Diagnostics.Debug.WriteLine($" 最大結果數: {_settings.MaxDetectionResults}");
        }

        /// <summary>
        /// 智慧怪物偵測 - 自動選擇最佳遮擋處理
        /// </summary>
        public static List<MatchResult> FindMonsters(
            Bitmap sourceBitmap,
            Bitmap templateBitmap,
            MonsterDetectionMode mode,
            double threshold = 0.7,
            string monsterName = "",
            Rectangle? characterBox = null)
        {
            EnsureInitialized();

            //  使用設定檔查找最佳遮擋處理
            var optimalOcclusionHandling = GetOptimalOcclusionHandlingFromConfig(mode);
            System.Diagnostics.Debug.WriteLine($"🎯 {mode} 模式自動使用 {optimalOcclusionHandling} 遮擋處理 (來自設定檔)");

            return FindMonstersWithOcclusionHandling(
                sourceBitmap,
                templateBitmap,
                mode,
                optimalOcclusionHandling,
                threshold,
                monsterName,
                characterBox);
        }

        /// <summary>
        /// 從設定檔獲取最佳遮擋處理 - 完全基於配置
        /// </summary>
        private static OcclusionHandling GetOptimalOcclusionHandlingFromConfig(MonsterDetectionMode mode)
        {
            var occlusionMappings = _currentConfig?.DetectionModes?.OcclusionMappings;
            var modeString = mode.ToString();

            if (occlusionMappings?.TryGetValue(modeString, out var occlusionString) == true)
            {
                return Enum.TryParse<OcclusionHandling>(occlusionString, out var result)
                    ? result
                    : OcclusionHandling.None;
            }

            System.Diagnostics.Debug.WriteLine($"⚠️ 警告：找不到模式 '{modeString}' 的遮擋處理設定，使用預設值");
            return OcclusionHandling.None;
        }

        /// <summary>
        /// 內部實作：具遮擋感知的模板匹配
        /// </summary>
        private static List<MatchResult> FindMonstersWithOcclusionHandling(
            Bitmap sourceBitmap,
            Bitmap templateBitmap,
            MonsterDetectionMode mode,
            OcclusionHandling occlusionMode,
            double threshold,
            string monsterName,
            Rectangle? characterBox)
        {
            var results = new List<MatchResult>();
            try
            {
                if (sourceBitmap == null) return results;
                if (mode != MonsterDetectionMode.TemplateFree && templateBitmap == null) return results;

                using var sourceImg = UtilityHelper.BitmapToThreeChannelMat(sourceBitmap);
                Mat? templateImg = null;

                if (templateBitmap != null)
                {
                    templateImg = UtilityHelper.BitmapToThreeChannelMat(templateBitmap);
                }

                try
                {
                    results = mode switch
                    {
                        MonsterDetectionMode.Basic =>
                            ProcessBasicMode(sourceImg, templateImg!, threshold, monsterName),
                        MonsterDetectionMode.ContourOnly =>
                            ProcessContourMode(sourceImg, templateImg!, threshold, monsterName, characterBox),
                        MonsterDetectionMode.Grayscale =>
                            ProcessGrayscaleMode(sourceImg, templateImg!, threshold, monsterName, characterBox),
                        MonsterDetectionMode.Color =>
                            ProcessColorMode(sourceImg, templateImg!, threshold, monsterName, characterBox),
                        MonsterDetectionMode.TemplateFree =>
                            ProcessTemplateFreeMode(sourceImg, characterBox),
                        _ => new List<MatchResult>()
                    };

                    System.Diagnostics.Debug.WriteLine($" {mode} 模式找到 {results.Count} 個怪物 (三通道)");
                }
                finally
                {
                    templateImg?.Dispose();
                }

                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ {mode} 模式匹配失敗: {ex.Message}");
                return results;
            }
        }

        #region 各模式的最佳化實作 - 三通道版本

        /// <summary>
        /// Basic 模式：無遮擋處理，追求速度 - 三通道版本
        /// </summary>
        private static List<MatchResult> ProcessBasicMode(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName)
        {
            var results = new List<MatchResult>();

            using var result = new Mat();
            Cv2.MatchTemplate(sourceImg, templateImg, result, TemplateMatchModes.CCoeffNormed);

            var locations = GetMatchingLocations(result, threshold, false);
            foreach (var loc in locations)
            {
                double score = result.At<float>(loc.Y, loc.X);
                results.Add(new MatchResult
                {
                    Name = monsterName,
                    Position = new SdPoint(loc.X, loc.Y),
                    Size = new System.Drawing.Size(templateImg.Width, templateImg.Height),
                    Score = score,
                    Confidence = Math.Max(0.0, Math.Min(1.0, score))
                });
            }
            double defaultNmsThreshold = _templateMatchingSettings.BasicModeNmsThreshold;

            return ApplySimpleNMS(results, _settings.NmsIouThreshold, lowerIsBetter: false); // Basic 用 CCoeffNormed，大分數更好
        }

        /// <summary>
        /// ContourOnly 模式：形態學修復輪廓斷裂 - 三通道版本
        /// </summary>
        private static List<MatchResult> ProcessContourMode(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName, Rectangle? characterBox)
        {
            var results = new List<MatchResult>();

            if (templateImg.Width > sourceImg.Width || templateImg.Height > sourceImg.Height)
                return results;

            using var templateMask = UtilityHelper.CreateBlackPixelMask(templateImg);
            using var sourceMask = UtilityHelper.CreateBlackPixelMask(sourceImg);

            if (characterBox.HasValue)
            {
                var charRect = new Rect(characterBox.Value.X, characterBox.Value.Y,
                    characterBox.Value.Width, characterBox.Value.Height);
                sourceMask[charRect].SetTo(new Scalar(0));
            }

            int kernelSize = _settings.MorphologyKernelSize;
            int blurSize = _settings.ContourBlurSize;
            double adjustedThreshold = Math.Min(threshold, _settings.ContourThresholdLimit);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(kernelSize, kernelSize));
            using var processedSourceMask = new Mat();
            using var processedTemplateMask = new Mat();

            Cv2.MorphologyEx(sourceMask, processedSourceMask, MorphTypes.Close, kernel);
            Cv2.MorphologyEx(templateMask, processedTemplateMask, MorphTypes.Close, kernel);

            int minContourPixels = _templateMatchingSettings.MinContourPixels;

            int minProcessedMaskPixels = _templateMatchingSettings.MinContourPixels;
            if (processedTemplateMask.CountNonZero() < minProcessedMaskPixels ||
                processedSourceMask.CountNonZero() < minProcessedMaskPixels)
            {
                return results;
            }

            using var templateBlur = new Mat();
            using var sourceBlur = new Mat();
            Cv2.GaussianBlur(processedTemplateMask, templateBlur, new OpenCvSharp.Size(blurSize, blurSize), 0);
            Cv2.GaussianBlur(processedSourceMask, sourceBlur, new OpenCvSharp.Size(blurSize, blurSize), 0);

            if (templateBlur.Height > sourceBlur.Height || templateBlur.Width > sourceBlur.Width)
                return results;

            using var result = new Mat();
            Cv2.MatchTemplate(sourceBlur, templateBlur, result, TemplateMatchModes.SqDiffNormed);

            var locations = GetMatchingLocations(result, adjustedThreshold, true);
            foreach (var loc in locations)
            {
                double score = result.At<float>(loc.Y, loc.X);
                if (loc.X >= 0 && loc.Y >= 0 && loc.X < result.Width && loc.Y < result.Height)
                {
                    results.Add(new MatchResult
                    {
                        Name = monsterName,
                        Position = new SdPoint(loc.X, loc.Y),
                        Size = new System.Drawing.Size(templateImg.Width, templateImg.Height),
                        Score = score,
                        Confidence = 1.0 - score
                    });
                }
            }

            return ApplySimpleNMS(results, _settings.NmsIouThreshold);
        }

        /// <summary>
        /// Grayscale 模式：動態閾值適應光照變化 - 三通道版本
        /// </summary>
        private static List<MatchResult> ProcessGrayscaleMode(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName, Rectangle? characterBox)
        {
            var results = new List<MatchResult>();

            using var sourceGray4Ch = UtilityHelper.ConvertToGrayscale(sourceImg);
            using var templateGray4Ch = UtilityHelper.ConvertToGrayscale(templateImg);
            using var templateMask = UtilityHelper.CreateThreeChannelTemplateMask(templateGray4Ch);
            using var result = new Mat();

            Cv2.MatchTemplate(sourceGray4Ch, templateGray4Ch, result, TemplateMatchModes.SqDiffNormed, templateMask);
            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out _, out _);
            Cv2.MeanStdDev(result, out Scalar mean, out Scalar stddev);

            double multiplier = _settings.DynamicThresholdMultiplier;
            double dynamicThreshold = Math.Min(threshold, mean.Val0 - stddev.Val0 * multiplier);

            double confidenceThreshold = _templateMatchingSettings?.ConfidenceThreshold ?? 0.8;
            dynamicThreshold = Math.Max(dynamicThreshold, threshold * confidenceThreshold);

            var locations = GetMatchingLocations(result, dynamicThreshold, true);

            foreach (var loc in locations)
            {
                double score = result.At<float>(loc.Y, loc.X);
                results.Add(new MatchResult
                {
                    Name = monsterName,
                    Position = new SdPoint(loc.X, loc.Y),
                    Size = new System.Drawing.Size(templateGray4Ch.Width, templateGray4Ch.Height),
                    Score = score,
                    Confidence = 1.0 - score / dynamicThreshold
                });
            }

            return ApplySimpleNMS(results, _settings.NmsIouThreshold);
        }

        /// <summary>
        /// Color 模式：多尺度匹配抗遮擋 - 三通道版本
        /// </summary>
        private static List<MatchResult> ProcessColorMode(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName, Rectangle? characterBox)
        {
            Console.WriteLine($"🎨 進入 Color 模式處理");
            if (sourceImg?.Empty() != false || templateImg?.Empty() != false)
            {
                Console.WriteLine($"❌ 輸入圖像無效");
                return new List<MatchResult>();
            }

            var results = new List<MatchResult>();
            try
            {
                // ✅ 修正：直接創建遮罩，不再檢查通道數
                using var templateMask = UtilityHelper.CreateThreeChannelTemplateMask(templateImg);
                var scales = _settings.MultiScaleFactors;

                Console.WriteLine($"📏 尺度數: {scales?.Length ?? 0}");

                foreach (var scale in scales)
                {
                    Console.WriteLine($"🔍 處理尺度: {scale}");
                    using var scaledTemplate = new Mat();
                    var newSize = new OpenCvSharp.Size((int)(templateImg.Width * scale), (int)(templateImg.Height * scale));
                    Cv2.Resize(templateImg, scaledTemplate, newSize);

                    using var result = new Mat();
                    using var scaledMask = new Mat();
                    Cv2.Resize(templateMask, scaledMask, newSize);

                    // ✅ 統一使用遮罩匹配
                    Cv2.MatchTemplate(sourceImg, scaledTemplate, result, TemplateMatchModes.CCoeffNormed, scaledMask);

                    var locations = GetMatchingLocations(result, threshold, false);
                    Console.WriteLine($"✅ 尺度 {scale} 找到 {locations.Count} 個候選");

                    foreach (var loc in locations)
                    {
                        float score = result.At<float>(loc.Y, loc.X);
                        results.Add(new MatchResult
                        {
                            Name = monsterName,
                            Position = new SdPoint(loc.X, loc.Y),
                            Size = new System.Drawing.Size(scaledTemplate.Width, scaledTemplate.Height),
                            Score = score,
                            Confidence = score
                        });
                    }
                }

                Console.WriteLine($"🎨 Color 模式處理完成，總共找到 {results.Count} 個結果");
                return ApplySimpleNMS(results, _settings.NmsIouThreshold, lowerIsBetter: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Color 模式處理失敗: {ex.Message}");
                return new List<MatchResult>();
            }
        }




        /// <summary>
        /// TemplateFree 模式：形態學修復 + 連通元件分析 - 三通道版本
        /// </summary>
        private static List<MatchResult> ProcessTemplateFreeMode(Mat sourceImg, Rectangle? characterBox)
        {
            var results = new List<MatchResult>();
            using var blackMask = UtilityHelper.CreateBlackPixelMask(sourceImg);

            if (characterBox.HasValue)
            {
                var charRect = new Rect(characterBox.Value.X, characterBox.Value.Y,
                    characterBox.Value.Width, characterBox.Value.Height);
                blackMask[charRect].SetTo(new Scalar(0));
            }

            int kernelSize = _settings.TemplateFreeKernelSize;
            int openKernelSize = _settings.TemplateFreeOpenKernelSize;
            int minArea = _settings.MinDetectionArea;
            int maxArea = _settings.MaxDetectionArea;
            double aspectRatioLimit = _settings.AspectRatioLimit;

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(kernelSize, kernelSize));
            using var closedMask = new Mat();
            Cv2.MorphologyEx(blackMask, closedMask, MorphTypes.Close, kernel);

            using var openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(openKernelSize, openKernelSize));
            using var refinedMask = new Mat();
            Cv2.MorphologyEx(closedMask, refinedMask, MorphTypes.Open, openKernel);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int numLabels = Cv2.ConnectedComponentsWithStats(refinedMask, labels, stats, centroids, PixelConnectivity.Connectivity8);

            for (int i = 1; i < numLabels; i++)
            {
                int area = stats.At<int>(i, 4);
                if (area > minArea && area < maxArea)
                {
                    int x = stats.At<int>(i, 0);
                    int y = stats.At<int>(i, 1);
                    int w = stats.At<int>(i, 2);
                    int h = stats.At<int>(i, 3);

                    double aspectRatio = Math.Max(w, h) / (double)Math.Min(w, h);
                    if (aspectRatio < aspectRatioLimit)
                    {
                        results.Add(new MatchResult
                        {
                            Name = "Unknown",
                            Position = new SdPoint(x, y),
                            Size = new System.Drawing.Size(w, h),
                            Score = 1.0,
                            Confidence = Math.Min(1.0, (double)area / (w * h))
                        });
                    }
                }
            }

            return ApplySimpleNMS(results, _settings.NmsIouThreshold);
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 簡單的非極大值抑制
        /// </summary>
        private static List<MatchResult> ApplySimpleNMS(List<MatchResult> results, double iouThreshold = 0.3, bool lowerIsBetter = true)
        {
            if (results.Count <= 1) return results;

            if (iouThreshold < 0)
            {
                iouThreshold = _templateMatchingSettings.DefaultIouThreshold;
            }

            var nmsResults = new List<MatchResult>();

            var sortedResults = lowerIsBetter
                ? results.OrderBy(r => r.Score).ToList()      // SqDiffNormed：小分數更好
                : results.OrderByDescending(r => r.Score).ToList(); // CCoeffNormed：大分數更好

            while (sortedResults.Any())
            {
                var best = sortedResults.First();
                nmsResults.Add(best);
                sortedResults.RemoveAt(0);

                var bestRect = new Rectangle(best.Position.X, best.Position.Y,
                    best.Size.Width, best.Size.Height);

                sortedResults.RemoveAll(candidate =>
                {
                    var candidateRect = new Rectangle(candidate.Position.X, candidate.Position.Y,
                        candidate.Size.Width, candidate.Size.Height);
                    return UtilityHelper.CalculateIoU(bestRect, candidateRect) > iouThreshold;
                });
            }

            return nmsResults;
        }

        /// <summary>
        /// 獲取匹配位置（含數量限制）
        /// </summary>
        private static List<CvPoint> GetMatchingLocations(Mat result, double threshold, bool useLessEqual)
        {
            var locations = new List<CvPoint>();
            int maxResults = _settings.MaxDetectionResults;

            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out _, out _);
            Console.WriteLine($"🎯 匹配統計 - Min: {minVal:F4}, Max: {maxVal:F4}, 閾值: {threshold:F4}");

            var candidates = new List<(CvPoint location, float score)>();
            int matchCount = 0;

            // 🔥 修正：使用正確的類型讀取
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    float score = result.At<float>(y, x); // 確保使用 float
                    bool isMatch = useLessEqual ? score <= threshold : score >= threshold;

                    if (isMatch)
                    {
                        candidates.Add((new CvPoint(x, y), score));
                        matchCount++;
                    }
                }
            }

            Console.WriteLine($"🔍 符合閾值的候選: {matchCount} 個");

            var bestCandidates = useLessEqual
                ? candidates.OrderBy(c => c.score).Take(maxResults)
                : candidates.OrderByDescending(c => c.score).Take(maxResults);

            var finalLocations = bestCandidates.Select(c => c.location).ToList();
            Console.WriteLine($"✅ 最終返回 {finalLocations.Count} 個位置");

            return finalLocations;
        }

        /// <summary>
        /// 帶快取的批量怪物偵測 - 完全使用設定檔參數
        /// </summary>
        public static List<MatchResult> FindMonstersWithCache(
            Bitmap sourceBitmap,
            List<Bitmap> templates,
            MonsterDetectionMode mode,
            double threshold = 0.7,
            string monsterName = "",
            Rectangle? characterBox = null)
        {
            EnsureInitialized();

            Console.WriteLine($"🎯 開始模板匹配 - 模板數量: {templates.Count}, 模式: {mode}");

            if (templates == null || !templates.Any())
            {
                Console.WriteLine("❌ 模板列表為空，無法進行匹配");
                return new List<MatchResult>();
            }

            if (sourceBitmap == null)
            {
                Console.WriteLine("❌ 源圖像為空，無法進行匹配");
                return new List<MatchResult>();
            }

            var allResults = new List<MatchResult>();

            for (int i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                try
                {
                    Console.WriteLine($"🔍 處理模板 {i + 1}/{templates.Count}");

                    if (template == null)
                    {
                        Console.WriteLine($"⚠️ 模板 {i + 1} 為空，跳過");
                        continue;
                    }

                    var results = FindMonsters(sourceBitmap, template, mode, threshold, monsterName, characterBox);
                    Console.WriteLine($"✅ 模板 {i + 1} 匹配完成，找到 {results.Count} 個結果");
                    allResults.AddRange(results);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 模板 {i + 1} 匹配失敗: {ex.Message}");
                    Console.WriteLine($"🔍 詳細錯誤: {ex.StackTrace}");
                    continue; // 繼續處理下一個模板
                }
            }

            Console.WriteLine($"🏁 所有模板處理完成，總結果: {allResults.Count}");
            return allResults;
        }

        /// <summary>
        /// 確保已初始化
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_settings == null)
            {
                throw new InvalidOperationException(
                    "TemplateMatcher 未初始化！請先呼叫 Initialize() 並傳入有效的 MonsterDetectionSettings");
            }
        }

        #endregion
    }
}
