namespace ArtaleAI.Utils
{
    public static class MathUtils
    {
        /// <summary>
        /// 計算兩個矩形的 IoU（交集與聯集的比值）
        /// </summary>
        public static double CalculateIoU(Rectangle rectA, Rectangle rectB)
        {
            var intersection = Rectangle.Intersect(rectA, rectB);
            if (intersection.IsEmpty) return 0.0;

            double intersectionArea = intersection.Width * intersection.Height;
            double unionArea = rectA.Width * rectA.Height +
                              rectB.Width * rectB.Height - intersectionArea;

            return intersectionArea / unionArea;
        }

        /// <summary>
        /// 統一的非極大值抑制方法 - 泛型版本
        /// </summary>
        public static List<T> ApplyNonMaxSuppression<T>(
            List<T> items,
            Func<T, double> confidenceSelector,
            Func<T, Rectangle> boundingBoxSelector,
            double iouThreshold)
        {
            if (!items.Any()) return new List<T>();

            // 按信心度排序
            var sortedItems = items.OrderByDescending(confidenceSelector).ToList();
            var result = new List<T>();

            while (sortedItems.Any())
            {
                var best = sortedItems.First();
                result.Add(best);
                sortedItems.RemoveAt(0);

                var bestBoundingBox = boundingBoxSelector(best);
                sortedItems.RemoveAll(item =>
                    CalculateIoU(bestBoundingBox, boundingBoxSelector(item)) > iouThreshold);
            }

            return result;
        }
    }
}
