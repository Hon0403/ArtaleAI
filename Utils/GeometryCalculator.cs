using ArtaleAI.Models;
using System.Drawing;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 幾何計算專用類別
    /// </summary>
    public static class GeometryCalculator
    {
        /// <summary>
        /// 計算兩個矩形的 IoU (Intersection over Union)
        /// </summary>
        public static double CalculateIoU(Rectangle rectA, Rectangle rectB)
        {
            var intersection = Rectangle.Intersect(rectA, rectB);
            if (intersection.IsEmpty) return 0.0;

            double intersectionArea = intersection.Width * intersection.Height;
            double unionArea = rectA.Width * rectA.Height + rectB.Width * rectB.Height - intersectionArea;

            return unionArea == 0 ? 0.0 : intersectionArea / unionArea;
        }

        /// <summary>
        /// NMS (Non-Maximum Suppression) 演算法
        /// </summary>
        public static List<T> ApplyNMS<T>(List<T> items, double iouThreshold = 0.25, bool higherIsBetter = true) where T : class
        {
            if (items.Count <= 1) return items;

            var itemArray = items.ToArray();
            Array.Sort(itemArray, (a, b) => higherIsBetter
                ? GetConfidence(b).CompareTo(GetConfidence(a))
                : GetConfidence(a).CompareTo(GetConfidence(b)));

            var suppressed = new bool[itemArray.Length];
            var nmsResults = new List<T>();

            for (int i = 0; i < itemArray.Length; i++)
            {
                if (suppressed[i]) continue;

                var current = itemArray[i];
                nmsResults.Add(current);
                var currentRect = GetBoundingBox(current);

                for (int j = i + 1; j < itemArray.Length; j++)
                {
                    if (!suppressed[j])
                    {
                        var candidateRect = GetBoundingBox(itemArray[j]);
                        if (CalculateIoU(currentRect, candidateRect) > iouThreshold)
                        {
                            suppressed[j] = true;
                        }
                    }
                }
            }

            return nmsResults;
        }

        private static Rectangle GetBoundingBox<T>(T item)
        {
            return item switch
            {
                MonsterRenderInfo monster => new Rectangle(monster.Location.X, monster.Location.Y, monster.Size.Width, monster.Size.Height),
                MatchResult match => new Rectangle(match.Position.X, match.Position.Y, match.Size.Width, match.Size.Height),
                _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for bounding box extraction")
            };
        }

        private static double GetConfidence<T>(T item)
        {
            return item switch
            {
                MonsterRenderInfo monster => monster.Confidence,
                MatchResult match => match.Confidence,
                _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for confidence extraction")
            };
        }
    }
}
