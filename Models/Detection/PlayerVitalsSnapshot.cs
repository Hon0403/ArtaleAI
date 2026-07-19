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

        /// <summary>
        /// 每次成功量測填充率時遞增；佈局-only 更新不改動。
        /// 補給效果判定只消費更新的 ReadingId，避免同幀重算失敗。
        /// </summary>
        public long ReadingId { get; init; }

        /// <summary>本次填充量測的 UTC 時間；無填充讀數時為 MinValue。</summary>
        public DateTime MeasuredAtUtc { get; init; }

        public static PlayerVitalsSnapshot Empty { get; } = new();
    }
}
