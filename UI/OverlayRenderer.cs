using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using ArtaleAI.Models.Config;
using ArtaleAI.Vision;
using ArtaleAI.Models.Detection;
using ArtaleAI.Shared;
using ArtaleAI.Application.Pipeline;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.UI
{
    /// <summary>在遊戲畫面 Bitmap 上繪製偵測框、小地圖與玩家標記。</summary>
    public class OverlayRenderer
    {
        /// <summary>就地繪製後回傳 <see cref="Bitmap"/> 複本（呼叫者負責 Dispose）。</summary>
        public Bitmap Render(Bitmap bitmap, FrameProcessingResult result, AppConfig config)
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

            // 其他玩家：與自己標記分色，方便即時顯示驗證有無命中。
            if (result.OtherPlayerMarkers.Count > 0)
            {
                var otherColor = Color.OrangeRed;
                using var brush = new SolidBrush(otherColor);
                using var font = SystemFonts.DefaultFont;
                foreach (var marker in result.OtherPlayerMarkers)
                {
                    var center = new PointF(marker.X + marker.Width / 2f, marker.Y + marker.Height / 2f);
                    DrawingHelper.DrawCrosshair(graphics, center, 7f, otherColor, 2);
                    graphics.DrawEllipse(
                        Pens.OrangeRed,
                        center.X - 8f,
                        center.Y - 8f,
                        16f,
                        16f);
                    graphics.DrawString("其他", font, brush, center.X + 8f, center.Y - 10f);
                }

                if (result.MinimapBoxes.Count > 0)
                {
                    var mm = result.MinimapBoxes[0];
                    graphics.DrawString(
                        $"其他玩家: {result.OtherPlayerMarkers.Count}",
                        font,
                        brush,
                        mm.X + 4,
                        mm.Y + 4);
                }
            }

            DrawPlayerVitalsOverlay(graphics, result.PlayerVitals, config);
            DrawMinimapSearchRoiOverlay(graphics, bitmap.Width, bitmap.Height, config);
            DrawBloodBarSearchRoiOverlay(graphics, bitmap.Width, bitmap.Height, result, config);

            return (Bitmap)bitmap.Clone();
        }

        /// <summary>血條搜尋區：虛線框，與 DetectBloodBar 搜尋範圍一致（鎖定→鄰域；冷啟→可玩區）。</summary>
        private static void DrawBloodBarSearchRoiOverlay(
            Graphics graphics,
            int frameWidth,
            int frameHeight,
            FrameProcessingResult result,
            AppConfig config)
        {
            var vision = config.Vision;
            var style = config.Appearance.PartyRedBar;
            if (!style.ShowSearchRoi)
                return;

            SdRect? prevBar = result.BloodBars.Count > 0 ? result.BloodBars[0] : null;
            var searchRect = GameVisionCore.ResolveBloodBarDetectSearchRect(
                frameWidth, frameHeight, vision, prevBar);
            if (searchRect.Width <= 0 || searchRect.Height <= 0)
                return;

            // 顯示須與實際搜尋一致：遮罩階段挖空的是「實際小地圖框（青色）」，輪廓也扣同一塊。
            SdRect? detectedMinimap = result.MinimapBoxes.Count > 0 ? result.MinimapBoxes[0] : null;
            var minimapExclude = GameVisionCore.ResolveMinimapExclusion(
                detectedMinimap, frameWidth, frameHeight, vision);
            var exclusion = SdRect.Intersect(minimapExclude, searchRect);

            var color = GameVisionCore.ParseColor(style.SearchRoiFrameColor);
            float thickness = Math.Max(1f, style.FrameThickness);
            var labelPos = DrawDashedRectExcluding(graphics, searchRect, exclusion, color, thickness);

            string mode = prevBar.HasValue ? "追蹤" : "掃描";
            string label =
                $"HP搜尋 {searchRect.Width}x{searchRect.Height} ({mode})";
            DrawLabel(
                graphics,
                label,
                labelPos.X,
                labelPos.Y,
                GameVisionCore.ParseColor(style.TextColor));
        }

        /// <summary>
        /// 虛線繪製「搜尋區 − 左上角落排除區」的 L 形輪廓。
        /// 排除區由 ResolveMinimapExclusion 保證錨定於 (0,0)：右緣往上、下緣往左延伸即成 L 形，
        /// 小地圖尺寸改變時輪廓自動跟著伸縮。回傳不落在排除區內的標籤位置。
        /// </summary>
        private static PointF DrawDashedRectExcluding(
            Graphics graphics,
            SdRect searchRect,
            SdRect exclusion,
            Color color,
            float thickness)
        {
            var defaultLabel = new PointF(searchRect.X + 4, searchRect.Y + 4);
            bool cutsTopLeft =
                exclusion.Width > 0 && exclusion.Height > 0 &&
                exclusion.X <= searchRect.X && exclusion.Y <= searchRect.Y &&
                exclusion.Right > searchRect.Left && exclusion.Right < searchRect.Right &&
                exclusion.Bottom > searchRect.Top && exclusion.Bottom < searchRect.Bottom;

            if (!cutsTopLeft)
            {
                DrawDashedRect(graphics, searchRect, color, thickness);
                return defaultLabel;
            }

            var points = new[]
            {
                new Point(exclusion.Right, searchRect.Top),
                new Point(searchRect.Right, searchRect.Top),
                new Point(searchRect.Right, searchRect.Bottom),
                new Point(searchRect.Left, searchRect.Bottom),
                new Point(searchRect.Left, exclusion.Bottom),
                new Point(exclusion.Right, exclusion.Bottom),
            };

            using var pen = new Pen(color, thickness) { DashStyle = DashStyle.Dash };
            graphics.DrawPolygon(pen, points);
            using var innerPen = new Pen(Color.FromArgb(160, color), 1f);
            graphics.DrawPolygon(innerPen, points);

            return new PointF(exclusion.Right + 4, searchRect.Y + 4);
        }

        /// <summary>左上角百分比搜尋區：虛線框，與實際 FindMinimap 搜尋範圍一致。</summary>
        private static void DrawMinimapSearchRoiOverlay(
            Graphics graphics,
            int frameWidth,
            int frameHeight,
            AppConfig config)
        {
            var vision = config.Vision;
            var style = config.Appearance.Minimap;
            if (!vision.UseMinimapSearchRoi || !style.ShowSearchRoi)
                return;

            if (vision.UseFixedMinimapPosition)
                return;

            var searchRect = GameVisionCore.ResolveMinimapSearchRect(frameWidth, frameHeight, vision);
            if (searchRect.Width <= 0 || searchRect.Height <= 0)
                return;

            var roi = vision.MinimapSearchRoi ?? new MinimapSearchRoiPercent();
            var color = GameVisionCore.ParseColor(style.SearchRoiFrameColor);
            DrawDashedRect(graphics, searchRect, color, Math.Max(1f, style.FrameThickness));

            string label =
                $"Minimap ROI L{roi.LeftPercent:P0} T{roi.TopPercent:P0} " +
                $"W{roi.WidthPercent:P0} H{roi.HeightPercent:P0}";
            DrawLabel(
                graphics,
                label,
                searchRect.X + 4,
                searchRect.Y + 4,
                GameVisionCore.ParseColor(style.TextColor));
        }

        private static void DrawPlayerVitalsOverlay(
            Graphics graphics,
            PlayerVitalsSnapshot? vitals,
            AppConfig config)
        {
            if (vitals is not { IsLayoutValid: true })
                return;

            var vitalsSettings = config.PlayerVitals;
            if (!vitalsSettings.ShowRoiOverlay && !vitals.HasFillReading)
                return;

            var style = config.Appearance.PlayerVitals;
            var textColor = GameVisionCore.ParseColor(style.TextColor);

            if (style.ShowUiBand && vitals.UiBandRect.Width > 0 && vitals.UiBandRect.Height > 0)
            {
                DrawDashedRect(
                    graphics,
                    vitals.UiBandRect,
                    GameVisionCore.ParseColor(style.UiBandFrameColor),
                    1f);

                string bandLabel =
                    $"UI ≥{vitalsSettings.UiBandTopPercent:P0} | {vitals.FrameWidth}x{vitals.FrameHeight}";
                DrawLabel(graphics, bandLabel, vitals.UiBandRect.X + 4, vitals.UiBandRect.Y + 4, textColor);
            }

            string hpLabel = vitals.HasFillReading
                ? FormatVitalReading("HP", vitals.HpRatio, vitalsSettings.ReadingDecimalPlaces)
                : FormatRoiLabel("HP ROI", vitalsSettings.HpBar);
            string mpLabel = vitals.HasFillReading
                ? FormatVitalReading("MP", vitals.MpRatio, vitalsSettings.ReadingDecimalPlaces)
                : FormatRoiLabel("MP ROI", vitalsSettings.MpBar);

            var hpColor = GameVisionCore.ParseColor(style.HpFrameColor);
            var mpColor = GameVisionCore.ParseColor(style.MpFrameColor);

            // 只畫 ROI 框＋百分比文字；不畫填充預覽，避免青／紅半透明覆蓋與遊戲真條混淆。
            DrawVitalBar(graphics, vitals.HpBarRect, hpColor, textColor, style.FrameThickness, hpLabel);
            DrawVitalBar(graphics, vitals.MpBarRect, mpColor, textColor, style.FrameThickness, mpLabel);
        }

        private static string FormatRoiLabel(string prefix, BarRoiPercentAnchor anchor)
        {
            return $"{prefix} L{anchor.LeftPercent:P1} W{anchor.WidthPercent:P1} B{anchor.BottomPercent:P1} H{anchor.HeightPercent:P1}";
        }

        private static string FormatVitalReading(string prefix, double ratio, int decimalPlaces)
        {
            int digits = Math.Clamp(decimalPlaces, 0, 3);
            double percent = Math.Clamp(ratio, 0, 1) * 100;
            return $"{prefix} {percent.ToString($"F{digits}")}%";
        }

        private static void DrawVitalBar(
            Graphics graphics,
            SdRect rect,
            Color frameColor,
            Color textColor,
            int thickness,
            string label)
        {
            using var pen = new Pen(frameColor, thickness);
            graphics.DrawRectangle(pen, rect);
            using var highlight = new Pen(Color.FromArgb(180, Color.White), 1f);
            graphics.DrawLine(highlight, rect.Left, rect.Top, rect.Right, rect.Top);
            DrawLabelTopRight(graphics, label, rect, textColor);
        }

        private static void DrawLabelTopRight(Graphics graphics, string label, SdRect rect, Color textColor)
        {
            using var font = new Font(SystemFonts.DefaultFont.FontFamily, SystemFonts.DefaultFont.Size, FontStyle.Bold);
            var size = graphics.MeasureString(label, font);
            float x = rect.Right - size.Width - 2;
            float y = rect.Top + 2;
            DrawLabel(graphics, label, x, y, textColor, font);
        }

        private static void DrawDashedRect(Graphics graphics, SdRect rect, Color color, float thickness)
        {
            using var pen = new Pen(color, thickness) { DashStyle = DashStyle.Dash };
            graphics.DrawRectangle(pen, rect);
            using var innerPen = new Pen(Color.FromArgb(160, color), 1f);
            graphics.DrawRectangle(innerPen, rect);
        }

        private static void DrawLabel(Graphics graphics, string label, float x, float y, Color textColor)
        {
            using var font = new Font(SystemFonts.DefaultFont.FontFamily, SystemFonts.DefaultFont.Size, FontStyle.Bold);
            DrawLabel(graphics, label, x, y, textColor, font);
        }

        private static void DrawLabel(Graphics graphics, string label, float x, float y, Color textColor, Font font)
        {
            using var outline = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
            using var brush = new SolidBrush(textColor);
            graphics.DrawString(label, font, outline, x + 1, y + 1);
            graphics.DrawString(label, font, brush, x, y);
        }
    }
}
