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
            _settings = settings;
            _templateMatchingSettings = templateMatchingSettings;
            _currentConfig = config;
            System.Diagnostics.Debug.WriteLine($"✅ TemplateMatcher 已初始化（簡化版）");
        }

        #region 輔助方法

        private static List<CvPoint> GetMatchingLocations(Mat result, double threshold, bool useLessEqual)
        {
            if (result?.Empty() != false)
                return new List<CvPoint>();

            int maxResults = _settings.MaxDetectionResults;

            using var mask = new Mat();
            if (useLessEqual)
            {
                Cv2.Threshold(result, mask, threshold, 255, ThresholdTypes.BinaryInv);
            }
            else
            {
                Cv2.Threshold(result, mask, threshold, 255, ThresholdTypes.Binary);
            }

            using var nonZeroPoints = new Mat();
            Cv2.FindNonZero(mask, nonZeroPoints);

            // 檢查是否找到點
            if (nonZeroPoints.Empty())
                return new List<CvPoint>();

            //  轉換為 Point 陣列
            var matchingPoints = new CvPoint[nonZeroPoints.Rows];
            for (int i = 0; i < nonZeroPoints.Rows; i++)
            {
                matchingPoints[i] = nonZeroPoints.At<CvPoint>(i);
            }

            
            var candidatesArray = new (CvPoint location, float score)[matchingPoints.Length];

            Span<CvPoint> pointSpan = matchingPoints.AsSpan();
            for (int i = 0; i < pointSpan.Length; i++)
            {
                var pt = pointSpan[i];
                float score = result.At<float>(pt.Y, pt.X);
                candidatesArray[i] = (pt, score);
            }

            Array.Sort(candidatesArray, (a, b) =>
                useLessEqual ? a.score.CompareTo(b.score) : b.score.CompareTo(a.score));

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
            List<Mat> templateMats, 
            MonsterDetectionMode mode,
            double threshold = 0.7,
            string monsterName = "")
        {
            if (templateMats?.Count == 0) return new List<MatchResult>();

            var allResults = new List<MatchResult>(templateMats.Count * 3);

            foreach (var templateMat in templateMats)
            {
                if (templateMat == null || templateMat.Empty()) continue;

                try
                {
                    // 🎯 直接使用 Mat，無轉換開銷
                    var results = FindMonstersMatToMat(sourceMat, templateMat, mode, threshold, monsterName);
                    allResults.AddRange(results);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Mat模板匹配失敗: {ex.Message}");
                }
            }

            return allResults;
        }

        //  Mat 到 Mat 的直接匹配
        private static List<MatchResult> FindMonstersMatToMat(
            Mat sourceMat,
            Mat templateMat,
            MonsterDetectionMode mode,
            double threshold,
            string monsterName)
        {
            if (_settings?.MultiScaleFactors?.Length > 1)
            {
                return ProcessMultiScale(sourceMat, templateMat, mode, threshold, monsterName);
            }

            //  只有灰階需要特殊處理，其他都相同
            return mode == MonsterDetectionMode.Grayscale
                ? ProcessGrayscaleModeMat(sourceMat, templateMat, threshold, monsterName)
                : ProcessColorOrBasicMode(sourceMat, templateMat, threshold, monsterName);
        }

        //  Mat 優化版 Color 模式
        private static List<MatchResult> ProcessColorOrBasicMode(Mat sourceMat, Mat templateMat, double threshold, string monsterName)
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
                Debug.WriteLine($"❌ 模板匹配失敗: {ex.Message}");
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
            var scaleFactors = _settings.MultiScaleFactors;

            foreach (var scale in scaleFactors)
            {
                Mat scaledTemplate = null;
                bool needDispose = false;

                try
                {
                    // 縮放處理
                    if (Math.Abs(scale - 1.0) > 0.01)
                    {
                        var newSize = new OpenCvSharp.Size((int)(templateMat.Width * scale), (int)(templateMat.Height * scale));
                        scaledTemplate = new Mat();
                        Cv2.Resize(templateMat, scaledTemplate, newSize, 0, 0, InterpolationFlags.Linear);
                        needDispose = true;
                    }
                    else
                    {
                        scaledTemplate = templateMat;
                        needDispose = false;
                    }

                    // 尺寸檢查
                    if (scaledTemplate.Width > sourceMat.Width || scaledTemplate.Height > sourceMat.Height)
                        continue;

                    //  直接使用現有的簡化方法
                    var scaleResults = mode == MonsterDetectionMode.Grayscale
                        ? ProcessGrayscaleModeMat(sourceMat, scaledTemplate, threshold, $"{monsterName}@{scale:F1}x")
                        : ProcessColorOrBasicMode(sourceMat, scaledTemplate, threshold, $"{monsterName}@{scale:F1}x");

                    allResults.AddRange(scaleResults);
                }
                finally
                {
                    if (needDispose)
                        scaledTemplate?.Dispose();
                }
            }

            return allResults;
        }

        #endregion
    }
}
