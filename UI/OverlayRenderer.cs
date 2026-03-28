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
    /// <summary>
    /// 疊加層渲染器 — 負責在遊戲畫面上繪製所有偵測框、標記和資訊
    /// </summary>
    public class OverlayRenderer
    {
        /// <summary>
        /// 在 Bitmap 上繪製所有偵測結果的疊加層
        /// </summary>
        /// <param name="bitmap">要繪製的 Bitmap（會就地修改）</param>
        /// <param name="result">GamePipeline 的偵測結果快照</param>
        /// <param name="config">應用程式配置（包含顏色、線寬等設定）</param>
        /// <returns>繪製完成的 Bitmap 複本（呼叫者負責 Dispose）</returns>
        public Bitmap Render(Bitmap bitmap, Services.FrameProcessingResult result, AppConfig config)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (config == null) throw new ArgumentNullException(nameof(config));

            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // 血條框
            DrawingHelper.DrawRectangles(graphics, result.BloodBars,
                GameVisionCore.ParseColor(config.Appearance.PartyRedBar.FrameColor),
                config.Appearance.PartyRedBar.FrameThickness,
                GameVisionCore.ParseColor(config.Appearance.PartyRedBar.TextColor),
                config.Appearance.PartyRedBar.RedBarDisplayName);

            // 偵測框
            DrawingHelper.DrawRectangles(graphics, result.DetectionBoxes,
                GameVisionCore.ParseColor(config.Appearance.DetectionBox.FrameColor),
                config.Appearance.DetectionBox.FrameThickness,
                GameVisionCore.ParseColor(config.Appearance.DetectionBox.TextColor),
                config.Appearance.DetectionBox.BoxDisplayName);

            // 攻擊範圍框
            DrawingHelper.DrawRectangles(graphics, result.AttackRangeBoxes,
                GameVisionCore.ParseColor(config.Appearance.AttackRange.FrameColor),
                config.Appearance.AttackRange.FrameThickness,
                GameVisionCore.ParseColor(config.Appearance.AttackRange.TextColor),
                config.Appearance.AttackRange.RangeDisplayName);

            // 小地圖框
            DrawingHelper.DrawRectangles(graphics, result.MinimapBoxes,
                GameVisionCore.ParseColor(config.Appearance.Minimap.FrameColor),
                config.Appearance.Minimap.FrameThickness,
                GameVisionCore.ParseColor(config.Appearance.Minimap.TextColor),
                config.Appearance.Minimap.MinimapDisplayName);

            // 怪物
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
                    // 計算矩形中心點
                    var center = new PointF(marker.X + marker.Width / 2f, marker.Y + marker.Height / 2f);
                    // 繪製十字準心 (大小設為 5px，與 MinimapViewer 邏輯接近)
                    DrawingHelper.DrawCrosshair(graphics, center, 5f, color, style.FrameThickness);
                }
            }

            // 回傳克隆複本
            return (Bitmap)bitmap.Clone();
        }
    }
}
