using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Shared;
using ArtaleAI.Infrastructure.Capture;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Concurrent;
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

            var coarseSelection = SelectCoarseMatchIndices(sourceMat, sourceGray, entries, mode);

            var bag = new ConcurrentBag<DetectionResult>();
            var fineWatch = Stopwatch.StartNew();

            Parallel.ForEach(coarseSelection.Indices, index =>
            {
                var entry = entries[index];
                try
                {
                    MatchSingleTemplateEntry(
                        sourceMat, sourceGray, entry, mode, threshold, monsterName, bag);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[怪物偵測] 模板匹配錯誤: {ex.Message}");
                }
            });

            fineWatch.Stop();

            var stats = new MonsterTemplateMatchStats(
                entries.Count,
                entries.Count,
                coarseSelection.Indices.Length,
                coarseSelection.DownscaleMs,
                coarseSelection.CoarseScoreMs,
                fineWatch.Elapsed.TotalMilliseconds,
                coarseSelection.UsedFullFallback);

            return (bag.ToList(), stats);
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
            downscaleWatch.Stop();

            var scoreWatch = Stopwatch.StartNew();
            var coarseScores = ScoreAllCoarseMatches(sourceCoarse, sourceGrayCoarse, entries, mode);
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
            IReadOnlyList<MonsterTemplateEntry> entries,
            MonsterDetectionMode mode)
        {
            var scores = new ConcurrentBag<CoarseMatchScore>();

            Parallel.ForEach(Enumerable.Range(0, entries.Count), index =>
            {
                var score = ScoreCoarseMatch(sourceCoarse, sourceGrayCoarse, entries[index], mode);
                scores.Add(new CoarseMatchScore(index, score));
            });

            return scores.ToList();
        }

        private static void MatchSingleTemplateEntry(
            Mat sourceMat,
            Mat? sourceGray,
            MonsterTemplateEntry entry,
            MonsterDetectionMode mode,
            double threshold,
            string monsterName,
            ConcurrentBag<DetectionResult> results)
        {
            var templateMat = entry.Template;
            if (templateMat.Empty()) return;

            using var result = new Mat();

            if (mode == MonsterDetectionMode.Grayscale)
            {
                Cv2.MatchTemplate(sourceGray!, entry.GrayTemplate, result, TemplateMatchModes.CCoeffNormed);
            }
            else if (!entry.Mask.Empty())
            {
                Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed, entry.Mask);
            }
            else
            {
                Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed);
            }

            var localResults = new List<DetectionResult>();
            CollectMatchResults(result, templateMat, threshold, monsterName, localResults);
            foreach (var detection in localResults)
                results.Add(detection);
        }

        private static double ScoreCoarseMatch(
            Mat sourceCoarse,
            Mat? sourceGrayCoarse,
            MonsterTemplateEntry entry,
            MonsterDetectionMode mode)
        {
            using var result = new Mat();

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

            Cv2.MinMaxLoc(result, out double minVal, out _, out _, out _);
            return 1.0 - minVal;
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

                    if (mode == MonsterDetectionMode.Grayscale)
                    {
                        using var sourceGray = ConvertToGrayscale(sourceMat);
                        using var templateGray = ConvertToGrayscale(templateMat);
                        Cv2.MatchTemplate(sourceGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                    }
                    else if (mask != null && !mask.Empty())
                    {
                        Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed, mask);
                    }
                    else
                    {
                        Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed);
                    }

                    CollectMatchResults(result, templateMat, threshold, monsterName, allResults);
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

        private static void CollectMatchResults(
            Mat result,
            Mat templateMat,
            double threshold,
            string monsterName,
            List<DetectionResult> allResults)
        {
            if (result.Empty()) return;

            int count = 0;
            const int maxResults = 5;

            while (count < maxResults)
            {
                Cv2.MinMaxLoc(result, out double minVal, out _, out OpenCvSharp.Point minLoc, out _);

                if (minVal > (1.0 - threshold))
                    break;

                float matchScore = (float)(1.0 - minVal);

                allResults.Add(new DetectionResult(
                    monsterName,
                    new System.Drawing.Point(minLoc.X, minLoc.Y),
                    new System.Drawing.Size(templateMat.Width, templateMat.Height),
                    matchScore,
                    new Rectangle(minLoc.X, minLoc.Y, templateMat.Width, templateMat.Height)
                ));

                count++;

                int floodW = templateMat.Width / 2;
                int floodH = templateMat.Height / 2;
                var floodRect = new OpenCvSharp.Rect(
                    Math.Max(0, minLoc.X - floodW / 2),
                    Math.Max(0, minLoc.Y - floodH / 2),
                    floodW,
                    floodH);

                Cv2.Rectangle(result, floodRect, OpenCvSharp.Scalar.All(1.0), -1);
            }
        }

        private static MonsterTemplateEntry CreateTemplateEntry(Mat bgrTemplate)
        {
            var mask = CreateGreenMask(bgrTemplate);
            var gray = new Mat();
            Cv2.CvtColor(bgrTemplate, gray, ColorConversionCodes.BGR2GRAY);

            var coarseTemplate = DownscaleMat(bgrTemplate, MonsterTemplateEntry.CoarseScale);
            var coarseMask = DownscaleMat(mask, MonsterTemplateEntry.CoarseScale);
            var coarseGray = DownscaleMat(gray, MonsterTemplateEntry.CoarseScale);

            return new MonsterTemplateEntry(
                bgrTemplate, mask, gray,
                coarseTemplate, coarseMask, coarseGray);
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
