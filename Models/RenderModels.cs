using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using ArtaleAI.Config;

namespace ArtaleAI.Models
{
    #region 渲染工具類

    /// <summary>
    /// 統一的顏色解析工具類
    /// </summary>
    public static class ColorHelper
    {
        /// <summary>
        /// 根據字串解析顏色 (支援RGB格式：「255,0,0」)
        /// </summary>
        /// <param name="colorString">顏色字串</param>
        /// <returns>解析出的顏色</returns>
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

    #region 渲染項目實現

    /// <summary>
    /// 怪物渲染項目
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
        public Color FrameColor => ColorHelper.ParseColor(_style.FrameColor);
        public Color TextColor => ColorHelper.ParseColor(_style.TextColor);
        public int FrameThickness => _style.FrameThickness;
        public double TextScale => _style.TextScale;
        public int TextThickness => _style.TextThickness;
    }

    /// <summary>
    /// 隊友血條渲染項目
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
        public Color FrameColor => ColorHelper.ParseColor(_style.FrameColor);
        public Color TextColor => ColorHelper.ParseColor(_style.TextColor);
        public int FrameThickness => _style.FrameThickness;
        public double TextScale => _style.TextScale;
        public int TextThickness => _style.TextThickness;
    }

    /// <summary>
    /// 檢測框渲染項目
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
        public Color FrameColor => ColorHelper.ParseColor(_style.FrameColor);
        public Color TextColor => ColorHelper.ParseColor(_style.TextColor);
        public int FrameThickness => _style.FrameThickness;
        public double TextScale => _style.TextScale;
        public int TextThickness => _style.TextThickness;
    }

    /// <summary>
    /// 攻擊範圍渲染項目
    /// </summary>
    public class AttackRangeRenderItem : IRenderItem
    {
        public Rectangle BoundingBox { get; set; }
        private readonly AttackRangeOverlayStyle _style;

        public AttackRangeRenderItem(AttackRangeOverlayStyle style)
        {
            _style = style ?? throw new ArgumentNullException(nameof(style));
        }

        public string DisplayText => _style.RangeDisplayName;
        public Color FrameColor => ColorHelper.ParseColor(_style.FrameColor);
        public Color TextColor => ColorHelper.ParseColor(_style.TextColor);
        public int FrameThickness => _style.FrameThickness;
        public double TextScale => _style.TextScale;
        public int TextThickness => _style.TextThickness;
    }

    #endregion

    #region 渲染相關模型

    public class MonsterRenderInfo
    {
        public Point Location { get; set; }
        public Size Size { get; set; }
        public string MonsterName { get; set; } = "";
        public double Confidence { get; set; }
    }

    #endregion
}