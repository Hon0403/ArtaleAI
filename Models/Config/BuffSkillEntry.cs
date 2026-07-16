namespace ArtaleAI.Models.Config
{
    /// <summary>單一補助技能：依間隔重按遊戲熱鍵（無畫面辨識 Buff 是否還在）。</summary>
    public class BuffSkillEntry
    {
        public bool Enabled { get; set; }

        /// <summary>遊戲內技能快捷鍵名稱，例如 F1。</summary>
        public string Hotkey { get; set; } = "F1";

        /// <summary>每隔幾秒重開一次（5–3600）。</summary>
        public int IntervalSeconds { get; set; } = 180;
    }
}
