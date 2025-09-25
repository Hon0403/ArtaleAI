using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Drawing;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// OpenCV 圖像處理專用類別
    /// </summary>
    public static class OpenCvProcessor
    {
        private static readonly object ConversionLock = new object();

        /// <summary>
        /// 將 BGR 圖像轉換為 HSV
        /// </summary>
        public static Mat ConvertToHSV(Mat bgrMat)
        {
            if (bgrMat == null || bgrMat.Empty())
                throw new ArgumentException("Mat 是 null 或是空值", nameof(bgrMat));

            lock (ConversionLock)
            {
                var hsvMat = new Mat();
                Cv2.CvtColor(bgrMat, hsvMat, ColorConversionCodes.BGR2HSV);
                return hsvMat;
            }
        }

        /// <summary>
        /// 將 BGR 圖像轉換為灰階
        /// </summary>
        public static Mat ConvertToGrayscale(Mat bgrMat)
        {
            if (bgrMat == null || bgrMat.Empty())
                throw new ArgumentException("Mat 是 null 或是空值", nameof(bgrMat));

            var grayMat = new Mat();
            Cv2.CvtColor(bgrMat, grayMat, ColorConversionCodes.BGR2GRAY);
            return grayMat;
        }

        /// <summary>
        /// 創建黑色像素遮罩
        /// </summary>
        public static Mat CreateBlackPixelMask(Mat img)
        {
            if (img == null || img.Empty())
                throw new ArgumentException("Mat 是 null 或是空值", nameof(img));

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
        /// 確保 Mat 為三通道格式
        /// </summary>
        public static Mat EnsureThreeChannels(Mat input)
        {
            if (input == null || input.Empty())
                throw new ArgumentException("Mat 是 null 或是空值", nameof(input));

            if (input.Channels() == 3)
                return input.Clone();

            var output = new Mat();
            switch (input.Channels())
            {
                case 1: // 灰階轉 BGR
                    Cv2.CvtColor(input, output, ColorConversionCodes.GRAY2BGR);
                    break;
                case 4: // RGBA 轉 BGR
                    Cv2.CvtColor(input, output, ColorConversionCodes.RGBA2BGR);
                    break;
                default:
                    input.CopyTo(output);
                    break;
            }
            return output;
        }

        /// <summary>
        /// Bitmap 轉為三通道 Mat
        /// </summary>
        public static Mat BitmapToThreeChannelMat(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            using var originalMat = BitmapConverter.ToMat(bitmap);
            return EnsureThreeChannels(originalMat);
        }

        /// <summary>
        /// HSV 顏色值轉換為 OpenCV Scalar
        /// </summary>
        public static Scalar ToOpenCvHsv(int h, int s, int v)
        {
            return new Scalar(h, s, v);
        }

        /// <summary>
        /// OpenCV Scalar 轉換為 HSV 顏色值
        /// </summary>
        public static (int h, int s, int v) FromOpenCvHsv(Scalar hsv)
        {
            return ((int)hsv.Val0, (int)hsv.Val1, (int)hsv.Val2);
        }
    }
}