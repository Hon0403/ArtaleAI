using System.Drawing;
using System.Linq;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Shared
{
    /// <summary>GDI+ 輔助：偵測框與十字準心。</summary>
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

            using var pen = new Pen(frameColor, thickness);
            if (!string.IsNullOrEmpty(labelText) && textColor.HasValue)
            {
                using var brush = new SolidBrush(textColor.Value);
                using var font = SystemFonts.DefaultFont;
                foreach (var rect in rects)
                {
                    g.DrawRectangle(pen, rect);
                    g.DrawString(labelText, font, brush, rect.X, rect.Y + textOffsetY);
                }
            }
            else
            {
                foreach (var rect in rects)
                    g.DrawRectangle(pen, rect);
            }
        }

        public static void DrawCrosshair(
            Graphics g,
            PointF center,
            float size,
            Color color,
            float thickness = 1f)
        {
            using var pen = new Pen(color, thickness);
            g.DrawLine(pen, center.X - size, center.Y, center.X + size, center.Y);
            g.DrawLine(pen, center.X, center.Y - size, center.X, center.Y + size);
        }
    }
}
