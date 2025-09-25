using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 記憶體資源管理器 - 解決 Bitmap 和 Mat 的記憶體洩漏問題
    /// </summary>
    public static class ResourceManager
    {
        #region Bitmap 自動管理

        /// <summary>
        /// 安全使用 Bitmap：自動釋放記憶體
        /// </summary>
        public static void SafeUseBitmap(Bitmap bitmap, Action<Bitmap> operation)
        {
            try
            {
                operation(bitmap);
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        /// <summary>
        /// 安全使用 Bitmap：有返回值版本
        /// </summary>
        public static TResult SafeUseBitmap<TResult>(Bitmap bitmap, Func<Bitmap, TResult> operation)
        {
            try
            {
                return operation(bitmap);
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        /// <summary>
        /// 創建並安全使用 Bitmap
        /// </summary>
        public static TResult CreateAndUseBitmap<TResult>(int width, int height, Func<Bitmap, TResult> operation)
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            return operation(bitmap);
        }

        #endregion

        #region Mat 自動管理

        /// <summary>
        /// 安全使用 Mat：自動釋放記憶體
        /// </summary>
        public static void SafeUseMat(Mat mat, Action<Mat> operation)
        {
            try
            {
                operation(mat);
            }
            finally
            {
                mat?.Dispose();
            }
        }

        /// <summary>
        /// 安全使用 Mat：有返回值版本
        /// </summary>
        public static TResult SafeUseMat<TResult>(Mat mat, Func<Mat, TResult> operation)
        {
            try
            {
                return operation(mat);
            }
            finally
            {
                mat?.Dispose();
            }
        }

        #endregion
    }
}
