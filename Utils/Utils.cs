using OpenCvSharp;
using OpenCvSharp.Extensions;

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
                case 4: // BGRA → BGR
                    Cv2.CvtColor(input, output, ColorConversionCodes.BGRA2BGR);
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
        public static Mat BitmapToThreeChannelMat(Bitmap bitmap)
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

            if (templateImg.Channels() == 4)
            {
                // 🔥 修正：從BGRA提取Alpha通道並確保格式正確
                Mat[] channels = null;
                try
                {
                    channels = Cv2.Split(templateImg);
                    var alphaMask = channels[3].Clone();

                    // 確保是單通道 CV_8U 格式
                    if (alphaMask.Type() != MatType.CV_8UC1)
                    {
                        alphaMask.ConvertTo(mask, MatType.CV_8UC1);
                        alphaMask.Dispose();
                    }
                    else
                    {
                        mask = alphaMask;
                    }

                    // 二值化處理：透明=0, 不透明=255
                    Cv2.Threshold(mask, mask, 1, 255, ThresholdTypes.Binary);
                }
                finally
                {
                    if (channels != null)
                    {
                        foreach (var ch in channels)
                            ch?.Dispose();
                    }
                }
            }
            else
            {
                mask = Mat.Ones(templateImg.Size(), MatType.CV_8UC1) * 255;
            }

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
                // BGR 格式：檢查三個通道是否為黑色
                Cv2.InRange(img, new Scalar(0, 0, 0), new Scalar(0, 0, 0), mask);
            }
            else if (img.Channels() == 4)
            {
                // BGRA 格式：檢查前三個通道，忽略 Alpha
                Cv2.InRange(img, new Scalar(0, 0, 0, 0), new Scalar(0, 0, 0, 255), mask);
            }
            else
            {
                Cv2.InRange(img, new Scalar(0), new Scalar(0), mask);
            }
            return mask;
        }

        public static void SafeDispose(params Mat?[] mats)
        {
            if (mats == null) return;
            foreach (var mat in mats)
            {
                mat?.Dispose();
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
    }
}
