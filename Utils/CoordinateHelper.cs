using System.Drawing;
using SdPoint = System.Drawing.Point;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 座標轉換工具類別
    /// </summary>
    public static class CoordinateHelper
    {
        #region PictureBox 與 Minimap 座標轉換

        /// <summary>
        /// 將 PictureBox 控制項座標轉換為小地圖實際座標
        /// </summary>
        public static PointF ScreenToMinimapF(PointF pictureBoxPoint, SdRect minimapBounds)
        {
            if (minimapBounds.Width == 0 || minimapBounds.Height == 0)
                return pictureBoxPoint;

            float x = (pictureBoxPoint.X / minimapBounds.Width) * 100f;
            float y = (pictureBoxPoint.Y / minimapBounds.Height) * 100f;

            return new PointF(x, y);
        }

        /// <summary>
        /// 將小地圖實際座標轉換為 PictureBox 控制項座標
        /// </summary>
        public static PointF MinimapToScreenF(PointF minimapPoint, SdRect minimapBounds)
        {
            if (minimapBounds.Width == 0 || minimapBounds.Height == 0)
                return minimapPoint;

            float x = (minimapPoint.X / 100f) * minimapBounds.Width;
            float y = (minimapPoint.Y / 100f) * minimapBounds.Height;

            return new PointF(x, y);
        }

        /// <summary>
        /// 批次轉換座標陣列 (Minimap → Screen)
        /// </summary>
        public static PointF[] MinimapToScreenBatch(IEnumerable<PointF> minimapPoints, SdRect minimapBounds)
        {
            return minimapPoints.Select(p => MinimapToScreenF(p, minimapBounds)).ToArray();
        }

        #endregion

        #region PictureBox 顯示區域座標轉換 (用於放大鏡)

        /// <summary>
        /// 計算 PictureBox 的實際顯示區域
        /// </summary>
        public static SdRect GetDisplayRect(PictureBox pictureBox)
        {
            if (pictureBox.Image == null)
                return SdRect.Empty;

            float imageAspect = (float)pictureBox.Image.Width / pictureBox.Image.Height;
            float boxAspect = (float)pictureBox.Width / pictureBox.Height;

            int displayWidth, displayHeight, displayX, displayY;

            if (imageAspect > boxAspect)
            {
                // 圖片較寬,水平填滿
                displayWidth = pictureBox.Width;
                displayHeight = (int)(pictureBox.Width / imageAspect);
                displayX = 0;
                displayY = (pictureBox.Height - displayHeight) / 2;
            }
            else
            {
                // 圖片較高,垂直填滿
                displayWidth = (int)(pictureBox.Height * imageAspect);
                displayHeight = pictureBox.Height;
                displayX = (pictureBox.Width - displayWidth) / 2;
                displayY = 0;
            }

            return new SdRect(displayX, displayY, displayWidth, displayHeight);
        }

        /// <summary>
        /// 將控制項座標轉換為圖片實際像素座標 (考慮縮放)
        /// </summary>
        public static SdPoint ControlToImagePoint(SdPoint controlPoint, PictureBox pictureBox)
        {
            if (pictureBox.Image == null)
                return SdPoint.Empty;

            var displayRect = GetDisplayRect(pictureBox);

            // 座標相對於顯示區域
            var relativeX = controlPoint.X - displayRect.X;
            var relativeY = controlPoint.Y - displayRect.Y;

            // 計算縮放比例
            var scaleX = (float)pictureBox.Image.Width / displayRect.Width;
            var scaleY = (float)pictureBox.Image.Height / displayRect.Height;

            // 轉換為圖片像素座標
            var imagePointX = (int)(relativeX * scaleX);
            var imagePointY = (int)(relativeY * scaleY);

            return new SdPoint(imagePointX, imagePointY);
        }

        /// <summary>
        /// 檢查控制項座標是否在顯示區域內
        /// </summary>
        public static bool IsPointInDisplayArea(SdPoint controlPoint, PictureBox pictureBox)
        {
            var displayRect = GetDisplayRect(pictureBox);
            return displayRect.Contains(controlPoint);
        }

        #endregion

        #region 座標邊界限制

        /// <summary>
        /// 將座標限制在指定範圍內
        /// </summary>
        public static PointF ClampPoint(PointF point, float minX, float minY, float maxX, float maxY)
        {
            return new PointF(
                Math.Max(minX, Math.Min(maxX, point.X)),
                Math.Max(minY, Math.Min(maxY, point.Y))
            );
        }

        /// <summary>
        /// 將整數座標限制在指定範圍內
        /// </summary>
        public static SdPoint ClampPoint(SdPoint point, int minX, int minY, int maxX, int maxY)
        {
            return new SdPoint(
                Math.Max(minX, Math.Min(maxX, point.X)),
                Math.Max(minY, Math.Min(maxY, point.Y))
            );
        }

        #endregion
    }
}
