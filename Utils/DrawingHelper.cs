using System.Drawing;
using System.Drawing.Drawing2D;
using SdPoint = System.Drawing.Point;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 繪圖工具類別 - 統一管理 GDI+ 資源和繪圖邏輯
    /// </summary>
    public static class DrawingHelper
    {
        #region 基礎繪製形狀

        /// <summary>
        /// 繪製矩形列表
        /// </summary>
        public static void DrawRectangles(
        Graphics g,
        IEnumerable<Rectangle> rects,
        Color frameColor,
        float thickness,      
        Color? textColor = null,
        string? labelText = null,
        int textOffsetY = -15)
        {
            if (!rects.Any()) return;

            using (var pen = new Pen(frameColor, thickness))
            {
                if (!string.IsNullOrEmpty(labelText) && textColor.HasValue)
                {
                    using (var brush = new SolidBrush(textColor.Value))
                    using (var font = SystemFonts.DefaultFont)
                    {
                        foreach (var rect in rects)
                        {
                            g.DrawRectangle(pen, rect);
                            g.DrawString(labelText, font, brush, rect.X, rect.Y + textOffsetY);
                        }
                    }
                }
                else
                {
                    foreach (var rect in rects)
                    {
                        g.DrawRectangle(pen, rect);
                    }
                }
            }
        }

        /// <summary>
        /// 繪製實心矩形列表
        /// </summary>
        public static void FillRectangles(
            Graphics g,
            IEnumerable<Rectangle> rects,
            Color fillColor)
        {
            if (!rects.Any()) return;

            using (var brush = new SolidBrush(fillColor))
            {
                foreach (var rect in rects)
                {
                    g.FillRectangle(brush, rect);
                }
            }
        }

        /// <summary>
        /// 繪製圓形標記列表
        /// </summary>
        public static void DrawCircles(
            Graphics g,
            IEnumerable<PointF> points,
            float radius,
            Color frameColor,
            float thickness = 2f,
            bool filled = false)
        {
            if (!points.Any()) return;

            if (filled)
            {
                using (var brush = new SolidBrush(frameColor))
                {
                    foreach (var point in points)
                    {
                        g.FillEllipse(brush, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                    }
                }
            }
            else
            {
                using (var pen = new Pen(frameColor, thickness))
                {
                    foreach (var point in points)
                    {
                        g.DrawEllipse(pen, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                    }
                }
            }
        }

        #endregion

        #region 路徑繪製

        /// <summary>
        /// 繪製路徑 (線條 + 端點)
        /// 支援分段標記：座標為負數的點會被視為分段點，不繪製該點也不連線
        /// </summary>
        public static void DrawPath(
            Graphics g,
            IEnumerable<PointF> points,
            Color pathColor,
            float lineWidth = 2f,
            float pointRadius = 3f,
            bool showPoints = true)
        {
            var pointArray = points.ToArray();
            if (pointArray.Length < 2) return;

            // 繪製連接線（跳過分段標記點）
            using (var pen = new Pen(pathColor, lineWidth))
            {
                for (int i = 0; i < pointArray.Length - 1; i++)
                {
                    var p1 = pointArray[i];
                    var p2 = pointArray[i + 1];
                    
                    // 如果任一點座標為負數，視為分段標記，不連線
                    if (p1.X < 0 || p1.Y < 0 || p2.X < 0 || p2.Y < 0)
                        continue;
                    
                    g.DrawLine(pen, p1, p2);
                }
            }

            // 繪製端點（跳過分段標記點）
            if (showPoints)
            {
                using (var brush = new SolidBrush(Color.FromArgb(150, pathColor)))
                {
                    foreach (var point in pointArray)
                    {
                        // 跳過負座標的分段標記點
                        if (point.X < 0 || point.Y < 0)
                            continue;
                            
                        g.FillEllipse(brush, point.X - pointRadius, point.Y - pointRadius,
                                     pointRadius * 2, pointRadius * 2);
                    }
                }
            }
        }

        /// <summary>
        /// 繪製多條路徑 (不同顏色)
        /// </summary>
        public static void DrawMultiplePaths(
            Graphics g,
            Dictionary<string, (IEnumerable<PointF> Points, Color Color, float Width)> paths)
        {
            foreach (var kvp in paths)
            {
                DrawPath(g, kvp.Value.Points, kvp.Value.Color, kvp.Value.Width);
            }
        }

        #endregion

        #region 文字繪製

        /// <summary>
        /// 繪製文字 (帶背景)
        /// </summary>
        public static void DrawTextWithBackground(
            Graphics g,
            string text,
            PointF location,
            Color textColor,
            Color? backgroundColor = null,
            Font? customFont = null)
        {
            using (var font = customFont ?? SystemFonts.DefaultFont)
            using (var textBrush = new SolidBrush(textColor))
            {
                var textSize = g.MeasureString(text, font);

                if (backgroundColor.HasValue)
                {
                    using (var bgBrush = new SolidBrush(backgroundColor.Value))
                    {
                        g.FillRectangle(bgBrush, location.X, location.Y, textSize.Width, textSize.Height);
                    }
                }

                g.DrawString(text, font, textBrush, location);
            }
        }

        #endregion

        #region 圖像處理

        /// <summary>
        /// 建立高品質的放大圖像
        /// </summary>
        public static Bitmap? CreateZoomedImage(
            Bitmap sourceImage,
            Rectangle sourceRect,
            Size targetSize,
            InterpolationMode interpolation = InterpolationMode.HighQualityBicubic)
        {
            if (sourceImage == null || sourceRect.Width <= 0 || sourceRect.Height <= 0)
                return null;

            try
            {
                var zoomedImage = new Bitmap(targetSize.Width, targetSize.Height);

                using (var graphics = Graphics.FromImage(zoomedImage))
                {
                    graphics.InterpolationMode = interpolation;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;

                    graphics.DrawImage(
                        sourceImage,
                        new Rectangle(0, 0, targetSize.Width, targetSize.Height),
                        sourceRect,
                        GraphicsUnit.Pixel
                    );
                }

                return zoomedImage;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 繪製十字準心
        /// </summary>
        public static void DrawCrosshair(
            Graphics g,
            PointF center,
            float size,
            Color color,
            float thickness = 1f)
        {
            using (var pen = new Pen(color, thickness))
            {
                // 水平線
                g.DrawLine(pen, center.X - size, center.Y, center.X + size, center.Y);
                // 垂直線
                g.DrawLine(pen, center.X, center.Y - size, center.X, center.Y + size);
            }
        }

        #endregion

        #region 顏色工具

        /// <summary>
        /// 解析設定檔中的顏色字串
        /// </summary>
        public static Color ParseColor(string colorString, Color defaultColor = default)
        {
            try
            {
                // 支援 #RRGGBB 格式
                if (colorString.StartsWith("#") && colorString.Length == 7)
                {
                    int r = Convert.ToInt32(colorString.Substring(1, 2), 16);
                    int g = Convert.ToInt32(colorString.Substring(3, 2), 16);
                    int b = Convert.ToInt32(colorString.Substring(5, 2), 16);
                    return Color.FromArgb(r, g, b);
                }

                // 支援 ARGB 格式
                if (colorString.StartsWith("#") && colorString.Length == 9)
                {
                    int a = Convert.ToInt32(colorString.Substring(1, 2), 16);
                    int r = Convert.ToInt32(colorString.Substring(3, 2), 16);
                    int g = Convert.ToInt32(colorString.Substring(5, 2), 16);
                    int b = Convert.ToInt32(colorString.Substring(7, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }

                // 支援命名顏色
                return Color.FromName(colorString);
            }
            catch
            {
                return defaultColor;
            }
        }

        #endregion
    }
}
