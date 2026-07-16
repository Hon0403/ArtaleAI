namespace ArtaleAI.Models.Config
{
    /// <summary>
    /// 頻道列表相對面板模板的格網幾何（比例 0–1，對齊模板像素即可換算點擊）。
    /// </summary>
    public sealed class ChannelPickGridSettings
    {
        public int Columns { get; set; } = 5;

        public int VisibleRows { get; set; } = 10;

        /// <summary>格網左緣（相對面板寬）。</summary>
        public double LeftRatio { get; set; } = 0.018;

        /// <summary>格網上緣（略過標題＋「目前頻道」列）。</summary>
        public double TopRatio { get; set; } = 0.135;

        /// <summary>格網右緣（排除捲軸）。</summary>
        public double RightRatio { get; set; } = 0.955;

        /// <summary>格網下緣。</summary>
        public double BottomRatio { get; set; } = 0.985;

        public void Clamp()
        {
            Columns = Math.Clamp(Columns, 1, 10);
            VisibleRows = Math.Clamp(VisibleRows, 1, 20);
            LeftRatio = Math.Clamp(LeftRatio, 0, 0.4);
            TopRatio = Math.Clamp(TopRatio, 0, 0.5);
            RightRatio = Math.Clamp(RightRatio, LeftRatio + 0.2, 1);
            BottomRatio = Math.Clamp(BottomRatio, TopRatio + 0.2, 1);
        }
    }
}
