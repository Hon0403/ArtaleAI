namespace ArtaleAI.Models.Config
{
    /// <summary>玩家底部 UI 列 HP／MP 條 ROI（以畫面百分比定義，適應任意解析度）。</summary>
    public class PlayerVitalsSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>即時顯示疊加 ROI 框。</summary>
        public bool ShowRoiOverlay { get; set; } = true;

        public int DetectIntervalMs { get; set; } = 100;

        /// <summary>底部 UI 帶上緣，佔畫面高度比例（0~1）；帶狀 ROI 從此處延伸到畫面底。</summary>
        public double UiBandTopPercent { get; set; } = 0.91;

        public BarRoiPercentAnchor HpBar { get; set; } = new()
        {
            LeftPercent = 0.269,
            WidthPercent = 0.126,
            BottomPercent = 0.024,
            HeightPercent = 0.018
        };

        public BarRoiPercentAnchor MpBar { get; set; } = new()
        {
            LeftPercent = 0.4,
            WidthPercent = 0.166,
            BottomPercent = 0.024,
            HeightPercent = 0.018
        };

        public int[] LowerRedHsv { get; set; } = [0, 70, 70];
        public int[] UpperRedHsv { get; set; } = [12, 255, 255];
        public int[] LowerBlueHsv { get; set; } = [95, 80, 100];
        public int[] UpperBlueHsv { get; set; } = [130, 255, 255];
        public double EmaAlpha { get; set; } = 0.55;

        /// <summary>關閉 EMA 平滑，顯示每幀原始讀值（更精確、較抖動）。</summary>
        public bool SmoothReadings { get; set; } = true;

        /// <summary>欄位內垂直填充密度達此比例即視為「有色」（0~1）。</summary>
        public double ColumnFillThreshold { get; set; } = 0.45;

        public int MinFilledColumns { get; set; } = 1;

        /// <summary>量測前 ROI 內縮像素，避開灰框。</summary>
        public int RoiInnerMarginPx { get; set; } = 0;

        /// <summary>疊加顯示的小數位數。</summary>
        public int ReadingDecimalPlaces { get; set; } = 2;

        /// <summary>垂直取樣行數（0 = 使用 ROI 內全部行）。</summary>
        public int SampleRowCount { get; set; } = 0;

        /// <summary>自動從底部 UI 色條推算 ROI（config 已手動校準時請關閉）。</summary>
        public bool UseAutoLayout { get; set; } = true;
    }

    /// <summary>狀態條 ROI：以畫面寬高百分比描述（左緣、底邊距、寬、高）。</summary>
    public class BarRoiPercentAnchor
    {
        /// <summary>條左緣 X，佔畫面寬度比例。</summary>
        public double LeftPercent { get; set; } = 0.27;

        /// <summary>條寬度，佔畫面寬度比例。</summary>
        public double WidthPercent { get; set; } = 0.12;

        /// <summary>條底邊距畫面底緣，佔畫面高度比例。</summary>
        public double BottomPercent { get; set; } = 0.06;

        /// <summary>條高度，佔畫面高度比例。</summary>
        public double HeightPercent { get; set; } = 0.02;
    }
}
