using ArtaleAI.Config;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 簡化版模板匹配器 - 減少過度預處理
    /// </summary>
    public static class TemplateMatcher
    {
        private static MonsterDetectionSettings? _settings;
        private static TemplateMatchingSettings? _templateMatchingSettings;
        private static AppConfig? _currentConfig;

        public static void Initialize(MonsterDetectionSettings? settings, TemplateMatchingSettings? templateMatchingSettings = null, AppConfig? config = null)
        {
            _settings = settings ?? new MonsterDetectionSettings();
            _templateMatchingSettings = templateMatchingSettings ?? new TemplateMatchingSettings();
            _currentConfig = config;
            System.Diagnostics.Debug.WriteLine($"✅ TemplateMatcher 已初始化（簡化版）");
        }

        public static List<MatchResult> FindMonsters(
            Bitmap sourceBitmap,
            Bitmap templateBitmap,
            MonsterDetectionMode mode,
            double threshold = 0.7,
            string monsterName = "",
            Rectangle? characterBox = null)
        {
            EnsureInitialized();
            return FindMonstersSimplified(sourceBitmap, templateBitmap, mode, threshold, monsterName, characterBox);
        }

        private static List<MatchResult> FindMonstersSimplified(
            Bitmap sourceBitmap,
            Bitmap templateBitmap,
            MonsterDetectionMode mode,
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
                        MonsterDetectionMode.Basic => ProcessBasicModeSimplified(sourceImg, templateImg!, threshold, monsterName),
                        MonsterDetectionMode.ContourOnly => ProcessContourModeSimplified(sourceImg, templateImg!, threshold, monsterName),
                        MonsterDetectionMode.Grayscale => ProcessGrayscaleModeSimplified(sourceImg, templateImg!, threshold, monsterName),
                        MonsterDetectionMode.Color => ProcessColorModeSimplified(sourceImg, templateImg!, threshold, monsterName),
                        MonsterDetectionMode.TemplateFree => ProcessTemplateFreeModeSimplified(sourceImg, characterBox),
                        _ => new List<MatchResult>()
                    };
                    Console.WriteLine($"🎯 {mode} 模式找到 {results.Count} 個怪物（簡化版）");
                }
                finally
                {
                    templateImg?.Dispose();
                }
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ {mode} 模式匹配失敗: {ex.Message}");
                return results;
            }
        }

        #region 簡化版處理方法

        /// <summary>
        /// Basic 模式：最簡單直接的匹配
        /// </summary>
        private static List<MatchResult> ProcessBasicModeSimplified(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName)
        {
            var results = new List<MatchResult>();
            using var result = new Mat();

            Console.WriteLine($"⚡ Basic 模式開始匹配");
            Cv2.MatchTemplate(sourceImg, templateImg, result, TemplateMatchModes.CCoeffNormed);

            var locations = GetMatchingLocations(result, threshold, false);
            foreach (var loc in locations)
            {
                float score = result.At<float>(loc.Y, loc.X);
                Console.WriteLine($"   Basic匹配: ({loc.X}, {loc.Y}), 分數:{score:F4}");

                results.Add(new MatchResult
                {
                    Name = monsterName,
                    Position = new SdPoint(loc.X, loc.Y),
                    Size = new System.Drawing.Size(templateImg.Width, templateImg.Height),
                    Score = score,
                    Confidence = score
                });
            }
            return results;
        }

        /// <summary>
        /// ContourOnly 模式：簡化版輪廓匹配
        /// </summary>
        private static List<MatchResult> ProcessContourModeSimplified(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName)
        {
            var results = new List<MatchResult>();
            Console.WriteLine($"🖼️ ContourOnly 模式（簡化為直接匹配）");

            try
            {
                using var result = new Mat();
                Cv2.MatchTemplate(sourceImg, templateImg, result, TemplateMatchModes.CCoeffNormed);

                var locations = GetMatchingLocations(result, threshold, false);
                Console.WriteLine($"✅ 找到 {locations.Count} 個候選");

                foreach (var loc in locations)
                {
                    float score = result.At<float>(loc.Y, loc.X);
                    Console.WriteLine($"   位置:({loc.X}, {loc.Y}), 分數:{score:F4}");

                    results.Add(new MatchResult
                    {
                        Name = monsterName,
                        Position = new SdPoint(loc.X, loc.Y),
                        Size = new System.Drawing.Size(templateImg.Width, templateImg.Height),
                        Score = score,
                        Confidence = score
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ContourOnly 模式處理失敗: {ex.Message}");
                Console.WriteLine($"❌ 堆疊追蹤: {ex.StackTrace}");
                return new List<MatchResult>();
            }
        }

        /// <summary>
        /// Grayscale 模式：簡化版灰階匹配
        /// </summary>
        private static List<MatchResult> ProcessGrayscaleModeSimplified(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName)
        {
            var results = new List<MatchResult>();

            using var sourceGray = new Mat();
            using var templateGray = new Mat();
            Cv2.CvtColor(sourceImg, sourceGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(templateImg, templateGray, ColorConversionCodes.BGR2GRAY);

            using var result = new Mat();
            Cv2.MatchTemplate(sourceGray, templateGray, result, TemplateMatchModes.CCoeffNormed);

            var locations = GetMatchingLocations(result, threshold, false);
            foreach (var loc in locations)
            {
                float score = result.At<float>(loc.Y, loc.X);
                Console.WriteLine($"   Grayscale匹配: ({loc.X}, {loc.Y}), 分數:{score:F4}");

                results.Add(new MatchResult
                {
                    Name = monsterName,
                    Position = new SdPoint(loc.X, loc.Y),
                    Size = new System.Drawing.Size(templateImg.Width, templateImg.Height),
                    Score = score,
                    Confidence = score
                });
            }
            return results;
        }

        /// <summary>
        /// Color 模式：簡化版彩色匹配
        /// </summary>
        private static List<MatchResult> ProcessColorModeSimplified(
            Mat sourceImg, Mat templateImg, double threshold, string monsterName)
        {
            var results = new List<MatchResult>();
            Console.WriteLine($"🎨 Color 模式 - 源圖:{sourceImg.Width}x{sourceImg.Height}, 模板:{templateImg.Width}x{templateImg.Height}");

            try
            {
                using var result = new Mat();
                Cv2.MatchTemplate(sourceImg, templateImg, result, TemplateMatchModes.CCoeffNormed);

                Console.WriteLine($"🎯 匹配結果矩陣型別: {result.Type()}");

                var locations = GetMatchingLocations(result, threshold, false);
                Console.WriteLine($"✅ 通過閾值的位置: {locations.Count} 個");

                foreach (var loc in locations)
                {
                    float score = result.At<float>(loc.Y, loc.X);
                    Console.WriteLine($"   位置:({loc.X}, {loc.Y}), 分數:{score:F4}");

                    results.Add(new MatchResult
                    {
                        Name = monsterName,
                        Position = new SdPoint(loc.X, loc.Y),
                        Size = new System.Drawing.Size(templateImg.Width, templateImg.Height),
                        Score = score,
                        Confidence = score
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Color 模式處理失敗: {ex.Message}");
                Console.WriteLine($"❌ 堆疊追蹤: {ex.StackTrace}");
                return new List<MatchResult>();
            }
        }

        /// <summary>
        /// TemplateFree 模式：保持原有復雜度（這個需要預處理）
        /// </summary>
        private static List<MatchResult> ProcessTemplateFreeModeSimplified(Mat sourceImg, Rectangle? characterBox)
        {
            var results = new List<MatchResult>();
            using var blackMask = UtilityHelper.CreateBlackPixelMask(sourceImg);

            if (characterBox.HasValue)
            {
                var charRect = new Rect(characterBox.Value.X, characterBox.Value.Y,
                    characterBox.Value.Width, characterBox.Value.Height);
                blackMask[charRect].SetTo(new Scalar(0));
            }

            int kernelSize = Math.Min(_settings.TemplateFreeKernelSize, 15);
            int openKernelSize = Math.Min(_settings.TemplateFreeOpenKernelSize, 5);
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
            return results;
        }

        #endregion

        #region 輔助方法

        private static List<CvPoint> GetMatchingLocations(Mat result, double threshold, bool useLessEqual)
        {
            var locations = new List<CvPoint>();
            int maxResults = _settings.MaxDetectionResults;

            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out _, out _);
            Console.WriteLine($"🎯 匹配統計 - Min: {minVal:F4}, Max: {maxVal:F4}, 閾值: {threshold:F4}");
            Console.WriteLine($"🎯 結果矩陣尺寸: {result.Width}x{result.Height}, 型別: {result.Type()}");

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
                        Console.WriteLine($"    找到候選: ({x},{y}) 分數={score:F4}");
                    }
                }
            }

            Console.WriteLine($"📊 候選總數: {candidates.Count}");

            var bestCandidates = useLessEqual
                ? candidates.OrderBy(c => c.score).Take(maxResults)
                : candidates.OrderByDescending(c => c.score).Take(maxResults);

            var finalResults = bestCandidates.Select(c => c.location).ToList();
            Console.WriteLine($"✅ 最終候選: {finalResults.Count} 個");

            return finalResults;
        }


        public static List<MatchResult> FindMonstersWithCache(
            Bitmap sourceBitmap,
            List<Bitmap> templates,
            MonsterDetectionMode mode,
            double threshold = 0.7,
            string monsterName = "",
            Rectangle? characterBox = null)
        {
            EnsureInitialized();
            Console.WriteLine($"🎯 開始簡化版模板匹配 - 模板數量: {templates.Count}, 模式: {mode}");

            if (templates == null || !templates.Any() || sourceBitmap == null)
                return new List<MatchResult>();

            var allResults = new List<MatchResult>();
            for (int i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                if (template == null) continue;

                try
                {
                    var results = FindMonsters(sourceBitmap, template, mode, threshold, monsterName, characterBox);
                    allResults.AddRange(results);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 模板 {i + 1} 匹配失敗: {ex.Message}");
                    continue;
                }
            }
            return allResults;
        }

        private static void EnsureInitialized()
        {
            if (_settings == null)
            {
                throw new InvalidOperationException("TemplateMatcher 未初始化！");
            }
        }

        #endregion
    }
}
