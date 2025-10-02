using ArtaleAI.Models;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ArtaleAI.Display
{
    public static class SimpleRenderer
    {
        //  大幅簡化 - 單一方法處理所有渲染
        public static Bitmap? RenderOverlays(Bitmap baseBitmap, IEnumerable<IRenderItem>? allItems)
        {
            if (baseBitmap == null || allItems == null) return null;

            var result = new Bitmap(baseBitmap.Width, baseBitmap.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.DrawImage(baseBitmap, 0, 0);

            foreach (var item in allItems)
                RenderSingleItem(g, item);

            return result;
        }

        private static void RenderSingleItem(Graphics g, IRenderItem item)
        {
            try
            {
                var rect = item.BoundingBox;
                using var pen = new Pen(item.FrameColor, item.FrameThickness);
                g.DrawRectangle(pen, rect);

                //  統一異常處理，不需要嵌套
                if (!string.IsNullOrEmpty(item.DisplayText))
                {
                    RenderItemText(g, item, rect);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"繪製項目失敗: {ex.Message}");
            }
        }

        private static void RenderItemText(Graphics g, IRenderItem item, Rectangle rect)
        {
            float fontSize = Math.Max(8f, Math.Min(24f, (float)item.TextScale * 10));
            using var font = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Regular);
            using var brush = new SolidBrush(item.TextColor);
            using var backgroundBrush = new SolidBrush(Color.FromArgb(128, Color.Black));

            var textSize = g.MeasureString(item.DisplayText, font);
            var textRect = new RectangleF(rect.X, rect.Y - textSize.Height - 2,
                textSize.Width + 4, textSize.Height + 2);
            g.FillRectangle(backgroundBrush, textRect);
            g.DrawString(item.DisplayText, font, brush, rect.X + 2, rect.Y - textSize.Height);
        }
    }
}
