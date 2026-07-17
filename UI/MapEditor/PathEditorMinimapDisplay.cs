using System;

namespace ArtaleAI.UI.MapEditor
{
    /// <summary>
    /// 路徑編輯小地圖顯示：只用整數倍 Nearest，避免分數倍率把像素風抹糊。
    /// </summary>
    internal static class PathEditorMinimapDisplay
    {
        public const int MinPixelScale = 1;
        public const int MaxPixelScale = 12;

        /// <summary>依編輯區大小選最大整數倍率，讓底圖貼滿且不超過視窗。</summary>
        public static int ResolveFitScale(int sourceWidth, int sourceHeight, int viewportWidth, int viewportHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
                return 3;

            int byWidth = viewportWidth / sourceWidth;
            int byHeight = viewportHeight / sourceHeight;
            int fitted = Math.Min(byWidth, byHeight);
            return Math.Clamp(fitted, MinPixelScale, MaxPixelScale);
        }

        public static int ClampPixelScale(int scale) =>
            Math.Clamp(scale, MinPixelScale, MaxPixelScale);
    }
}
