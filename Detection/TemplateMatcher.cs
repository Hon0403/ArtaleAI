using ArtaleAI.Config;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using System.Diagnostics;
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

        #region 輔助方法

        private static List<CvPoint> GetMatchingLocations(Mat result, double threshold, bool useLessEqual)
        {
            int maxResults = _settings.MaxDetectionResults;

            // 🚀 使用 OpenCV 內建函數直接產生遮罩
            using var mask = new Mat();
            if (useLessEqual)
            {
                Cv2.Threshold(result, mask, threshold, 255, ThresholdTypes.BinaryInv);
            }
            else
            {
                Cv2.Threshold(result, mask, threshold, 255, ThresholdTypes.Binary);
            }

            // 🚀 修正：正確使用 FindNonZero 方法（需要 OutputArray 參數）
            using var nonZeroPoints = new Mat();
            Cv2.FindNonZero(mask, nonZeroPoints);

            // 🚀 檢查是否找到點
            if (nonZeroPoints.Empty())
                return new List<CvPoint>();

            // 🚀 轉換為 Point 陣列
            var matchingPoints = new CvPoint[nonZeroPoints.Rows];
            for (int i = 0; i < nonZeroPoints.Rows; i++)
            {
                matchingPoints[i] = nonZeroPoints.At<CvPoint>(i);
            }

            // 🚀 使用陣列操作處理分數
            var candidatesArray = new (CvPoint location, float score)[matchingPoints.Length];

            // 🚀 使用 Span<T> 提升記憶體效能
            Span<CvPoint> pointSpan = matchingPoints.AsSpan();
            for (int i = 0; i < pointSpan.Length; i++)
            {
                var pt = pointSpan[i];
                float score = result.At<float>(pt.Y, pt.X);
                candidatesArray[i] = (pt, score);
            }

            // 🚀 陣列排序比 LINQ 快
            Array.Sort(candidatesArray, (a, b) =>
                useLessEqual ? a.score.CompareTo(b.score) : b.score.CompareTo(a.score));

            // 🚀 預分配結果陣列
            int resultCount = Math.Min(maxResults, candidatesArray.Length);
            var results = new List<CvPoint>(resultCount);

            for (int i = 0; i < resultCount; i++)
            {
                results.Add(candidatesArray[i].location);
            }

            return results;
        }

        public static List<MatchResult> FindMonstersWithMatOptimized(
            Mat sourceMat,
            List<Bitmap> templateBitmaps,
            MonsterDetectionMode mode,
            double threshold = 0.7,
            string monsterName = "")
        {
            if (templateBitmaps?.Count == 0) return new List<MatchResult>();

            // 🚀 預分配容量避免動態擴展
            var allResults = new List<MatchResult>(templateBitmaps.Count * 3);

            // 🚀 轉換為陣列，陣列索引比 foreach 快
            var templateArray = templateBitmaps.ToArray();

            for (int i = 0; i < templateArray.Length; i++)
            {
                var templateBitmap = templateArray[i];
                if (templateBitmap == null) continue;

                try
                {
                    using var templateMat = UtilityHelper.BitmapToThreeChannelMat(templateBitmap);
                    var results = FindMonstersMatToMat(sourceMat, templateMat, mode, threshold, monsterName);

                    // 🚀 使用 AddRange 批次新增
                    allResults.AddRange(results);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ 模板 {i} 匹配失敗: {ex.Message}");
                }
            }

            return allResults;
        }

        // 🚀 Mat 到 Mat 的直接匹配
        private static List<MatchResult> FindMonstersMatToMat(
            Mat sourceMat,
            Mat templateMat,
            MonsterDetectionMode mode,
            double threshold,
            string monsterName)
        {
            var results = mode switch
            {
                MonsterDetectionMode.Basic => ProcessBasicModeMat(sourceMat, templateMat, threshold, monsterName),
                MonsterDetectionMode.Color => ProcessColorModeMat(sourceMat, templateMat, threshold, monsterName),
                MonsterDetectionMode.Grayscale => ProcessGrayscaleModeMat(sourceMat, templateMat, threshold, monsterName),
                _ => new List<MatchResult>()
            };

            return ProcessMultiScale(sourceMat, templateMat, mode, threshold, monsterName);
        }

        // 🚀 Mat 優化版 Color 模式
        private static List<MatchResult> ProcessColorModeMat(Mat sourceMat, Mat templateMat, double threshold, string monsterName)
        {
            var results = new List<MatchResult>();
            try
            {
                using var result = new Mat();
                Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.CCoeffNormed);
                var locations = GetMatchingLocations(result, threshold, false);
                foreach (var loc in locations)
                {
                    float score = result.At<float>(loc.Y, loc.X);
                    results.Add(new MatchResult
                    {
                        Name = monsterName,
                        Position = new SdPoint(loc.X, loc.Y),
                        Size = new System.Drawing.Size(templateMat.Width, templateMat.Height),
                        Score = score,
                        Confidence = score
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Mat Color優化失敗: {ex.Message}");
                return results;
            }
        }

        private static List<MatchResult> ProcessBasicModeMat(Mat sourceMat, Mat templateMat, double threshold, string monsterName)
        {
            var results = new List<MatchResult>();
            try
            {
                using var result = new Mat();
                Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.CCoeffNormed);
                var locations = GetMatchingLocations(result, threshold, false);
                foreach (var loc in locations)
                {
                    float score = result.At<float>(loc.Y, loc.X);
                    results.Add(new MatchResult
                    {
                        Name = monsterName,
                        Position = new SdPoint(loc.X, loc.Y),
                        Size = new System.Drawing.Size(templateMat.Width, templateMat.Height),
                        Score = score,
                        Confidence = score
                    });
                }
                return results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Mat Basic優化失敗: {ex.Message}");
                return results;
            }
        }

        // Mat 優化版 Grayscale 模式
        private static List<MatchResult> ProcessGrayscaleModeMat(Mat sourceMat, Mat templateMat, double threshold, string monsterName)
        {
            var results = new List<MatchResult>();
            try
            {
                using var sourceGray = new Mat();
                using var templateGray = new Mat();
                Cv2.CvtColor(sourceMat, sourceGray, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(templateMat, templateGray, ColorConversionCodes.BGR2GRAY);
                using var result = new Mat();
                Cv2.MatchTemplate(sourceGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                var locations = GetMatchingLocations(result, threshold, false);
                foreach (var loc in locations)
                {
                    float score = result.At<float>(loc.Y, loc.X);
                    results.Add(new MatchResult
                    {
                        Name = monsterName,
                        Position = new SdPoint(loc.X, loc.Y),
                        Size = new System.Drawing.Size(templateMat.Width, templateMat.Height),
                        Score = score,
                        Confidence = score
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Grayscale優化失敗: {ex.Message}");
                return results;
            }
        }

        private static List<MatchResult> ProcessMultiScale(Mat sourceMat, Mat templateMat, MonsterDetectionMode mode, double threshold, string monsterName)
        {
            var allResults = new List<MatchResult>();
            var scaleFactors = _settings?.MultiScaleFactors ?? new double[] { 1.0 };

            Debug.WriteLine($"🔍 開始多尺度匹配，尺度因子: [{string.Join(", ", scaleFactors.Select(s => s.ToString("F1")))}]");

            foreach (var scale in scaleFactors)
            {
                Mat scaledTemplate = null;
                try
                {
                    // 縮放模板
                    if (Math.Abs(scale - 1.0) > 0.01)
                    {
                        var newSize = new OpenCvSharp.Size((int)(templateMat.Width * scale), (int)(templateMat.Height * scale));
                        scaledTemplate = new Mat();
                        Cv2.Resize(templateMat, scaledTemplate, newSize, 0, 0, InterpolationFlags.Linear);
                        Debug.WriteLine($"📏 模板縮放: {templateMat.Width}x{templateMat.Height} → {scaledTemplate.Width}x{scaledTemplate.Height}");
                    }
                    else
                    {
                        scaledTemplate = templateMat.Clone();
                    }

                    // 尺寸檢查
                    if (scaledTemplate.Width > sourceMat.Width || scaledTemplate.Height > sourceMat.Height)
                    {
                        Debug.WriteLine($"⚠️ 尺度 {scale:F1}x 後模板過大 ({scaledTemplate.Width}x{scaledTemplate.Height} vs {sourceMat.Width}x{sourceMat.Height})");
                        continue;
                    }

                    // 根據模式執行匹配
                    var scaleResults = mode switch
                    {
                        MonsterDetectionMode.Basic => ProcessBasicModeMat(sourceMat, scaledTemplate, threshold, $"{monsterName}@{scale:F1}x"),
                        MonsterDetectionMode.Color => ProcessColorModeMat(sourceMat, scaledTemplate, threshold, $"{monsterName}@{scale:F1}x"),
                        MonsterDetectionMode.Grayscale => ProcessGrayscaleModeMat(sourceMat, scaledTemplate, threshold, $"{monsterName}@{scale:F1}x"),
                        _ => new List<MatchResult>()
                    };

                    allResults.AddRange(scaleResults);
                    Debug.WriteLine($"✅ 尺度 {scale:F1}x 完成，找到 {scaleResults.Count} 個匹配");
                }
                finally
                {
                    if (scaledTemplate != templateMat)
                        scaledTemplate?.Dispose();
                }
            }

            Debug.WriteLine($"🎯 多尺度匹配完成，總共找到 {allResults.Count} 個結果");
            return allResults;
        }

        #endregion
    }
}
