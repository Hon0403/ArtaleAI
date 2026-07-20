namespace ArtaleAI.Application.Pipeline
{
    /// <summary>
    /// 攻擊與導航共享鍵盤時的仲裁規則（純決策，不持有鍵）。
    /// 楓之谷主攻為長按連打；輪轉技仍採單次脈衝。
    /// </summary>
    public static class AttackInputArbiter
    {
        /// <summary>放開攻擊鍵後，再次取得租約前的最短間隔（防抖）。</summary>
        public const int SessionCooldownMs = 150;

        /// <summary>面向重點最短間隔，減少同方向重複 tap。</summary>
        public const int DirectionChangeCooldownMs = 200;

        /// <summary>輪轉技等單次脈衝的按下時間。</summary>
        public const int SkillPulseMs = 35;

        /// <summary>長按主攻時，重驗目標與中斷條件的輪詢間隔。</summary>
        public const int HoldPollMs = 50;

        /// <summary>長按安全上限，避免偵測異常時攻擊鍵永遠不放開。</summary>
        public const int MaxHoldMs = 4000;

        public static bool IsCooldownReady(DateTime lastSessionEndUtc, DateTime nowUtc, int cooldownMs = SessionCooldownMs)
        {
            if (lastSessionEndUtc == DateTime.MinValue)
                return true;

            int gate = cooldownMs > 0 ? cooldownMs : SessionCooldownMs;
            return (nowUtc - lastSessionEndUtc).TotalMilliseconds >= gate;
        }

        public static bool ShouldRetapFacing(
            DateTime lastFacingUtc,
            DateTime nowUtc,
            int cooldownMs = DirectionChangeCooldownMs)
        {
            int gate = cooldownMs > 0 ? cooldownMs : DirectionChangeCooldownMs;
            return (nowUtc - lastFacingUtc).TotalMilliseconds > gate;
        }
    }
}
