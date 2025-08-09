using OpenCvSharp;
using System.Collections.Generic;
using System.Linq;

using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 代表模板匹配操作的結果。
    /// </summary>
    public class TemplateMatch
    {
        public SdPoint Location { get; }
        public double Confidence { get; }
        public Rect BoundingBox { get; }

        public TemplateMatch(SdPoint location, double confidence, SdSize templateSize)
        {
            Location = location;
            Confidence = confidence;
            BoundingBox = new Rect(location.X, location.Y, templateSize.Width, templateSize.Height);
        }
    }

    /// <summary>
    /// 提供影像處理的工具方法，特別是模板匹配。
    /// </summary>
    public static class ImageUtils
    {
        /// <summary>
        /// 在來源影像中尋找單一最佳的模板匹配。
        /// </summary>
        public static TemplateMatch? FindSingleTemplateMatch(Mat source, Mat template, double threshold)
        {
            if (source.Empty() || template.Empty() || source.Width < template.Width || source.Height < template.Height)
            {
                return null;
            }

            using (Mat result = new Mat())
            {
                Cv2.MatchTemplate(source, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                if (maxVal >= threshold)
                {
                    return new TemplateMatch(new SdPoint(maxLoc.X, maxLoc.Y), maxVal, new SdSize(template.Width, template.Height));
                }
            }
            return null;
        }

        /// <summary>
        /// 在來源影像中尋找所有超過給定閾值的模板匹配。
        /// </summary>
        public static List<TemplateMatch> FindAllTemplateMatches(Mat source, Mat template, double threshold)
        {
            var matches = new List<TemplateMatch>();
            if (source.Empty() || template.Empty() || source.Width < template.Width || source.Height < template.Height)
            {
                return matches;
            }

            using (Mat result = new Mat())
            {
                Cv2.MatchTemplate(source, template, result, TemplateMatchModes.CCoeffNormed);

                // 找出所有高於閾值的匹配點
                for (int y = 0; y < result.Rows; y++)
                {
                    for (int x = 0; x < result.Cols; x++)
                    {
                        if (result.At<float>(y, x) >= threshold)
                        {
                            matches.Add(new TemplateMatch(new SdPoint(x, y), result.At<float>(y, x), new SdSize(template.Width, template.Height)));
                        }
                    }
                }
            }
            // 使用非極大值抑制來過濾掉重疊的結果，0.3 是一個經驗值，可以調整
            return NonMaxSuppression(matches, 0.3);
        }

        /// <summary>
        /// 非極大值抑制，用於從大量重疊的候選框中選出最佳的一個。
        /// </summary>
        private static List<TemplateMatch> NonMaxSuppression(List<TemplateMatch> matches, double overlapThresh)
        {
            if (!matches.Any())
                return new List<TemplateMatch>();

            var pick = new List<TemplateMatch>();
            var orderedMatches = matches.OrderByDescending(m => m.Confidence).ToList();

            while (orderedMatches.Any())
            {
                var last = orderedMatches.First();
                pick.Add(last);

                var suppress = new List<TemplateMatch> { last };
                for (int i = 1; i < orderedMatches.Count; i++)
                {
                    var match = orderedMatches[i];
                    double overlap = CalculateOverlap(last.BoundingBox, match.BoundingBox);
                    if (overlap > overlapThresh)
                    {
                        suppress.Add(match);
                    }
                }
                orderedMatches.RemoveAll(m => suppress.Contains(m));
            }
            return pick;
        }

        /// <summary>
        /// 計算兩個矩形的重疊率 (Intersection over Union - IoU)。
        /// </summary>
        private static double CalculateOverlap(Rect boxA, Rect boxB)
        {
            int xA = System.Math.Max(boxA.Left, boxB.Left);
            int yA = System.Math.Max(boxA.Top, boxB.Top);
            int xB = System.Math.Min(boxA.Right, boxB.Right);
            int yB = System.Math.Min(boxA.Bottom, boxB.Bottom);

            int interArea = System.Math.Max(0, xB - xA) * System.Math.Max(0, yB - yA);
            if (interArea == 0) return 0;

            double boxAArea = boxA.Width * boxA.Height;
            double boxBArea = boxB.Width * boxB.Height;

            return interArea / (boxAArea + boxBArea - interArea);
        }
    }
}
