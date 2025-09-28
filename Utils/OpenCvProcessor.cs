using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Drawing;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// OpenCV 圖像處理專用類別 - 記憶體優化版
    /// </summary>
    public static class OpenCvProcessor
    {
        private static readonly object ConversionLock = new object();

        #region 原始方法（保持向後兼容）
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
        #endregion

        #region 記憶體安全版本
        /// <summary>
        /// 安全轉換 BGR 到 HSV，自動管理記憶體
        /// </summary>
        public static TResult SafeConvertToHSV<TResult>(Mat bgrMat, Func<Mat, TResult> processor)
        {
            return ResourceManager.SafeUseMat(
                ConvertToHSV(bgrMat),
                hsvMat => processor(hsvMat)
            );
        }

        /// <summary>
        /// 安全轉換 BGR 到灰階，自動管理記憶體
        /// </summary>
        public static TResult SafeConvertToGrayscale<TResult>(Mat bgrMat, Func<Mat, TResult> processor)
        {
            return ResourceManager.SafeUseMat(
                ConvertToGrayscale(bgrMat),
                grayMat => processor(grayMat)
            );
        }

        /// <summary>
        /// 安全創建黑色像素遮罩，自動管理記憶體
        /// </summary>
        public static TResult SafeCreateBlackPixelMask<TResult>(Mat img, Func<Mat, TResult> processor)
        {
            return ResourceManager.SafeUseMat(
                CreateBlackPixelMask(img),
                mask => processor(mask)
            );
        }

        /// <summary>
        /// 安全確保三通道格式，自動管理記憶體
        /// </summary>
        public static TResult SafeEnsureThreeChannels<TResult>(Mat input, Func<Mat, TResult> processor)
        {
            return ResourceManager.SafeUseMat(
                EnsureThreeChannels(input),
                output => processor(output)
            );
        }

        /// <summary>
        /// 安全 Bitmap 轉三通道 Mat，自動管理記憶體
        /// </summary>
        public static TResult SafeBitmapToThreeChannelMat<TResult>(Bitmap bitmap, Func<Mat, TResult> processor)
        {
            return ResourceManager.SafeUseMat(
                BitmapToThreeChannelMat(bitmap),
                mat => processor(mat)
            );
        }

        /// <summary>
        /// 安全組合操作：BGR → HSV → 處理，自動管理記憶體
        /// </summary>
        public static TResult SafeProcessWithHSV<TResult>(Mat bgrMat, Func<Mat, Mat, TResult> processor)
        {
            return ResourceManager.SafeUseMat(
                ConvertToHSV(bgrMat),
                hsvMat => processor(bgrMat, hsvMat)
            );
        }

        /// <summary>
        /// 安全組合操作：BGR → Grayscale → 處理，自動管理記憶體
        /// </summary>
        public static TResult SafeProcessWithGrayscale<TResult>(Mat bgrMat, Func<Mat, Mat, TResult> processor)
        {
            return ResourceManager.SafeUseMat(
                ConvertToGrayscale(bgrMat),
                grayMat => processor(bgrMat, grayMat)
            );
        }

        /// <summary>
        /// 複雜流程：BGR → HSV → Grayscale，全程記憶體管理
        /// </summary>
        public static TResult SafeProcessMultiFormat<TResult>(
            Mat bgrMat,
            Func<Mat, Mat, Mat, TResult> processor)
        {
            return ResourceManager.SafeUseMat(
                ConvertToHSV(bgrMat),
                hsvMat => ResourceManager.SafeUseMat(
                    ConvertToGrayscale(bgrMat),
                    grayMat => processor(bgrMat, hsvMat, grayMat)
                )
            );
        }

        /// <summary>
        /// 批次處理多個轉換，統一記憶體管理
        /// </summary>
        public static TResult SafeBatchProcess<TResult>(
            Mat sourceMat,
            bool needHsv,
            bool needGrayscale,
            bool needMask,
            Func<Mat, Mat?, Mat?, Mat?, TResult> processor)
        {
            Mat? hsvMat = null;
            Mat? grayMat = null;
            Mat? maskMat = null;

            try
            {
                if (needHsv)
                    hsvMat = ConvertToHSV(sourceMat);

                if (needGrayscale)
                    grayMat = ConvertToGrayscale(sourceMat);

                if (needMask)
                    maskMat = CreateBlackPixelMask(sourceMat);

                return processor(sourceMat, hsvMat, grayMat, maskMat);
            }
            finally
            {
                // 🎯 統一釋放所有中間結果
                hsvMat?.Dispose();
                grayMat?.Dispose();
                maskMat?.Dispose();
            }
        }
        #endregion

        #region 輔助方法（無記憶體問題）
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
        #endregion
    }
}
