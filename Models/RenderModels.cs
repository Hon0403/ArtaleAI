using System;
using System.Drawing;
using ArtaleAI.Config;

namespace ArtaleAI.Models
{
    #region 渲染工具類
    public static class ColorHelper
    {
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
    #endregion

    #region 統一渲染項目 - 大幅簡化
    /// <summary>
    /// 統一渲染項目 - 取代所有特定類型的渲染項目
    /// </summary>
    public class RenderItem : IRenderItem
    {
        public Rectangle BoundingBox { get; set; }
        public string DisplayText { get; set; } = string.Empty;
        public Color FrameColor { get; set; }
        public Color TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }

        //  工廠方法模式 - 取代繼承
        public static RenderItem CreateMonster(MonsterOverlayStyle style, string name, double confidence)
        {
            return new RenderItem
            {
                DisplayText = style.ShowConfidence
                    ? string.Format(style.TextFormat, name, confidence)
                    : name,
                FrameColor = ColorHelper.ParseColor(style.FrameColor),
                TextColor = ColorHelper.ParseColor(style.TextColor),
                FrameThickness = style.FrameThickness,
                TextScale = style.TextScale,
                TextThickness = style.TextThickness
            };
        }

        public static RenderItem CreateBloodBar(PartyRedBarOverlayStyle style)
        {
            return new RenderItem
            {
                DisplayText = style.RedBarDisplayName,
                FrameColor = ColorHelper.ParseColor(style.FrameColor),
                TextColor = ColorHelper.ParseColor(style.TextColor),
                FrameThickness = style.FrameThickness,
                TextScale = style.TextScale,
                TextThickness = style.TextThickness
            };
        }

        public static RenderItem CreateDetectionBox(DetectionBoxOverlayStyle style)
        {
            return new RenderItem
            {
                DisplayText = style.BoxDisplayName,
                FrameColor = ColorHelper.ParseColor(style.FrameColor),
                TextColor = ColorHelper.ParseColor(style.TextColor),
                FrameThickness = style.FrameThickness,
                TextScale = style.TextScale,
                TextThickness = style.TextThickness
            };
        }

        public static RenderItem CreateAttackRange(AttackRangeOverlayStyle style)
        {
            return new RenderItem
            {
                DisplayText = style.RangeDisplayName,
                FrameColor = ColorHelper.ParseColor(style.FrameColor),
                TextColor = ColorHelper.ParseColor(style.TextColor),
                FrameThickness = style.FrameThickness,
                TextScale = style.TextScale,
                TextThickness = style.TextThickness
            };
        }

        public static RenderItem CreateMinimap(MinimapOverlayStyle style)
        {
            return new RenderItem
            {
                FrameColor = ColorHelper.ParseColor(style.FrameColor),
                TextColor = ColorHelper.ParseColor(style.TextColor),
                FrameThickness = (int)style.FrameThickness,
                TextScale = style.TextScale,
                TextThickness = 1
            };
        }
    }
    #endregion

    #region 其他模型 - 保持不變
    public class MonsterRenderInfo
    {
        public Point Location { get; set; }
        public Size Size { get; set; }
        public string MonsterName { get; set; } = "";
        public double Confidence { get; set; }
    }
    #endregion
}
