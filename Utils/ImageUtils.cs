using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 圖像處理工具類 - 專門處理 OpenCV 和通道轉換
    /// </summary>
    public static class ImageUtils
    {
        /// <summary>
        /// 確保 Mat 是四通道 BGRA 格式
        /// </summary>
        /// <param name="input">輸入的 Mat</param>
        /// <param name="makeOpaque">是否將 Alpha 通道設為不透明 (預設: true)</param>
        /// <returns>四通道 BGRA 格式的 Mat</returns>
        public static Mat EnsureFourChannels(Mat input, bool makeOpaque = true)
        {
            if (input?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(input));

            // 已經是四通道，直接返回副本
            if (input.Channels() == 4)
                return input.Clone();

            var output = new Mat();

            // 根據輸入通道數進行轉換
            switch (input.Channels())
            {
                case 1: // 灰階 -> BGRA
                    Cv2.CvtColor(input, output, ColorConversionCodes.GRAY2BGRA);
                    break;
                case 3: // BGR -> BGRA
                    Cv2.CvtColor(input, output, ColorConversionCodes.BGR2BGRA);
                    break;
                default:
                    input.CopyTo(output);
                    break;
            }

            // 確保 Alpha 通道為不透明
            if (makeOpaque && output.Channels() == 4)
            {
                SetAlphaChannel(output, 255);
            }

            return output;
        }

        /// <summary>
        /// 設定 Alpha 通道值
        /// </summary>
        /// <param name="image">四通道圖像</param>
        /// <param name="alphaValue">Alpha 值 (0-255)</param>
        public static void SetAlphaChannel(Mat image, byte alphaValue)
        {
            if (image.Channels() != 4)
                throw new ArgumentException("圖像必須是四通道格式", nameof(image));

            Mat[] channels = null;
            try
            {
                channels = Cv2.Split(image);
                channels[3].SetTo(new Scalar(alphaValue));
                Cv2.Merge(channels, image);
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

        /// <summary>
        /// 安全地轉換 Bitmap 為四通道 Mat
        /// </summary>
        /// <param name="bitmap">輸入的 Bitmap</param>
        /// <param name="makeOpaque">是否將 Alpha 通道設為不透明</param>
        /// <returns>四通道 BGRA 格式的 Mat</returns>
        public static Mat BitmapToFourChannelMat(Bitmap bitmap, bool makeOpaque = true)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            using var originalMat = bitmap.ToMat();
            return EnsureFourChannels(originalMat, makeOpaque);
        }

        /// <summary>
        /// 創建四通道模板遮罩
        /// </summary>
        /// <param name="templateImg">模板圖像</param>
        /// <returns>遮罩 Mat</returns>
        public static Mat CreateFourChannelTemplateMask(Mat templateImg)
        {
            if (templateImg?.Empty() == true)
                throw new ArgumentException("模板圖像為空", nameof(templateImg));

            var mask = new Mat();

            if (templateImg.Channels() == 4)
            {
                // 使用 Alpha 通道作為遮罩
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
        /// 創建黑色像素遮罩（四通道版本）
        /// </summary>
        /// <param name="img">輸入圖像</param>
        /// <returns>黑色像素遮罩</returns>
        public static Mat CreateBlackPixelMask(Mat img)
        {
            if (img?.Empty() == true)
                throw new ArgumentException("輸入圖像為空", nameof(img));

            var mask = new Mat();

            if (img.Channels() == 4)
            {
                // BGRA 格式：檢查前三個通道是否為黑色，忽略 Alpha
                Cv2.InRange(img, new Scalar(0, 0, 0, 0), new Scalar(0, 0, 0, 255), mask);
            }
            else
            {
                Cv2.InRange(img, new Scalar(0, 0, 0), new Scalar(0, 0, 0), mask);
            }

            return mask;
        }

        /// <summary>
        /// 記錄圖像通道資訊（調試用）
        /// </summary>
        /// <param name="mat">要檢查的 Mat</param>
        /// <param name="name">圖像名稱</param>
        public static void LogImageInfo(Mat mat, string name)
        {
            if (mat?.Empty() != false)
            {
                System.Diagnostics.Debug.WriteLine($"🔍 {name}: 空圖像或 null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"🔍 {name}: {mat.Width}x{mat.Height}, {mat.Channels()} 通道, 類型: {mat.Type()}");
        }
    }
}
