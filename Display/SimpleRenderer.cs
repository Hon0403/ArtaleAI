using ArtaleAI.Models;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ArtaleAI.Display
{
    public static class SimpleRenderer
    {
        // 主要渲染方法
        public static Bitmap? RenderOverlays(
            Bitmap baseBitmap,
            IEnumerable<IRenderItem>? monsterItems,
            IEnumerable<IRenderItem>? minimapItems,
            IEnumerable<IRenderItem>? partyRedBarItems,
            IEnumerable<IRenderItem>? detectionBoxItems,
            IEnumerable<IRenderItem>? attackRangeItems = null)
        {
            if (baseBitmap == null) return null;

            var allItems = new List<IRenderItem>();
            allItems.AddRange(monsterItems ?? Enumerable.Empty<IRenderItem>());
            allItems.AddRange(minimapItems ?? Enumerable.Empty<IRenderItem>());
            allItems.AddRange(partyRedBarItems ?? Enumerable.Empty<IRenderItem>());
            allItems.AddRange(detectionBoxItems ?? Enumerable.Empty<IRenderItem>());
            allItems.AddRange(attackRangeItems ?? Enumerable.Empty<IRenderItem>());

            if (!allItems.Any()) return new Bitmap(baseBitmap);

            try
            {
                // 🚀 方案三：建立透明疊加層
                var overlay = new Bitmap(baseBitmap.Width, baseBitmap.Height, PixelFormat.Format32bppArgb);
                using (var gOverlay = Graphics.FromImage(overlay))
                {
                    // 清除為完全透明
                    gOverlay.Clear(Color.Transparent);

                    // 設定高品質繪製
                    gOverlay.SmoothingMode = SmoothingMode.AntiAlias;
                    gOverlay.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                    // 在透明層上一次性完成所有繪製
                    foreach (var item in allItems)
                    {
                        RenderSingleItemWithGraphics(gOverlay, item);
                    }
                }

                // 🚀 最終合成：只需要兩次 DrawImage
                var result = new Bitmap(baseBitmap.Width, baseBitmap.Height, PixelFormat.Format32bppArgb);
                using (var gFinal = Graphics.FromImage(result))
                {
                    gFinal.DrawImage(baseBitmap, 0, 0);    // 貼底圖
                    gFinal.DrawImage(overlay, 0, 0);       // 疊加透明層
                }

                overlay.Dispose(); // 釋放疊加層
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"透明疊加層渲染失敗: {ex.Message}");
                return new Bitmap(baseBitmap);
            }
        }


        private static void RenderSingleItemWithGraphics(Graphics g, IRenderItem item)
        {
            try
            {
                var rect = item.BoundingBox;
                using (var pen = new Pen(item.FrameColor, item.FrameThickness))
                {
                    g.DrawRectangle(pen, rect);
                }

                if (!string.IsNullOrEmpty(item.DisplayText))
                {
                    try
                    {
                        float fontSize = Math.Max(8f, Math.Min(24f, (float)item.TextScale * 10));
                        using (var font = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Regular))
                        using (var brush = new SolidBrush(item.TextColor))
                        using (var backgroundBrush = new SolidBrush(Color.FromArgb(128, Color.Black)))
                        {
                            var textSize = g.MeasureString(item.DisplayText, font);
                            var textRect = new RectangleF(rect.X, rect.Y - textSize.Height - 2,
                                textSize.Width + 4, textSize.Height + 2);
                            g.FillRectangle(backgroundBrush, textRect);
                            g.DrawString(item.DisplayText, font, brush, rect.X + 2, rect.Y - textSize.Height);
                        }
                    }
                    catch (Exception fontEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"字體渲染失敗: {fontEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"繪製項目失敗: {ex.Message}");
            }
        }
    }
}
