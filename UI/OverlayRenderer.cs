using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using ArtaleAI.Models.Config;
using ArtaleAI.Core;
using ArtaleAI.Models.Detection;
using ArtaleAI.Utils;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.UI
{
    /// <summary>在遊戲畫面 Bitmap 上繪製偵測框、小地圖與玩家標記。</summary>
    public class OverlayRenderer
    {
        /// <summary>就地繪製後回傳 <see cref="Bitmap"/> 複本（呼叫者負責 Dispose）。</summary>
        public Bitmap Render(Bitmap bitmap, Services.FrameProcessingResult result, AppConfig config)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (config == null) throw new ArgumentNullException(nameof(config));

            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            DrawingHelper.DrawRectangles(graphics, result.BloodBars,
                GameVisionCore.ParseColor(config.Appearance.PartyRedBar.FrameColor),
                config.Appearance.PartyRedBar.FrameThickness,
                GameVisionCore.ParseColor(config.Appearance.PartyRedBar.TextColor),
                config.Appearance.PartyRedBar.RedBarDisplayName);

            DrawingHelper.DrawRectangles(graphics, result.DetectionBoxes,
                GameVisionCore.ParseColor(config.Appearance.DetectionBox.FrameColor),
                config.Appearance.DetectionBox.FrameThickness,
                GameVisionCore.ParseColor(config.Appearance.DetectionBox.TextColor),
                config.Appearance.DetectionBox.BoxDisplayName);

            DrawingHelper.DrawRectangles(graphics, result.AttackRangeBoxes,
                GameVisionCore.ParseColor(config.Appearance.AttackRange.FrameColor),
                config.Appearance.AttackRange.FrameThickness,
                GameVisionCore.ParseColor(config.Appearance.AttackRange.TextColor),
                config.Appearance.AttackRange.RangeDisplayName);

            DrawingHelper.DrawRectangles(graphics, result.MinimapBoxes,
                GameVisionCore.ParseColor(config.Appearance.Minimap.FrameColor),
                config.Appearance.Minimap.FrameThickness,
                GameVisionCore.ParseColor(config.Appearance.Minimap.TextColor),
                config.Appearance.Minimap.MinimapDisplayName);

            if (result.Monsters.Any())
            {
                var style = config.Appearance.Monster;
                using var pen = new Pen(GameVisionCore.ParseColor(style.FrameColor), style.FrameThickness);
                using var brush = new SolidBrush(GameVisionCore.ParseColor(style.TextColor));
                using var font = SystemFonts.DefaultFont;

                foreach (var monster in result.Monsters)
                {
                    var rect = new SdRect(
                        monster.Position.X, monster.Position.Y,
                        monster.Size.Width, monster.Size.Height);
                    graphics.DrawRectangle(pen, rect);
                    if (!string.IsNullOrEmpty(monster.Name))
                    {
                        graphics.DrawString(
                            $"{monster.Name} ({monster.Confidence:F1})",
                            font, brush, rect.X, rect.Y - 15);
                    }
                }
            }

            if (result.MinimapMarkers.Any())
            {
                var style = config.Appearance.MinimapPlayer;
                var color = GameVisionCore.ParseColor(style.FrameColor);
                foreach (var marker in result.MinimapMarkers)
                {
                    var center = new PointF(marker.X + marker.Width / 2f, marker.Y + marker.Height / 2f);
                    DrawingHelper.DrawCrosshair(graphics, center, 5f, color, style.FrameThickness);
                }
            }

            return (Bitmap)bitmap.Clone();
        }
    }
}
