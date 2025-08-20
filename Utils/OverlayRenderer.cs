using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ArtaleAI.Config;

namespace ArtaleAI.Utils
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
            public string Label { get; set; } = "";

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
        /// 通用的疊加層渲染方法 - 四通道版本
        /// </summary>
        public static Bitmap RenderOverlays(Bitmap baseBitmap, params IEnumerable<IRenderItem>[] itemGroups)
        {
            if (baseBitmap == null) return null;

            // 合併所有渲染項目
            var allItems = itemGroups.SelectMany(group => group ?? Enumerable.Empty<IRenderItem>()).ToList();
            if (!allItems.Any()) return new Bitmap(baseBitmap);

            // 🔧 使用 ImageUtils 轉換為四通道
            using var mat = ImageUtils.BitmapToFourChannelMat(baseBitmap);
            using var drawFrame = mat.Clone();

            ImageUtils.LogImageInfo(mat, "RenderOverlays-Input");
            ImageUtils.LogImageInfo(drawFrame, "RenderOverlays-DrawFrame");

            foreach (var item in allItems)
            {
                RenderSingleItem(drawFrame, item);
            }

            return drawFrame.ToBitmap();
        }

        /// <summary>
        /// 渲染單個項目 - 四通道版本
        /// </summary>
        private static void RenderSingleItem(Mat drawFrame, IRenderItem item)
        {
            var rect = new Rect(item.BoundingBox.X, item.BoundingBox.Y,
                item.BoundingBox.Width, item.BoundingBox.Height);

            // 🔧 確保顏色轉換正確 (BGRA 格式)
            var frameColor = new Scalar(item.FrameColor.B, item.FrameColor.G, item.FrameColor.R, 255);
            Cv2.Rectangle(drawFrame, rect, frameColor, item.FrameThickness);

            // 繪製文字（如果有）
            if (!string.IsNullOrEmpty(item.DisplayText))
            {
                var textLocation = new OpenCvSharp.Point(rect.X, rect.Y - 10);
                var textColor = new Scalar(item.TextColor.B, item.TextColor.G, item.TextColor.R, 255);

                // 繪製文字背景
                var textSize = Cv2.GetTextSize(item.DisplayText, HersheyFonts.HersheyPlain,
                    item.TextScale, item.TextThickness, out _);
                var textBgRect = new Rect(rect.X, rect.Y - 25, textSize.Width + 10, 20);

                // 🔧 黑色背景也要四通道
                Cv2.Rectangle(drawFrame, textBgRect, new Scalar(0, 0, 0, 255), -1);

                // 繪製文字
                Cv2.PutText(drawFrame, item.DisplayText, textLocation,
                    HersheyFonts.HersheyPlain, item.TextScale, textColor, item.TextThickness);

                System.Diagnostics.Debug.WriteLine($"🎨 渲染項目: {item.DisplayText} at ({rect.X}, {rect.Y})");
            }
        }

        /// <summary>
        /// 便利方法：從現有的 MonsterRenderInfo 轉換
        /// </summary>
        public static List<MonsterRenderItem> FromMonsterRenderInfos(
            IEnumerable<MonsterRenderInfo> renderInfos, MonsterOverlayStyle style)
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
        private static Color ParseColor(string colorString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(colorString))
                {
                    // 從全域設定讀取預設顏色，而非硬編碼
                    return GetDefaultColor();
                }

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

            return GetDefaultColor();
        }

        private static Color GetDefaultColor()
        {
            // 可以通過依賴注入或全域設定存取
            // 或拋出異常強制提供有效顏色設定
            throw new InvalidOperationException(
                "顏色設定無效，請檢查 config.yaml 中的顏色格式 (R,G,B)");
        }

    }

}
