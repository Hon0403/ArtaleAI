using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using SdPoint = System.Drawing.Point;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Utils
{
    /// <summary>GDI+ 輔助：偵測框、路徑、縮放與顏色字串解析。</summary>
    public static class DrawingHelper
    {
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

        /// <summary>折線與端點；負座標點為分段標記（不連線、不畫點）。</summary>
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

            using (var pen = new Pen(pathColor, lineWidth))
            {
                for (int i = 0; i < pointArray.Length - 1; i++)
                {
                    var p1 = pointArray[i];
                    var p2 = pointArray[i + 1];

                    if (p1.X < 0 || p1.Y < 0 || p2.X < 0 || p2.Y < 0)
                        continue;
                    
                    g.DrawLine(pen, p1, p2);
                }
            }

            if (showPoints)
            {
                using (var brush = new SolidBrush(Color.FromArgb(150, pathColor)))
                {
                    foreach (var point in pointArray)
                    {
                        if (point.X < 0 || point.Y < 0)
                            continue;
                            
                        g.FillEllipse(brush, point.X - pointRadius, point.Y - pointRadius,
                                     pointRadius * 2, pointRadius * 2);
                    }
                }
            }
        }

        public static void DrawMultiplePaths(
            Graphics g,
            Dictionary<string, (IEnumerable<PointF> Points, Color Color, float Width)> paths)
        {
            foreach (var kvp in paths)
            {
                DrawPath(g, kvp.Value.Points, kvp.Value.Color, kvp.Value.Width);
            }
        }

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

        public static void DrawCrosshair(
            Graphics g,
            PointF center,
            float size,
            Color color,
            float thickness = 1f)
        {
            using (var pen = new Pen(color, thickness))
            {
                g.DrawLine(pen, center.X - size, center.Y, center.X + size, center.Y);
                g.DrawLine(pen, center.X, center.Y - size, center.X, center.Y + size);
            }
        }

        /// <summary>解析 <c>#RRGGBB</c>、<c>#AARRGGBB</c> 或 <see cref="Color.FromName"/>。</summary>
        public static Color ParseColor(string colorString, Color defaultColor = default)
        {
            try
            {
                if (colorString.StartsWith("#") && colorString.Length == 7)
                {
                    int r = Convert.ToInt32(colorString.Substring(1, 2), 16);
                    int g = Convert.ToInt32(colorString.Substring(3, 2), 16);
                    int b = Convert.ToInt32(colorString.Substring(5, 2), 16);
                    return Color.FromArgb(r, g, b);
                }

                if (colorString.StartsWith("#") && colorString.Length == 9)
                {
                    int a = Convert.ToInt32(colorString.Substring(1, 2), 16);
                    int r = Convert.ToInt32(colorString.Substring(3, 2), 16);
                    int g = Convert.ToInt32(colorString.Substring(5, 2), 16);
                    int b = Convert.ToInt32(colorString.Substring(7, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }

                return Color.FromName(colorString);
            }
            catch
            {
                return defaultColor;
            }
        }
    }
}
