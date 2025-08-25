using ArtaleAI.Config;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using SdPoint = System.Drawing.Point;
using ArtaleAI.Utils;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 智慧模板匹配器 - 每個辨識模式自動使用最佳遮擋處理
    /// </summary>
    public static class TemplateMatcher
    {
        private static MonsterDetectionSettings? _settings;

        private static Mat? _cachedSourceMat;
        private static string? _lastFrameHash;
        private static readonly Dictionary<string, Mat> _templateCache = new();

        /// <summary>
        /// 辨識模式與最佳遮擋處理的配對表
        /// </summary>
        private static readonly Dictionary<MonsterDetectionMode, OcclusionHandling> OptimalPairings = new()
        {
            { MonsterDetectionMode.Basic, OcclusionHandling.None },
            { MonsterDetectionMode.ContourOnly, OcclusionHandling.MorphologyRepair },
            { MonsterDetectionMode.Grayscale, OcclusionHandling.DynamicThreshold },
            { MonsterDetectionMode.Color, OcclusionHandling.MultiScale },
            { MonsterDetectionMode.TemplateFree, OcclusionHandling.MorphologyRepair }
        };

        /// <summary>
        /// 初始化模板匹配器
        /// </summary>
        public static void Initialize(MonsterDetectionSettings? settings)
        {
            _settings = settings ?? new MonsterDetectionSettings();
            System.Diagnostics.Debug.WriteLine($"✅ TemplateMatcher 已初始化 (智慧模式 - 三通道)");
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
            // 自動選擇最佳遮擋處理
            var optimalOcclusionHandling = GetOptimalOcclusionHandling(mode);
            System.Diagnostics.Debug.WriteLine($"🎯 {mode} 模式自動使用 {optimalOcclusionHandling} 遮擋處理");

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
        /// 獲取指定辨識模式的最佳遮擋處理
        /// </summary>
        private static OcclusionHandling GetOptimalOcclusionHandling(MonsterDetectionMode mode)
        {
            return OptimalPairings.TryGetValue(mode, out var handling)
                ? handling
                : OcclusionHandling.None;
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

                using var sourceImg = ImageUtils.BitmapToThreeChannelMat(sourceBitmap);
                Mat? templateImg = null;

                if (templateBitmap != null)
                {
                    templateImg = ImageUtils.BitmapToThreeChannelMat(templateBitmap);
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

                    System.Diagnostics.Debug.WriteLine($"✅ {mode} 模式找到 {results.Count} 個怪物 (三通道)");
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
                    Score = score
                });
            }

            return ApplySimpleNMS(results, 0.5);
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

            using var templateMask = ImageUtils.CreateBlackPixelMask(templateImg);
            using var sourceMask = ImageUtils.CreateBlackPixelMask(sourceImg);

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

            if (processedTemplateMask.CountNonZero() < 100 || processedSourceMask.CountNonZero() < 100)
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

            using var sourceGray4Ch = ImageUtils.ConvertToGrayscale(sourceImg);
            using var templateGray4Ch = ImageUtils.ConvertToGrayscale(templateImg);

            using var templateMask = ImageUtils.CreateThreeChannelTemplateMask(templateGray4Ch);
            using var result = new Mat();
            Cv2.MatchTemplate(sourceGray4Ch, templateGray4Ch, result, TemplateMatchModes.SqDiffNormed, templateMask);

            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out _, out _);
            Cv2.MeanStdDev(result, out Scalar mean, out Scalar stddev);

            double multiplier = _settings.DynamicThresholdMultiplier;
            double dynamicThreshold = Math.Min(threshold, mean.Val0 - stddev.Val0 * multiplier);
            dynamicThreshold = Math.Max(dynamicThreshold, threshold * 0.8);

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
            var results = new List<MatchResult>();
            using var templateMask = ImageUtils.CreateThreeChannelTemplateMask(templateImg);

            var scales = _settings.MultiScaleFactors;

            foreach (var scale in scales)
            {
                using var scaledTemplate = new Mat();
                using var scaledMask = new Mat();
                var newSize = new OpenCvSharp.Size((int)(templateImg.Width * scale), (int)(templateImg.Height * scale));
                Cv2.Resize(templateImg, scaledTemplate, newSize);
                Cv2.Resize(templateMask, scaledMask, newSize);

                using var result = new Mat();
                Cv2.MatchTemplate(sourceImg, scaledTemplate, result, TemplateMatchModes.SqDiffNormed, scaledMask);

                var locations = GetMatchingLocations(result, threshold, true);
                foreach (var loc in locations)
                {
                    double score = result.At<float>(loc.Y, loc.X);
                    results.Add(new MatchResult
                    {
                        Name = monsterName,
                        Position = new SdPoint(loc.X, loc.Y),
                        Size = new System.Drawing.Size(scaledTemplate.Width, scaledTemplate.Height),
                        Score = score,
                        Confidence = 1.0 - Math.Abs(scale - 1.0)
                    });
                }
            }

            return ApplySimpleNMS(results, _settings.NmsIouThreshold);
        }

        /// <summary>
        /// TemplateFree 模式：形態學修復 + 連通元件分析 - 三通道版本
        /// </summary>
        private static List<MatchResult> ProcessTemplateFreeMode(Mat sourceImg, Rectangle? characterBox)
        {
            var results = new List<MatchResult>();
            using var blackMask = ImageUtils.CreateBlackPixelMask(sourceImg);

            if (characterBox.HasValue)
            {
                var charRect = new Rect(characterBox.Value.X, characterBox.Value.Y,
                    characterBox.Value.Width, characterBox.Value.Height);
                blackMask[charRect].SetTo(new Scalar(0));
            }

            // ✅ 從設定讀取 TemplateFree 參數
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
        private static List<MatchResult> ApplySimpleNMS(List<MatchResult> results, double iouThreshold = 0.3)
        {
            if (results.Count <= 1) return results;

            var nmsResults = new List<MatchResult>();
            var sortedResults = results.OrderBy(r => r.Score).ToList(); // SqDiffNormed: 越小越好

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
                    return common.CalculateIoU(bestRect, candidateRect) > iouThreshold;
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

            // 收集所有候選位置
            var candidates = new List<(CvPoint location, float score)>();

            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    float score = result.At<float>(y, x);
                    bool isMatch = useLessEqual ? score <= threshold : score >= threshold;
                    if (isMatch)
                    {
                        candidates.Add((new CvPoint(x, y), score));
                    }
                }
            }

            // 排序並取前N個最佳結果
            var bestCandidates = useLessEqual
                ? candidates.OrderBy(c => c.score).Take(maxResults)
                : candidates.OrderByDescending(c => c.score).Take(maxResults);

            return bestCandidates.Select(c => c.location).ToList();
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

            // 獲取或創建快取的來源 Mat
            using var sharedSource = GetOrCreateCachedSourceMat(sourceBitmap);

            var allResults = new List<MatchResult>();

            for (int i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                var templateKey = $"{monsterName}_{i}";

                // 獲取或創建快取的模板 Mat
                using var templateMat = GetOrCreateCachedTemplate(templateKey, template);

                // 🔧 重要：使用現有的完整模式處理邏輯，不簡化
                var results = ProcessSingleTemplateWithSharedMats(
                    sharedSource, templateMat, mode, threshold, monsterName, characterBox);
                allResults.AddRange(results);
            }

            return allResults;
        }

        private static Mat GetOrCreateCachedSourceMat(Bitmap sourceBitmap)
        {
            var currentHash = $"{sourceBitmap.Width}x{sourceBitmap.Height}_{sourceBitmap.GetHashCode()}";
            if (_cachedSourceMat == null || _lastFrameHash != currentHash)
            {
                _cachedSourceMat?.Dispose();
                _cachedSourceMat = ImageUtils.BitmapToThreeChannelMat(sourceBitmap);
                _lastFrameHash = currentHash;
            }

            return _cachedSourceMat.Clone();
        }

        private static Mat GetOrCreateCachedTemplate(string key, Bitmap template)
        {
            if (!_templateCache.TryGetValue(key, out var cachedTemplate))
            {
                cachedTemplate = ImageUtils.BitmapToThreeChannelMat(template);
                _templateCache[key] = cachedTemplate;
            }

            return cachedTemplate.Clone();
        }

        /// <summary>
        /// 使用共享 Mat 處理單個模板 - 完全遵守設定檔
        /// </summary>
        private static List<MatchResult> ProcessSingleTemplateWithSharedMats(
            Mat sharedSource, Mat templateMat, MonsterDetectionMode mode,
            double threshold, string monsterName, Rectangle? characterBox)
        {
            // 🔧 關鍵：調用現有的完整模式處理邏輯
            // 不重新實作，只是傳入已轉換的 Mat
            return mode switch
            {
                MonsterDetectionMode.Basic =>
                    ProcessBasicModeWithSharedMats(sharedSource, templateMat, threshold, monsterName),
                MonsterDetectionMode.ContourOnly =>
                    ProcessContourModeWithSharedMats(sharedSource, templateMat, threshold, monsterName, characterBox),
                MonsterDetectionMode.Grayscale =>
                    ProcessGrayscaleModeWithSharedMats(sharedSource, templateMat, threshold, monsterName, characterBox),
                MonsterDetectionMode.Color =>
                    ProcessColorModeWithSharedMats(sharedSource, templateMat, threshold, monsterName, characterBox),
                MonsterDetectionMode.TemplateFree =>
                    ProcessTemplateFreeMode(sharedSource, characterBox), // 這個不需要模板
                _ => new List<MatchResult>()
            };
        }

        /// <summary>
        /// Basic 模式 - 使用共享 Mat，保持所有設定檔參數
        /// </summary>
        private static List<MatchResult> ProcessBasicModeWithSharedMats(
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
                    Score = score
                });
            }

            // 🔧 使用設定檔中的 NMS 參數
            return ApplySimpleNMS(results, _settings.NmsIouThreshold);
        }

        /// <summary>
        /// ContourOnly 模式 - 使用共享 Mat，完全遵守設定檔參數
        /// </summary>
        private static List<MatchResult> ProcessContourModeWithSharedMats(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName, Rectangle? characterBox)
        {
            var results = new List<MatchResult>();

            if (templateImg.Width > sourceImg.Width || templateImg.Height > sourceImg.Height)
                return results;

            using var templateMask = ImageUtils.CreateBlackPixelMask(templateImg);
            using var sourceMask = ImageUtils.CreateBlackPixelMask(sourceImg);

            if (characterBox.HasValue)
            {
                var charRect = new Rect(characterBox.Value.X, characterBox.Value.Y,
                    characterBox.Value.Width, characterBox.Value.Height);
                sourceMask[charRect].SetTo(new Scalar(0));
            }

            // 🔧 使用設定檔參數，不寫死
            int kernelSize = _settings.MorphologyKernelSize;
            int blurSize = _settings.ContourBlurSize;
            double adjustedThreshold = Math.Min(threshold, _settings.ContourThresholdLimit);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(kernelSize, kernelSize));
            using var processedSourceMask = new Mat();
            using var processedTemplateMask = new Mat();

            Cv2.MorphologyEx(sourceMask, processedSourceMask, MorphTypes.Close, kernel);
            Cv2.MorphologyEx(templateMask, processedTemplateMask, MorphTypes.Close, kernel);

            if (processedTemplateMask.CountNonZero() < 100 || processedSourceMask.CountNonZero() < 100)
                return results;

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

            // 🔧 使用設定檔中的 NMS 閾值
            return ApplySimpleNMS(results, _settings.NmsIouThreshold);
        }

        /// <summary>
        /// Grayscale 模式 - 使用共享 Mat，遵守動態閾值設定
        /// </summary>
        private static List<MatchResult> ProcessGrayscaleModeWithSharedMats(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName, Rectangle? characterBox)
        {
            var results = new List<MatchResult>();

            using var sourceGray4Ch = ImageUtils.ConvertToGrayscale(sourceImg);
            using var templateGray4Ch = ImageUtils.ConvertToGrayscale(templateImg);

            using var templateMask = ImageUtils.CreateThreeChannelTemplateMask(templateGray4Ch);
            using var result = new Mat();
            Cv2.MatchTemplate(sourceGray4Ch, templateGray4Ch, result, TemplateMatchModes.SqDiffNormed, templateMask);

            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out _, out _);
            Cv2.MeanStdDev(result, out Scalar mean, out Scalar stddev);

            // 🔧 使用設定檔中的動態閾值參數
            double multiplier = _settings.DynamicThresholdMultiplier;
            double dynamicThreshold = Math.Min(threshold, mean.Val0 - stddev.Val0 * multiplier);
            dynamicThreshold = Math.Max(dynamicThreshold, threshold * 0.8);

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
        /// Color 模式 - 使用共享 Mat，遵守多尺度設定
        /// </summary>
        private static List<MatchResult> ProcessColorModeWithSharedMats(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName, Rectangle? characterBox)
        {
            var results = new List<MatchResult>();
            using var templateMask = ImageUtils.CreateThreeChannelTemplateMask(templateImg);

            // 🔧 使用設定檔中的多尺度參數
            var scales = _settings.MultiScaleFactors;

            foreach (var scale in scales)
            {
                using var scaledTemplate = new Mat();
                using var scaledMask = new Mat();
                var newSize = new OpenCvSharp.Size((int)(templateImg.Width * scale), (int)(templateImg.Height * scale));

                Cv2.Resize(templateImg, scaledTemplate, newSize);
                Cv2.Resize(templateMask, scaledMask, newSize);

                using var result = new Mat();
                Cv2.MatchTemplate(sourceImg, scaledTemplate, result, TemplateMatchModes.SqDiffNormed, scaledMask);

                var locations = GetMatchingLocations(result, threshold, true);
                foreach (var loc in locations)
                {
                    double score = result.At<float>(loc.Y, loc.X);
                    results.Add(new MatchResult
                    {
                        Name = monsterName,
                        Position = new SdPoint(loc.X, loc.Y),
                        Size = new System.Drawing.Size(scaledTemplate.Width, scaledTemplate.Height),
                        Score = score,
                        Confidence = 1.0 - Math.Abs(scale - 1.0)
                    });
                }
            }

            return ApplySimpleNMS(results, _settings.NmsIouThreshold);
        }

        /// <summary>
        /// 清理快取（在應用程式結束時調用）
        /// </summary>
        public static void ClearCache()
        {
            _cachedSourceMat?.Dispose();
            _cachedSourceMat = null;
            _lastFrameHash = null;

            foreach (var mat in _templateCache.Values)
                mat?.Dispose();
            _templateCache.Clear();

            System.Diagnostics.Debug.WriteLine("🧹 TemplateMatcher 快取已清理");
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

        /// <summary>
        /// 清理資源
        /// </summary>
        public static void Dispose()
        {
            ImageUtils.SafeDispose(ref _cachedSourceMat);
            ImageUtils.SafeDispose(_templateCache);
            _lastFrameHash = null;
            System.Diagnostics.Debug.WriteLine("🧹 TemplateMatcher 快取已清理");
        }

        #endregion
    }
}
