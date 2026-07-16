namespace ArtaleAI.Models.Config
{
    /// <summary>攻擊輪轉技能：冷卻就緒時可取代主攻鍵按下。</summary>
    public class AttackSkillEntry
    {
        public bool Enabled { get; set; }

        /// <summary>遊戲內攻擊／範圍技快捷鍵。</summary>
        public string Hotkey { get; set; } = "A";

        /// <summary>冷卻秒數（5–600）；到期後下一次鎖定怪時優先用此鍵。</summary>
        public int CooldownSeconds { get; set; } = 30;
    }
}
