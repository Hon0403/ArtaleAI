using ArtaleAI.Config;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ArtaleAI.Display
{
    /// <summary>
    /// 通用的疊加層渲染器 - 支援配置化樣式
    /// </summary>
    public static class OverlayRenderer
    {
        /// <summary>
        /// 渲染項目的基礎介面
        /// </summary>
        public interface IRenderItem
        {
            Rectangle BoundingBox { get; }
            string DisplayText { get; }
            Color FrameColor { get; }
            Color TextColor { get; }
            int FrameThickness { get; }
            double TextScale { get; }
            int TextThickness { get; }
        }

        /// <summary>
        /// 怪物渲染項目 - 配置化版本
        /// </summary>
        public class MonsterRenderItem : IRenderItem
        {
            public Rectangle BoundingBox { get; set; }
            public string MonsterName { get; set; } = "";
            public double Confidence { get; set; }
            private readonly MonsterOverlayStyle _style;

            public MonsterRenderItem(MonsterOverlayStyle style)
            {
                _style = style ?? throw new ArgumentNullException(nameof(style));
            }

            public string DisplayText => _style.ShowConfidence
                ? string.Format(_style.TextFormat, MonsterName, Confidence)
                : MonsterName;
            public Color FrameColor => ParseColor(_style.FrameColor);
            public Color TextColor => ParseColor(_style.TextColor);
            public int FrameThickness => _style.FrameThickness;
            public double TextScale => _style.TextScale;
            public int TextThickness => _style.TextThickness;
        }

        /// <summary>
        /// 小地圖渲染項目 - 配置化版本
        /// </summary>
        public class MinimapRenderItem : IRenderItem
        {
            public Rectangle BoundingBox { get; set; }
            private readonly MinimapOverlayStyle _style;

            public MinimapRenderItem(MinimapOverlayStyle style)
            {
                _style = style ?? throw new ArgumentNullException(nameof(style));
            }

            public string DisplayText => _style.MinimapDisplayName;
            public Color FrameColor => ParseColor(_style.FrameColor);
            public Color TextColor => ParseColor(_style.TextColor);
            public int FrameThickness => _style.FrameThickness;
            public double TextScale => _style.TextScale;
            public int TextThickness => _style.TextThickness;
        }

        /// <summary>
        /// 玩家位置渲染項目 - 配置化版本
        /// </summary>
        public class PlayerRenderItem : IRenderItem
        {
            public Rectangle BoundingBox { get; set; }
            private readonly PlayerOverlayStyle _style;

            public PlayerRenderItem(PlayerOverlayStyle style)
            {
                _style = style ?? throw new ArgumentNullException(nameof(style));
            }

            public string DisplayText => _style.PlayerDisplayName;
            public Color FrameColor => ParseColor(_style.FrameColor);
            public Color TextColor => ParseColor(_style.TextColor);
            public int FrameThickness => _style.FrameThickness;
            public double TextScale => _style.TextScale;
            public int TextThickness => _style.TextThickness;
        }

        /// <summary>
        /// 隊友血條渲染項目 - 配置化版本
        /// </summary>
        public class PartyRedBarRenderItem : IRenderItem
        {
            public Rectangle BoundingBox { get; set; }
            private readonly PartyRedBarOverlayStyle _style;

            public PartyRedBarRenderItem(PartyRedBarOverlayStyle style)
            {
                _style = style ?? throw new ArgumentNullException(nameof(style));
            }

            public string DisplayText => _style.RedBarDisplayName;
            public Color FrameColor => ParseColor(_style.FrameColor);
            public Color TextColor => ParseColor(_style.TextColor);
            public int FrameThickness => _style.FrameThickness;
            public double TextScale => _style.TextScale;
            public int TextThickness => _style.TextThickness;
        }

        /// <summary>
        ///  檢測框渲染項目 - 矩形框版本
        /// </summary>
        public class DetectionBoxRenderItem : IRenderItem
        {
            public Rectangle BoundingBox { get; set; }
            private readonly DetectionBoxOverlayStyle _style;

            public DetectionBoxRenderItem(DetectionBoxOverlayStyle style)
            {
                _style = style ?? throw new ArgumentNullException(nameof(style));
            }

            public string DisplayText => _style.BoxDisplayName;
            public Color FrameColor => ParseColor(_style.FrameColor);
            public Color TextColor => ParseColor(_style.TextColor);
            public int FrameThickness => _style.FrameThickness;
            public double TextScale => _style.TextScale;
            public int TextThickness => _style.TextThickness;
        }

        /// <summary>
        ///  修復：正確的參數類型聲明
        /// </summary>
        public static Bitmap RenderOverlays(
            Bitmap baseBitmap,
            IEnumerable<MonsterRenderItem>? monsterItems,
            IEnumerable<MinimapRenderItem>? minimapItems,
            IEnumerable<PlayerRenderItem>? playerItems,
            IEnumerable<PartyRedBarRenderItem>? partyRedBarItems,
            IEnumerable<DetectionBoxRenderItem>? detectionBoxItems) //  改為DetectionBoxRenderItem
        {
            if (baseBitmap == null) return null;

            var allItems = new List<IRenderItem>();
            allItems.AddRange(monsterItems ?? Enumerable.Empty<MonsterRenderItem>());
            allItems.AddRange(minimapItems ?? Enumerable.Empty<MinimapRenderItem>());
            allItems.AddRange(playerItems ?? Enumerable.Empty<PlayerRenderItem>());
            allItems.AddRange(partyRedBarItems ?? Enumerable.Empty<PartyRedBarRenderItem>());
            allItems.AddRange(detectionBoxItems ?? Enumerable.Empty<DetectionBoxRenderItem>());

            if (!allItems.Any()) return new Bitmap(baseBitmap);

            using var mat = ImageUtils.BitmapToThreeChannelMat(baseBitmap);
            using var drawFrame = mat.Clone();

            foreach (var item in allItems)
            {
                RenderSingleItem(drawFrame, item);
            }

            return drawFrame.ToBitmap();
        }

        /// <summary>
        /// 渲染單個項目 - 三通道版本
        /// </summary>
        private static void RenderSingleItem(Mat drawFrame, IRenderItem item)
        {
            var rect = new Rect(item.BoundingBox.X, item.BoundingBox.Y,
                item.BoundingBox.Width, item.BoundingBox.Height);
            var frameColor = new Scalar(item.FrameColor.B, item.FrameColor.G, item.FrameColor.R);

            // 繪製矩形框
            Cv2.Rectangle(drawFrame, rect, frameColor, item.FrameThickness);

            // 繪製文字標籤
            if (!string.IsNullOrEmpty(item.DisplayText))
            {
                var textLocation = new OpenCvSharp.Point(rect.X, rect.Y - 10);
                var textColor = new Scalar(item.TextColor.B, item.TextColor.G, item.TextColor.R);

                var textSize = Cv2.GetTextSize(item.DisplayText, HersheyFonts.HersheyPlain,
                    item.TextScale, item.TextThickness, out _);

                var textBgRect = new Rect(rect.X, rect.Y - 25, textSize.Width + 10, 20);
                Cv2.Rectangle(drawFrame, textBgRect, new Scalar(0, 0, 0), -1);

                Cv2.PutText(drawFrame, item.DisplayText, textLocation,
                    HersheyFonts.HersheyPlain, item.TextScale, textColor, item.TextThickness);
            }
        }

        /// <summary>
        /// 便利方法：從現有的 MonsterRenderInfo 轉換
        /// </summary>
        public static List<MonsterRenderItem> FromMonsterRenderInfos(
            IEnumerable<MonsterRenderInfo>? renderInfos, MonsterOverlayStyle style)
        {
            return renderInfos?.Select(info => new MonsterRenderItem(style)
            {
                BoundingBox = new Rectangle(info.Location.X, info.Location.Y,
                    info.Size.Width, info.Size.Height),
                MonsterName = info.MonsterName,
                Confidence = info.Confidence
            }).ToList() ?? new List<MonsterRenderItem>();
        }

        /// <summary>
        /// 解析顏色字串 "R,G,B" 為 Color 物件
        /// </summary>
        public static Color ParseColor(string colorString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(colorString))
                    return Color.Yellow;

                var parts = colorString.Split(',');
                if (parts.Length >= 3)
                {
                    int r = int.Parse(parts[0].Trim());
                    int g = int.Parse(parts[1].Trim());
                    int b = int.Parse(parts[2].Trim());

                    if (r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
                        return Color.FromArgb(r, g, b);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"顏色解析失敗: {colorString} - {ex.Message}");
            }

            return Color.Yellow;
        }
    }
}
