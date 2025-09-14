using ArtaleAI.Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Linq;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 統一工具類：整合圖像處理、路徑、檔案、數學工具
    /// </summary>
    public static class UtilityHelper
    {
        private static readonly object _conversionLock = new object();

        #region 圖像處理工具

        /// <summary>
        /// 創建HSV版本的Mat
        /// </summary>
        public static Mat ConvertToHSV(Mat bgrMat) // 參數改名
        {
            if (bgrMat?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(bgrMat));
            var hsvMat = new Mat();
            Cv2.CvtColor(bgrMat, hsvMat, ColorConversionCodes.BGR2HSV); // RGB2HSV → BGR2HSV
            return hsvMat;
        }

        /// <summary>
        /// 創建灰階版本的Mat
        /// </summary>
        public static Mat ConvertToGrayscale(Mat bgrMat) // 參數改名
        {
            if (bgrMat?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(bgrMat));
            var grayMat = new Mat();
            Cv2.CvtColor(bgrMat, grayMat, ColorConversionCodes.BGR2GRAY);
            return grayMat;
        }

        /// <summary>
        /// 創建三通道模板遮罩
        /// </summary>
        public static Mat CreateThreeChannelTemplateMask(Mat templateImg)
        {
            if (templateImg?.Empty() == true)
                throw new ArgumentException("模板圖像為空", nameof(templateImg));

            var mask = new Mat();

            return mask;
        }

        /// <summary>
        /// 創建黑色像素遮罩（三通道版本）
        /// </summary>
        public static Mat CreateBlackPixelMask(Mat img)
        {
            if (img?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(img));

            var mask = new Mat();
            if (img.Channels() == 3)
            {
                Cv2.InRange(img, new Scalar(0, 0, 0), new Scalar(30, 30, 30), mask);
            }
            else
            {
                Cv2.InRange(img, new Scalar(0), new Scalar(30), mask);
            }

            return mask;
        }

        public static Mat EnsureThreeChannels(Mat input)
        {
            if (input?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(input));

            if (input.Channels() == 3)
                return input.Clone();

            var output = new Mat();
            switch (input.Channels())
            {
                case 1: // 灰階 → BGR
                    Cv2.CvtColor(input, output, ColorConversionCodes.GRAY2BGR);
                    break;
                case 4: // RGBA → BGR
                    Cv2.CvtColor(input, output, ColorConversionCodes.RGBA2BGR);
                    break;
                default:
                    input.CopyTo(output);
                    break;
            }
            return output;
        }

        public static Mat BitmapToThreeChannelMat(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            // 🚀 直接使用OpenCV的標準轉換，保持BGR格式
            using var originalMat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap);
            return EnsureThreeChannels(originalMat); // 這個返回BGR格式
        }


        public static void SafeDispose(params Bitmap?[] bitmaps)
        {
            if (bitmaps == null) return;
            foreach (var bitmap in bitmaps)
            {
                bitmap?.Dispose();
            }
        }

        public static void SafeDispose<TKey>(Dictionary<TKey, Mat?> matDictionary) where TKey : notnull
        {
            if (matDictionary == null) return;
            foreach (var mat in matDictionary.Values)
            {
                mat?.Dispose();
            }
            matDictionary.Clear();
        }

        #endregion

        #region 路徑工具

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

        #endregion

        #region 數學工具

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

        #endregion

        #region NMS工具

        /// <summary>
        /// 通用的非極大值抑制算法
        /// </summary>
        /// <typeparam name="T">實現位置和尺寸介面的類型</typeparam>
        public static List<T> ApplyNMS<T>(List<T> items, double iouThreshold = 0.25, bool higherIsBetter = true) where T : class
        {
            if (items.Count <= 1) return items;

            // 🚀 轉為陣列並排序
            var itemArray = items.ToArray();
            Array.Sort(itemArray, (a, b) =>
                higherIsBetter ? GetConfidence(b).CompareTo(GetConfidence(a))
                              : GetConfidence(a).CompareTo(GetConfidence(b)));

            // 🚀 使用 bool 陣列標記被抑制的項目
            var suppressed = new bool[itemArray.Length];
            var nmsResults = new List<T>();

            for (int i = 0; i < itemArray.Length; i++)
            {
                if (suppressed[i]) continue;

                var current = itemArray[i];
                nmsResults.Add(current);
                var currentRect = GetBoundingBox(current);

                // 🚀 標記重疊項目而非移除
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

        /// <summary>
        /// 獲取物件的邊界框
        /// </summary>
        private static Rectangle GetBoundingBox<T>(T item)
        {
            return item switch
            {
                MonsterRenderInfo monster => new Rectangle(
                    monster.Location.X, monster.Location.Y,
                    monster.Size.Width, monster.Size.Height),
                MatchResult match => new Rectangle(
                    match.Position.X, match.Position.Y,
                    match.Size.Width, match.Size.Height),
                _ => throw new NotSupportedException($"不支持的類型: {typeof(T)}")
            };
        }

        /// <summary>
        /// 獲取物件的信心度
        /// </summary>
        private static double GetConfidence<T>(T item)
        {
            return item switch
            {
                MonsterRenderInfo monster => monster.Confidence,
                MatchResult match => match.Confidence,
                _ => throw new NotSupportedException($"不支持的類型: {typeof(T)}")
            };
        }

        #endregion

        #region 怪物模板管理工具
        private static readonly Dictionary<string, List<Bitmap>> _cachedMonsterTemplates = new();


        /// <summary>
        /// 清理模板快取
        /// </summary>
        public static void ClearMonsterTemplateCache()
        {
            foreach (var templates in _cachedMonsterTemplates.Values)
            {
                SafeDispose(templates.ToArray());
            }
            _cachedMonsterTemplates.Clear();
        }

        /// <summary>
        /// HSV 轉換工具
        /// </summary>
        public static Scalar ToOpenCvHsv((int h, int s, int v) hsv)
        {
            return new Scalar(hsv.h, hsv.s, hsv.v);
        }

        public static (int h, int s, int v) FromOpenCvHsv(Scalar hsv)
        {
            return ((int)hsv.Val0, (int)hsv.Val1, (int)hsv.Val2);
        }
        #endregion

    }
}
