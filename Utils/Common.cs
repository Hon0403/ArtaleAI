
namespace ArtaleAI.Utils
{
    /// <summary>
    /// 統一工具類：整合路徑、檔案、數學工具
    /// 注意：影像處理請使用 ImageUtils.cs
    /// </summary>
    public static class common
    {
        // ------------------------------
        // 路徑工具（原 PathUtils 的職責）
        // ------------------------------

        /// <summary>
        /// 取得 Config 目錄的完整路徑
        /// </summary>
        public static string GetConfigDirectory() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

        /// <summary>
        /// 取得 config.yaml 的完整路徑
        /// </summary>
        public static string GetConfigFilePath() =>
            Path.Combine(GetConfigDirectory(), "config.yaml");

        /// <summary>
        /// 取得地圖資料目錄的完整路徑
        /// </summary>
        public static string GetMapDataDirectory() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MapData");

        /// <summary>
        /// 取得 Templates 根目錄
        /// </summary>
        public static string GetTemplatesDirectory() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");

        /// <summary>
        /// 取得怪物模板的目錄
        /// </summary>
        public static string GetMonstersDirectory() =>
            Path.Combine(GetTemplatesDirectory(), "Monsters");

        // ------------------------------
        // 檔案工具（原 FileUtils 的職責）
        // ------------------------------

        public static void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);
        }

        public static string ReadAllText(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"找不到檔案: {filePath}");
            return File.ReadAllText(filePath);
        }

        public static void WriteAllText(string filePath, string content)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) EnsureDirectoryExists(dir);
            File.WriteAllText(filePath, content);
        }

        public static string[] GetFilesByExtension(string directoryPath, string extensionWithoutDot)
        {
            if (!Directory.Exists(directoryPath)) return Array.Empty<string>();
            var pattern = "*." + extensionWithoutDot.TrimStart('.');
            return Directory.GetFiles(directoryPath, pattern);
        }

        // ------------------------------
        // 數學工具（原 MathUtils 的職責）
        // ------------------------------

        /// <summary>
        /// 計算兩個 System.Drawing.Rectangle 的 IoU（交並比）
        /// </summary>
        public static double CalculateIoU(Rectangle rectA, Rectangle rectB)
        {
            var inter = Rectangle.Intersect(rectA, rectB);
            if (inter.IsEmpty) return 0.0;

            double interArea = inter.Width * inter.Height;
            double unionArea = rectA.Width * rectA.Height +
                               rectB.Width * rectB.Height - interArea;

            if (unionArea <= 0) return 0.0;
            return interArea / unionArea;
        }

        /// <summary>
        /// 泛型 NMS，透過信心分數與邊界框選擇最佳集合
        /// </summary>
        public static List<T> ApplyNonMaxSuppression<T>(
            List<T> items,
            Func<T, double> scoreSelector,
            Func<T, Rectangle> bboxSelector,
            double iouThreshold)
        {
            if (items == null || items.Count == 0) return new List<T>();

            var sorted = items.OrderByDescending(scoreSelector).ToList();
            var kept = new List<T>();

            while (sorted.Count > 0)
            {
                var best = sorted[0];
                kept.Add(best);
                sorted.RemoveAt(0);

                var bestRect = bboxSelector(best);

                sorted = sorted
                    .Where(item => CalculateIoU(bestRect, bboxSelector(item)) <= iouThreshold)
                    .ToList();
            }

            return kept;
        }
    }
}
