namespace ArtaleAI.Models.Config
{
    /// <summary>自動打怪行為參數（防偵測休息、自動喝水）。</summary>
    public class AutoFarmSettings
    {
        /// <summary>每隔多少分鐘休息一次；0 = 關閉。</summary>
        public int RestIntervalMinutes { get; set; } = 0;

        /// <summary>每次休息持續秒數。</summary>
        public int RestDurationSeconds { get; set; } = 60;

        /// <summary>間隔與持續時間的 ±百分比抖動；0 = 固定時程。</summary>
        public int RestJitterPercent { get; set; } = 20;

        public bool HealHpEnabled { get; set; }

        /// <summary>HP 低於此百分比時按補藥鍵（1–99）。</summary>
        public int HealHpThresholdPercent { get; set; } = 40;

        /// <summary>遊戲內 HP 藥水快捷鍵名稱，例如 Insert。</summary>
        public string HealHpHotkey { get; set; } = "Insert";

        public bool HealMpEnabled { get; set; }

        /// <summary>MP 低於此百分比時按補魔鍵（1–99）。</summary>
        public int HealMpThresholdPercent { get; set; } = 30;

        /// <summary>遊戲內 MP 藥水快捷鍵名稱，例如 Delete。</summary>
        public string HealMpHotkey { get; set; } = "Delete";

        /// <summary>同類型藥水最短間隔，避免連按。</summary>
        public int HealCooldownMs { get; set; } = 800;
    }
}
