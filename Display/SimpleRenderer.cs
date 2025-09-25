using ArtaleAI.Models;
using ArtaleAI.Utils;
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
                // 🎯 使用 ResourceManager 管理覆蓋層
                return ResourceManager.CreateAndUseBitmap(baseBitmap.Width, baseBitmap.Height, overlay =>
                {
                    using (var gOverlay = Graphics.FromImage(overlay))
                    {
                        gOverlay.Clear(Color.Transparent);
                        gOverlay.SmoothingMode = SmoothingMode.AntiAlias;
                        gOverlay.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                        foreach (var item in allItems)
                        {
                            RenderSingleItemWithGraphics(gOverlay, item);
                        }
                    }

                    // 🎯 創建最終結果並自動管理
                    return ResourceManager.CreateAndUseBitmap(baseBitmap.Width, baseBitmap.Height, result =>
                    {
                        using (var gFinal = Graphics.FromImage(result))
                        {
                            gFinal.DrawImage(baseBitmap, 0, 0);
                            gFinal.DrawImage(overlay, 0, 0);
                        }
                        return new Bitmap(result);  // 返回副本
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"渲染失敗: {ex.Message}");
                try
                {
                    return new Bitmap(baseBitmap);
                }
                catch
                {
                    return null;
                }
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
