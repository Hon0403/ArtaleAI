using System.Drawing;
using OpenCvSharp;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 快取管理專用類別
    /// </summary>
    public static class CacheManager
    {
        private static readonly Dictionary<string, List<Bitmap>> cachedMonsterTemplates = new();

        /// <summary>
        /// 清除怪物模板快取
        /// </summary>
        public static void ClearMonsterTemplateCache()
        {
            foreach (var templates in cachedMonsterTemplates.Values)
            {
                SafeDispose(templates.ToArray());
            }
            cachedMonsterTemplates.Clear();
        }

        /// <summary>
        /// 安全釋放多個 Bitmap
        /// </summary>
        public static void SafeDispose(params Bitmap?[] bitmaps)
        {
            if (bitmaps == null) return;

            foreach (var bitmap in bitmaps)
            {
                bitmap?.Dispose();
            }
        }

        /// <summary>
        /// 安全釋放字典中的所有 Mat
        /// </summary>
        public static void SafeDispose<TKey>(Dictionary<TKey, Mat?> matDictionary) where TKey : notnull
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
