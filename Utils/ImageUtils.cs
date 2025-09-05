using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 圖像處理工具類 - 三通道 BGR 版本
    /// </summary>
    public static class ImageUtils
    {
        private static readonly object _conversionLock = new object();

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
        /// 新增：創建HSV版本的Mat
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
        /// 新增：創建灰階版本的Mat
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
					// 🔧 額外保護：創建完全獨立的副本再轉換
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
		/// 創建三通道模板遮罩
		/// </summary>
		/// <param name="templateImg">模板圖像</param>
		/// <returns>遮罩 Mat</returns>
		public static Mat CreateThreeChannelTemplateMask(Mat templateImg)
        {
            if (templateImg?.Empty() == true)
                throw new ArgumentException("模板圖像為空", nameof(templateImg));

            var mask = new Mat();

            if (templateImg.Channels() == 4)
            {
                // 從三通道提取 Alpha 通道作為遮罩
                Mat[] channels = null;
                try
                {
                    channels = Cv2.Split(templateImg);
                    mask = channels[3].Clone();
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
                // 創建全白遮罩
                mask = Mat.Ones(templateImg.Size(), MatType.CV_8UC1) * 255;
            }

            return mask;
        }

        /// <summary>
        /// 創建黑色像素遮罩（三通道版本）
        /// </summary>
        /// <param name="img">輸入圖像</param>
        /// <returns>黑色像素遮罩</returns>
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

        public static void SafeDispose(ref Mat? mat)
        {
            mat?.Dispose();
            mat = null;
        }

        public static void SafeDispose(params Mat?[] mats)
        {
            if (mats == null) return;
            foreach (var mat in mats)
            {
                mat?.Dispose();
            }
        }

        public static void SafeDispose(IEnumerable<Mat> mats)
        {
            if (mats == null) return;
            foreach (var mat in mats)
            {
                mat?.Dispose();
            }
        }

        public static void SafeDispose<TKey>(Dictionary<TKey, Mat> matDictionary) where TKey : notnull
        {
            if (matDictionary == null) return;
            foreach (var mat in matDictionary.Values)
            {
                mat?.Dispose();
            }
            matDictionary.Clear();
        }
    }
}
