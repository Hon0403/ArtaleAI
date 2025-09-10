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
        /// 確保Mat是三通道BGR格式
        /// </summary>
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
                default:
                    input.CopyTo(output);
                    break;
            }
            return output;
        }

        /// <summary>
        /// 執行緒安全的 Bitmap 轉三通道 Mat
        /// </summary>
        public static Mat BitmapToThreeChannelMatSafe(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            lock (_conversionLock)
            {
                try
                {
                    // 創建完全獨立的副本再轉換
                    using var safeCopy = new Bitmap(bitmap.Width, bitmap.Height, bitmap.PixelFormat);
                    using (var g = Graphics.FromImage(safeCopy))
                    {
                        // 鎖定來源 bitmap 避免併發讀取
                        lock (bitmap)
                        {
                            g.DrawImage(bitmap, 0, 0);
                        }
                    }

                    using var originalMat = safeCopy.ToMat();
                    return EnsureThreeChannels(originalMat);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"轉換失敗: {ex.Message}");
                    throw new InvalidOperationException($"安全轉換失敗: {ex.Message}", ex);
                }
            }
        }

        public static Mat BitmapToThreeChannelMat(Bitmap bitmap, bool fastMode = true)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            if (fastMode)
            {
                // 🚀 快速模式：直接使用 OpenCvSharp 內建轉換器
                try
                {
                    using var originalMat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap);
                    return EnsureThreeChannels(originalMat);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"快速轉換失敗，回退至安全模式: {ex.Message}");
                    // 失敗時自動回退到原本的安全模式
                    return BitmapToThreeChannelMatSafe(bitmap);
                }
            }
            else
            {
                return BitmapToThreeChannelMatSafe(bitmap);
            }
        }

        /// <summary>
        /// 創建HSV版本的Mat
        /// </summary>
        public static Mat ConvertToHSV(Mat bgrMat)
        {
            if (bgrMat?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(bgrMat));

            var hsvMat = new Mat();
            Cv2.CvtColor(bgrMat, hsvMat, ColorConversionCodes.BGR2HSV);
            return hsvMat;
        }

        /// <summary>
        /// 創建灰階版本的Mat
        /// </summary>
        public static Mat ConvertToGrayscale(Mat bgrMat)
        {
            if (bgrMat?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(bgrMat));

            var grayMat = new Mat();
            Cv2.CvtColor(bgrMat, grayMat, ColorConversionCodes.BGR2GRAY);
            // 轉回三通道灰階
            var gray3ChMat = new Mat();
            Cv2.CvtColor(grayMat, gray3ChMat, ColorConversionCodes.GRAY2BGR);
            grayMat.Dispose();
            return gray3ChMat;
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

        /// <summary>
        /// 使用 HSV 顏色空間分離綠色背景（推薦）
        /// </summary>
        public static Mat CreateForegroundMaskHSV(Mat img)
        {
            if (img?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(img));

            using var hsvImg = new Mat();
            Cv2.CvtColor(img, hsvImg, ColorConversionCodes.BGR2HSV);

            var mask = new Mat();

            // ✅ HSV 綠色範圍：H(60-80), S(100-255), V(100-255)
            var lowerGreen = new Scalar(50, 80, 80);   // 較寬的綠色範圍
            var upperGreen = new Scalar(90, 255, 255);

            var greenMask = new Mat();
            Cv2.InRange(hsvImg, lowerGreen, upperGreen, greenMask);

            // 反轉：綠色=0，非綠色=255
            Cv2.BitwiseNot(greenMask, mask);
            greenMask.Dispose();

            return mask;
        }

        /// <summary>
        /// 多層HSV檢測，更準確分離綠色背景
        /// </summary>
        public static Mat CreateAdvancedForegroundMask(Mat img)
        {
            if (img?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(img));

            using var hsvImg = new Mat();
            Cv2.CvtColor(img, hsvImg, ColorConversionCodes.BGR2HSV);

            // 🎯 更精準的綠色範圍檢測
            var pureGreenMask = new Mat();
            Cv2.InRange(hsvImg, new Scalar(35, 80, 80), new Scalar(85, 255, 255), pureGreenMask);

            // 🎯 形態學處理
            var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));
            using var closedGreen = new Mat();

            // 填補洞
            Cv2.MorphologyEx(pureGreenMask, closedGreen, MorphTypes.Close, kernel);

            // 反轉得到前景遮罩
            var finalMask = new Mat();
            Cv2.BitwiseNot(closedGreen, finalMask);

            // 🔧 調試輸出
            try
            {
                var debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug");
                Directory.CreateDirectory(debugDir);

                pureGreenMask.SaveImage(Path.Combine(debugDir, "01_green_detection.png"));
                closedGreen.SaveImage(Path.Combine(debugDir, "02_closed.png"));
                finalMask.SaveImage(Path.Combine(debugDir, "05_final_mask.png"));

                Console.WriteLine($"✅ 調試圖像已保存");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"調試圖像保存失敗: {ex.Message}");
            }

            // 清理資源
            pureGreenMask.Dispose();
            kernel.Dispose();

            return finalMask;
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
        public static List<T> ApplyNMS<T>(
            List<T> items,
            double iouThreshold = 0.25,
            bool higherIsBetter = true)
            where T : class
        {
            if (items.Count <= 1) return items;

            var nmsResults = new List<T>();

            // 根據信心度排序
            var sortedItems = higherIsBetter
                ? items.OrderByDescending(GetConfidence).ToList()
                : items.OrderBy(GetConfidence).ToList();

            while (sortedItems.Any())
            {
                var best = sortedItems.First();
                nmsResults.Add(best);
                sortedItems.RemoveAt(0);

                var bestRect = GetBoundingBox(best);

                // 移除重疊度過高的項目
                sortedItems.RemoveAll(candidate =>
                {
                    var candidateRect = GetBoundingBox(candidate);
                    return CalculateIoU(bestRect, candidateRect) > iouThreshold;
                });
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
