using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Shared;
using ArtaleAI.Infrastructure.Capture;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace ArtaleAI.Vision
{
    /// <summary>整合的遊戲視覺核心。</summary>
    public partial class GameVisionCore
    {
        #region 綠色遮罩工具

        /// <summary>
        /// 建立綠色背景遮罩
        /// 綠色區域 (0, 255, 0) 會被標記為 0 (忽略)
        /// 非綠色區域會被標記為 255 (用於匹配)
        /// </summary>
        /// <param name="template">模板影像 (BGR 格式)</param>
        /// <returns>遮罩 Mat (與模板相同通道數)，需要調用者負責 Dispose</returns>
        public static Mat CreateGreenMask(Mat template)
        {
            if (template?.Empty() != false) return new Mat();

            try
            {
                var lowerGreen = new Scalar(0, 240, 0);
                var upperGreen = new Scalar(20, 255, 20);

                using var greenMask = new Mat();
                Cv2.InRange(template, lowerGreen, upperGreen, greenMask);

                using var invertedMask = new Mat();
                Cv2.BitwiseNot(greenMask, invertedMask);

                var element = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
                Cv2.Erode(invertedMask, invertedMask, element, null, 1);

                var result = new Mat();
                if (template.Channels() == 3)
                {
                    Cv2.Merge(new[] { invertedMask, invertedMask, invertedMask }, result);
                }
                else if (template.Channels() == 4)
                {
                    Cv2.Merge(new[] { invertedMask, invertedMask, invertedMask, invertedMask }, result);
                }
                else
                {
                    result = invertedMask.Clone();
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"[遮罩] 建立綠色遮罩錯誤: {ex.Message}");
                return new Mat();
            }
        }

        #endregion

        #region 怪物檢測功能群組

        /// <summary>
        /// 使用模板匹配尋找怪物
        /// 支援多種檢測模式（彩色、灰階等）
        /// 支援綠色背景遮罩以提高匹配準確度
        /// </summary>
        /// <param name="sourceMat">來源影像 Mat</param>
        /// <param name="templateMats">怪物模板列表</param>
        /// <param name="mode">檢測模式</param>
        /// <param name="threshold">檢測閾值（0.0-1.0）</param>
        /// <param name="monsterName">怪物名稱（用於結果標記）</param>
        /// <param name="templateMasks">模板遮罩列表（可選，與 templateMats 一一對應）</param>
        /// <returns>檢測結果列表</returns>
        public List<DetectionResult> FindMonsters(
            Mat sourceMat,
            MonsterTemplateBundle bundle,
            MonsterDetectionMode detectionMode,
            double threshold = 0.7)
        {
            return FindMonstersWithStats(sourceMat, bundle, detectionMode, threshold).Results;
        }

        public (List<DetectionResult> Results, MonsterTemplateMatchStats Stats) FindMonstersWithStats(
            Mat sourceMat,
            MonsterTemplateBundle bundle,
            MonsterDetectionMode mode,
            double threshold = 0.7)
        {
            if (bundle == null || bundle.IsEmpty)
            {
                return (new List<DetectionResult>(), new MonsterTemplateMatchStats(
                    0, 0, 0, 0, 0, 0, false));
            }

            return MatchTemplateEntries(
                sourceMat,
                bundle.Entries,
                mode,
                threshold,
                bundle.MonsterName);
        }

        /// <summary>
        /// 使用模板匹配尋找怪物（舊版清單 API；無預計算 mask 時才 fallback）。
        /// </summary>
        [Obsolete("請改用 MonsterTemplateBundle 以使用預計算 mask。")]
        public List<DetectionResult> FindMonsters(
            Mat sourceMat,
            List<Mat> templateMats,
            MonsterDetectionMode mode,
            double threshold = 0.7,
            string monsterName = "",
            List<Mat>? templateMasks = null)
        {
            if (templateMats == null || templateMats.Count == 0) return new List<DetectionResult>();

            return MatchLegacyTemplates(
                sourceMat, templateMats, mode, threshold, monsterName, templateMasks);
        }

        private const int CoarseTopK = 3;
        private const double CoarseMinBestScore = 0.35;
        /// <summary>對齊 KenYu <c>monster_detect.contour_blur</c> 預設。</summary>
        private const int DefaultContourBlur = 5;

        private (List<DetectionResult> Results, MonsterTemplateMatchStats Stats) MatchTemplateEntries(
            Mat sourceMat,
            IReadOnlyList<MonsterTemplateEntry> entries,
            MonsterDetectionMode mode,
            double threshold,
            string monsterName)
        {
            using var sourceGray = mode == MonsterDetectionMode.Grayscale
                ? ConvertToGrayscale(sourceMat)
                : null;
            // KenYu ContourOnly：整張 ROI 轉成「精確黑像素」遮罩再模糊，再跟模板黑輪廓比
            using var sourceContour = mode == MonsterDetectionMode.ContourOnly
                ? BuildBlackContourMask(sourceMat, ResolveContourBlur())
                : null;

            var coarseSelection = SelectCoarseMatchIndices(
                sourceMat, sourceGray, sourceContour, entries, mode);

            var bag = new List<DetectionResult>();
            var fineWatch = Stopwatch.StartNew();

            // OpenCV Mat 非執行緒安全：不可對同一張 source 平行 MatchTemplate（會 AV）
            foreach (var index in coarseSelection.Indices)
            {
                var entry = entries[index];
                try
                {
                    MatchSingleTemplateEntry(
                        sourceMat, sourceGray, sourceContour, entry, mode, threshold, monsterName, bag);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[怪物偵測] 模板匹配錯誤: {ex.Message}");
                }
            }

            fineWatch.Stop();

            var stats = new MonsterTemplateMatchStats(
                entries.Count,
                entries.Count,
                coarseSelection.Indices.Length,
                coarseSelection.DownscaleMs,
                coarseSelection.CoarseScoreMs,
                fineWatch.Elapsed.TotalMilliseconds,
                coarseSelection.UsedFullFallback);

            return (bag, stats);
        }

        private readonly record struct CoarseMatchScore(int Index, double Score);

        private readonly record struct CoarseSelectionResult(
            int[] Indices,
            bool UsedFullFallback,
            double DownscaleMs,
            double CoarseScoreMs);

        private static CoarseSelectionResult SelectCoarseMatchIndices(
            Mat sourceMat,
            Mat? sourceGray,
            Mat? sourceContour,
            IReadOnlyList<MonsterTemplateEntry> entries,
            MonsterDetectionMode mode)
        {
            if (entries.Count <= CoarseTopK)
                return new CoarseSelectionResult(
                    Enumerable.Range(0, entries.Count).ToArray(), false, 0, 0);

            var downscaleWatch = Stopwatch.StartNew();
            using var sourceCoarse = DownscaleMat(sourceMat, MonsterTemplateEntry.CoarseScale);
            using var sourceGrayCoarse = sourceGray != null
                ? DownscaleMat(sourceGray, MonsterTemplateEntry.CoarseScale)
                : null;
            using var sourceContourCoarse = sourceContour != null
                ? DownscaleMat(sourceContour, MonsterTemplateEntry.CoarseScale)
                : null;
            downscaleWatch.Stop();

            var scoreWatch = Stopwatch.StartNew();
            var coarseScores = ScoreAllCoarseMatches(
                sourceCoarse, sourceGrayCoarse, sourceContourCoarse, entries, mode);
            scoreWatch.Stop();

            var ranked = coarseScores.OrderByDescending(s => s.Score).ToList();
            if (ranked.Count == 0 || ranked[0].Score < CoarseMinBestScore)
            {
                return new CoarseSelectionResult(
                    Enumerable.Range(0, entries.Count).ToArray(),
                    true,
                    downscaleWatch.Elapsed.TotalMilliseconds,
                    scoreWatch.Elapsed.TotalMilliseconds);
            }

            var topIndices = ranked
                .Take(CoarseTopK)
                .Select(s => s.Index)
                .OrderBy(i => i)
                .ToArray();

            return new CoarseSelectionResult(
                topIndices,
                false,
                downscaleWatch.Elapsed.TotalMilliseconds,
                scoreWatch.Elapsed.TotalMilliseconds);
        }

        private static List<CoarseMatchScore> ScoreAllCoarseMatches(
            Mat sourceCoarse,
            Mat? sourceGrayCoarse,
            Mat? sourceContourCoarse,
            IReadOnlyList<MonsterTemplateEntry> entries,
            MonsterDetectionMode mode)
        {
            var scores = new List<CoarseMatchScore>(entries.Count);

            // 粗篩同樣不可平行讀寫同一張 downscale Mat
            for (int index = 0; index < entries.Count; index++)
            {
                var score = ScoreCoarseMatch(
                    sourceCoarse, sourceGrayCoarse, sourceContourCoarse, entries[index], mode);
                scores.Add(new CoarseMatchScore(index, score));
            }

            return scores;
        }

        private static void MatchSingleTemplateEntry(
            Mat sourceMat,
            Mat? sourceGray,
            Mat? sourceContour,
            MonsterTemplateEntry entry,
            MonsterDetectionMode mode,
            double threshold,
            string monsterName,
            List<DetectionResult> results)
        {
            var templateMat = entry.Template;
            if (templateMat.Empty()) return;

            using var result = new Mat();
            var localResults = new List<DetectionResult>();

            if (mode == MonsterDetectionMode.ContourOnly)
            {
                // KenYu: matchTemplate(roi_black_blur, pattern_black_blur, TM_SQDIFF_NORMED)
                // threshold 此處語意＝最大允許 diff（res <= threshold）
                if (sourceContour == null || !entry.HasUsableContour)
                    return;

                Cv2.MatchTemplate(sourceContour, entry.ContourMask, result, TemplateMatchModes.SqDiffNormed);
                CollectSqDiffByMaxDiff(result, templateMat, threshold, monsterName, localResults);
            }
            else if (mode == MonsterDetectionMode.Grayscale)
            {
                Cv2.MatchTemplate(sourceGray!, entry.GrayTemplate, result, TemplateMatchModes.CCoeffNormed);
                CollectCCoeffMatches(result, templateMat, threshold, monsterName, localResults);
            }
            else if (!entry.Mask.Empty())
            {
                Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed, entry.Mask);
                CollectSqDiffByMinConfidence(result, templateMat, threshold, monsterName, localResults);
            }
            else
            {
                Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed);
                CollectSqDiffByMinConfidence(result, templateMat, threshold, monsterName, localResults);
            }

            foreach (var detection in localResults)
                results.Add(detection);
        }

        private static double ScoreCoarseMatch(
            Mat sourceCoarse,
            Mat? sourceGrayCoarse,
            Mat? sourceContourCoarse,
            MonsterTemplateEntry entry,
            MonsterDetectionMode mode)
        {
            using var result = new Mat();

            if (mode == MonsterDetectionMode.ContourOnly)
            {
                if (sourceContourCoarse == null || !entry.HasUsableContour)
                    return 0;

                var contourTemplate = entry.SupportsCoarse
                    ? entry.CoarseContourMask
                    : entry.ContourMask;

                Cv2.MatchTemplate(
                    sourceContourCoarse, contourTemplate, result, TemplateMatchModes.SqDiffNormed);
                Cv2.MinMaxLoc(result, out double minVal, out _, out _, out _);
                return 1.0 - minVal;
            }

            if (mode == MonsterDetectionMode.Grayscale)
            {
                if (sourceGrayCoarse == null) return 0;

                var grayTemplate = entry.SupportsCoarse
                    ? entry.CoarseGrayTemplate
                    : entry.GrayTemplate;

                Cv2.MatchTemplate(sourceGrayCoarse, grayTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out _);
                return maxVal;
            }

            var colorTemplate = entry.SupportsCoarse
                ? entry.CoarseTemplate
                : entry.Template;
            var mask = entry.SupportsCoarse
                ? entry.CoarseMask
                : entry.Mask;

            if (!mask.Empty())
                Cv2.MatchTemplate(sourceCoarse, colorTemplate, result, TemplateMatchModes.SqDiffNormed, mask);
            else
                Cv2.MatchTemplate(sourceCoarse, colorTemplate, result, TemplateMatchModes.SqDiffNormed);

            Cv2.MinMaxLoc(result, out double colorMinVal, out _, out _, out _);
            return 1.0 - colorMinVal;
        }

        private static Mat DownscaleMat(Mat source, double scale)
        {
            var scaled = new Mat();
            Cv2.Resize(
                source,
                scaled,
                new OpenCvSharp.Size(),
                scale,
                scale,
                InterpolationFlags.Area);
            return scaled;
        }

        /// <summary>
        /// KenYu ContourOnly：精確黑色 (0,0,0) → 單通道遮罩 → GaussianBlur。
        /// 見 https://github.com/KenYu910645/MapleStoryAutoLevelUp
        /// </summary>
        private static Mat BuildBlackContourMask(Mat bgr, int blurKernel)
        {
            using var black = new Mat();
            Cv2.InRange(bgr, new Scalar(0, 0, 0), new Scalar(0, 0, 0), black);

            int k = Math.Max(1, blurKernel);
            if ((k & 1) == 0) k++;

            var blurred = new Mat();
            Cv2.GaussianBlur(black, blurred, new OpenCvSharp.Size(k, k), 0);
            return blurred;
        }

        private static int ResolveContourBlur()
        {
            try
            {
                int blur = AppConfig.Instance?.Vision?.ContourBlur ?? DefaultContourBlur;
                if ((blur & 1) == 0) blur++;
                return Math.Clamp(blur, 1, 31);
            }
            catch
            {
                return DefaultContourBlur;
            }
        }

        /// <summary>
        /// 在 ROI 內找出多條紅色小血條（怪血條）。座標相對 <paramref name="sourceBgr"/>。
        /// </summary>
        public static List<Rectangle> FindEnemyHpBars(Mat sourceBgr, AppConfig config)
        {
            var results = new List<Rectangle>();
            if (sourceBgr == null || sourceBgr.Empty() || config?.Vision == null)
                return results;

            using var hsv = new Mat();
            Cv2.CvtColor(sourceBgr, hsv, ColorConversionCodes.BGR2HSV);

            var lower = config.Vision.LowerRedHsv;
            var upper = config.Vision.UpperRedHsv;
            using var mask1 = new Mat();
            Cv2.InRange(
                hsv,
                new Scalar(lower[0], lower[1], lower[2]),
                new Scalar(upper[0], upper[1], upper[2]),
                mask1);

            using var mask2 = new Mat();
            Cv2.InRange(hsv, new Scalar(160, lower[1], lower[2]), new Scalar(180, 255, 255), mask2);

            using var redMask = new Mat();
            Cv2.BitwiseOr(mask1, mask2, redMask);

            Mat? hierarchy = null;
            Mat[]? contours = null;
            try
            {
                hierarchy = new Mat();
                Cv2.FindContours(
                    redMask, out contours, hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                if (contours == null)
                    return results;

                foreach (var contour in contours)
                {
                    if (contour?.Empty() != false) continue;

                    try
                    {
                        var br = Cv2.BoundingRect(contour);
                        var rect = new Rectangle(br.X, br.Y, br.Width, br.Height);
                        int area = rect.Width * rect.Height;
                        double aspect = rect.Height > 0 ? (double)rect.Width / rect.Height : 0;

                        if (rect.Width < config.Vision.MinBarWidth ||
                            rect.Width > config.Vision.MaxBarWidth ||
                            rect.Height < config.Vision.MinBarHeight ||
                            rect.Height > config.Vision.MaxBarHeight ||
                            area < config.Vision.MinBarArea)
                            continue;

                        if (aspect < config.Vision.MinAspectRatio ||
                            aspect > config.Vision.MaxAspectRatio)
                            continue;

                        results.Add(rect);
                    }
                    finally
                    {
                        contour?.Dispose();
                    }
                }
            }
            finally
            {
                hierarchy?.Dispose();
            }

            return results;
        }

        /// <summary>
        /// 僅在明確啟用時使用。怪血條多半攻擊後才出現，不可當發現過濾。
        /// 無血條清單時原樣返回。
        /// </summary>
        public static List<DetectionResult> FilterMonstersByEnemyHpBars(
            IReadOnlyList<DetectionResult> detections,
            IReadOnlyList<Rectangle> enemyHpBars,
            int maxGapPx,
            Rectangle? playerBloodBar)
        {
            if (detections == null || detections.Count == 0)
                return new List<DetectionResult>();

            if (enemyHpBars == null || enemyHpBars.Count == 0)
                return detections.ToList();

            int gap = Math.Max(8, maxGapPx);
            var usableBars = enemyHpBars
                .Where(bar => !IsSameBar(bar, playerBloodBar))
                .ToList();

            if (usableBars.Count == 0)
                return detections.ToList();

            return detections
                .Where(det => HasEnemyHpBarAbove(det, usableBars, gap))
                .ToList();
        }

        private static bool IsSameBar(Rectangle bar, Rectangle? playerBloodBar)
        {
            if (!playerBloodBar.HasValue) return false;
            var p = playerBloodBar.Value;
            var inter = Rectangle.Intersect(bar, p);
            if (inter.IsEmpty) return false;
            double iou = (double)(inter.Width * inter.Height) /
                         Math.Max(1, bar.Width * bar.Height + p.Width * p.Height - inter.Width * inter.Height);
            return iou >= 0.35;
        }

        private static bool HasEnemyHpBarAbove(
            DetectionResult detection,
            IReadOnlyList<Rectangle> bars,
            int maxGapPx)
        {
            var box = detection.BoundingBox;
            int centerX = box.X + box.Width / 2;
            int top = box.Y;
            int xTolerance = Math.Max(box.Width / 2, 12);

            foreach (var bar in bars)
            {
                int barCenterX = bar.X + bar.Width / 2;
                if (Math.Abs(barCenterX - centerX) > xTolerance + bar.Width)
                    continue;

                // 血條應在怪物頂部附近上方
                if (bar.Bottom > top + 6)
                    continue;

                if (top - bar.Bottom > maxGapPx)
                    continue;

                return true;
            }

            return false;
        }

        private static List<DetectionResult> MatchLegacyTemplates(
            Mat sourceMat,
            List<Mat> templateMats,
            MonsterDetectionMode mode,
            double threshold,
            string monsterName,
            List<Mat>? templateMasks)
        {
            var allResults = new List<DetectionResult>();

            for (int i = 0; i < templateMats.Count; i++)
            {
                var templateMat = templateMats[i];
                if (templateMat?.Empty() != false) continue;

                Mat? mask = null;
                bool disposeMask = false;

                if (templateMasks != null && i < templateMasks.Count && templateMasks[i]?.Empty() == false)
                    mask = templateMasks[i];
                else
                {
                    mask = CreateGreenMask(templateMat);
                    disposeMask = true;
                }

                try
                {
                    using var result = new Mat();

                    if (mode == MonsterDetectionMode.ContourOnly)
                    {
                        using var sourceContour = BuildBlackContourMask(sourceMat, ResolveContourBlur());
                        using var templateContour = BuildBlackContourMask(templateMat, ResolveContourBlur());
                        if (Cv2.CountNonZero(templateContour) < MonsterTemplateEntry.MinContourPixels)
                            continue;

                        Cv2.MatchTemplate(
                            sourceContour, templateContour, result, TemplateMatchModes.SqDiffNormed);
                        CollectSqDiffByMaxDiff(result, templateMat, threshold, monsterName, allResults);
                    }
                    else if (mode == MonsterDetectionMode.Grayscale)
                    {
                        using var sourceGray = ConvertToGrayscale(sourceMat);
                        using var templateGray = ConvertToGrayscale(templateMat);
                        Cv2.MatchTemplate(sourceGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                        CollectCCoeffMatches(result, templateMat, threshold, monsterName, allResults);
                    }
                    else if (mask != null && !mask.Empty())
                    {
                        Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed, mask);
                        CollectSqDiffByMinConfidence(result, templateMat, threshold, monsterName, allResults);
                    }
                    else
                    {
                        Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed);
                        CollectSqDiffByMinConfidence(result, templateMat, threshold, monsterName, allResults);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[怪物偵測] 模板匹配錯誤: {ex.Message}");
                }
                finally
                {
                    if (disposeMask) mask?.Dispose();
                }
            }

            return allResults;
        }

        /// <summary>KenYu ContourOnly：接受 <c>minVal &lt;= maxAllowedDiff</c>。</summary>
        private static void CollectSqDiffByMaxDiff(
            Mat result,
            Mat templateMat,
            double maxAllowedDiff,
            string monsterName,
            List<DetectionResult> allResults)
        {
            if (result.Empty()) return;

            int count = 0;
            const int maxResults = 5;

            while (count < maxResults)
            {
                Cv2.MinMaxLoc(result, out double minVal, out _, out OpenCvSharp.Point minLoc, out _);

                if (minVal > maxAllowedDiff)
                    break;

                allResults.Add(new DetectionResult(
                    monsterName,
                    new System.Drawing.Point(minLoc.X, minLoc.Y),
                    new System.Drawing.Size(templateMat.Width, templateMat.Height),
                    (float)(1.0 - minVal),
                    new Rectangle(minLoc.X, minLoc.Y, templateMat.Width, templateMat.Height)));

                count++;
                SuppressMatchPeak(result, minLoc, templateMat, fillValue: 1.0);
            }
        }

        /// <summary>Color／Basic：threshold 為最低信心分數（1 - SqDiff）。</summary>
        private static void CollectSqDiffByMinConfidence(
            Mat result,
            Mat templateMat,
            double minConfidence,
            string monsterName,
            List<DetectionResult> allResults)
        {
            if (result.Empty()) return;

            int count = 0;
            const int maxResults = 5;

            while (count < maxResults)
            {
                Cv2.MinMaxLoc(result, out double minVal, out _, out OpenCvSharp.Point minLoc, out _);

                if (minVal > (1.0 - minConfidence))
                    break;

                allResults.Add(new DetectionResult(
                    monsterName,
                    new System.Drawing.Point(minLoc.X, minLoc.Y),
                    new System.Drawing.Size(templateMat.Width, templateMat.Height),
                    (float)(1.0 - minVal),
                    new Rectangle(minLoc.X, minLoc.Y, templateMat.Width, templateMat.Height)));

                count++;
                SuppressMatchPeak(result, minLoc, templateMat, fillValue: 1.0);
            }
        }

        private static void CollectCCoeffMatches(
            Mat result,
            Mat templateMat,
            double minScore,
            string monsterName,
            List<DetectionResult> allResults)
        {
            if (result.Empty()) return;

            int count = 0;
            const int maxResults = 5;

            while (count < maxResults)
            {
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                if (maxVal < minScore)
                    break;

                allResults.Add(new DetectionResult(
                    monsterName,
                    new System.Drawing.Point(maxLoc.X, maxLoc.Y),
                    new System.Drawing.Size(templateMat.Width, templateMat.Height),
                    (float)maxVal,
                    new Rectangle(maxLoc.X, maxLoc.Y, templateMat.Width, templateMat.Height)));

                count++;
                SuppressMatchPeak(result, maxLoc, templateMat, fillValue: 0.0);
            }
        }

        private static void SuppressMatchPeak(
            Mat result,
            OpenCvSharp.Point peak,
            Mat templateMat,
            double fillValue)
        {
            int floodW = Math.Max(1, templateMat.Width / 2);
            int floodH = Math.Max(1, templateMat.Height / 2);
            var floodRect = new OpenCvSharp.Rect(
                Math.Max(0, peak.X - floodW / 2),
                Math.Max(0, peak.Y - floodH / 2),
                floodW,
                floodH);

            Cv2.Rectangle(result, floodRect, OpenCvSharp.Scalar.All(fillValue), -1);
        }

        private static MonsterTemplateEntry CreateTemplateEntry(Mat bgrTemplate)
        {
            var mask = CreateGreenMask(bgrTemplate);
            var gray = new Mat();
            Cv2.CvtColor(bgrTemplate, gray, ColorConversionCodes.BGR2GRAY);
            var contourMask = BuildBlackContourMask(bgrTemplate, ResolveContourBlur());

            var coarseTemplate = DownscaleMat(bgrTemplate, MonsterTemplateEntry.CoarseScale);
            var coarseMask = DownscaleMat(mask, MonsterTemplateEntry.CoarseScale);
            var coarseGray = DownscaleMat(gray, MonsterTemplateEntry.CoarseScale);
            var coarseContour = DownscaleMat(contourMask, MonsterTemplateEntry.CoarseScale);

            return new MonsterTemplateEntry(
                bgrTemplate, mask, gray, contourMask,
                coarseTemplate, coarseMask, coarseGray, coarseContour);
        }

        /// <summary>非同步載入怪物模板 bundle（含預計算 mask 與灰階）。</summary>
        public async Task<MonsterTemplateBundle?> LoadMonsterTemplateBundleAsync(string monsterName, string monstersDirectory)
        {
            try
            {
                if (_monsterBundleCache.TryGetValue(monsterName, out var cached))
                    return cached;

                string monsterFolderPath = Path.Combine(monstersDirectory, monsterName);
                if (!Directory.Exists(monsterFolderPath))
                    return null;

                var templateFiles = await Task.Run(() => Directory.GetFiles(monsterFolderPath, "*.png"));
                var entries = new List<MonsterTemplateEntry>();

                foreach (var file in templateFiles)
                {
                    try
                    {
                        using var tempBitmap = new System.Drawing.Bitmap(file);

                        if (tempBitmap.Width < 5 || tempBitmap.Height < 5)
                            continue;

                        using var originalMat = BitmapConverter.ToMat(tempBitmap);
                        Mat matForMatching = new Mat();

                        if (originalMat.Channels() == 4)
                            Cv2.CvtColor(originalMat, matForMatching, ColorConversionCodes.BGRA2BGR);
                        else if (originalMat.Channels() == 1)
                            Cv2.CvtColor(originalMat, matForMatching, ColorConversionCodes.GRAY2BGR);
                        else
                            matForMatching = originalMat.Clone();

                        entries.Add(CreateTemplateEntry(matForMatching));

                        Mat flippedMat = matForMatching.Clone();
                        Cv2.Flip(flippedMat, flippedMat, FlipMode.Y);
                        entries.Add(CreateTemplateEntry(flippedMat));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[模板] 載入失敗 {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                if (entries.Count == 0)
                    return null;

                var bundle = new MonsterTemplateBundle(monsterName, entries);
                _monsterBundleCache[monsterName] = bundle;
                Logger.Info($"[模板] 已載入 {monsterName}: {bundle.TemplateCount} 個模板（含預計算 mask、灰階與粗篩縮圖）");
                return bundle;
            }
            catch (Exception ex)
            {
                Logger.Error($"[模板] LoadMonsterTemplateBundleAsync 錯誤: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
