using System.Drawing;

namespace ArtaleAI.Models.Detection
{
    /// <summary>玩家底部 UI 血魔條：ROI 佈局與可選填充讀數。</summary>
    public sealed record PlayerVitalsSnapshot
    {
        public double HpRatio { get; init; }
        public double MpRatio { get; init; }
        public Rectangle HpBarRect { get; init; }
        public Rectangle MpBarRect { get; init; }
        public Rectangle UiBandRect { get; init; }
        public int FrameWidth { get; init; }
        public int FrameHeight { get; init; }

        /// <summary>百分比 ROI 已成功換算且落在畫面內。</summary>
        public bool IsLayoutValid { get; init; }

        /// <summary>HSV 填充率讀取成功（與 ROI 校準可獨立）。</summary>
        public bool HasFillReading { get; init; }

        public static PlayerVitalsSnapshot Empty { get; } = new();
    }
}
