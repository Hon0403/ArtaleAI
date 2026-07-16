using ArtaleAI.Models.Config;
using ArtaleAI.Shared;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>
    /// 依間隔重按補助技能快捷鍵。不辨識 Buff 圖示；由 Pipeline 注入 tap。
    /// 間隔抖動寫死在程式內（非使用者設定）：略破固定節奏，又不至於大幅落於 Buff 時效外。
    /// </summary>
    public sealed class BuffSkillCoordinator
    {
        public const int MaxSlots = AutoFarmSettings.MaxBuffSkillSlots;

        /// <summary>
        /// ±10%：對「秒數略短於 Buff 持續」的設定夠溫和；20%（休息用）對技能過猛，易偶發掉 Buff。
        /// </summary>
        private const int IntervalJitterPercent = 10;

        private const int MinIntervalSeconds = 5;
        private const int MaxIntervalSeconds = 3600;
        private const int PulseHoldMs = 2000;

        private readonly DateTime[] _nextDueUtc = new DateTime[MaxSlots];
        private DateTime _lastAnyCastUtc = DateTime.MinValue;
        private DateTime _lastPulseUtc = DateTime.MinValue;
        private string _lastPulseLabel = string.Empty;
        private readonly Random _random = new();

        public void TryCast(
            AutoFarmSettings settings,
            DateTime nowUtc,
            Action<ushort> tapKey)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(tapKey);

            IReadOnlyList<BuffSkillEntry> skills = settings.BuffSkills;
            if (skills.Count == 0)
                return;

            int gapMs = Math.Clamp(settings.BuffCastGapMs, 100, 3000);
            if (_lastAnyCastUtc != DateTime.MinValue
                && (nowUtc - _lastAnyCastUtc).TotalMilliseconds < gapMs)
                return;

            int limit = Math.Min(MaxSlots, skills.Count);

            for (int i = 0; i < limit; i++)
            {
                BuffSkillEntry skill = skills[i];
                if (!skill.Enabled)
                    continue;

                if (_nextDueUtc[i] != DateTime.MinValue && nowUtc < _nextDueUtc[i])
                    continue;

                if (!VirtualKeyParser.TryParse(skill.Hotkey, out ushort vk))
                    continue;

                tapKey(vk);
                _lastAnyCastUtc = nowUtc;
                _nextDueUtc[i] = nowUtc.AddSeconds(ResolveNextIntervalSeconds(skill.IntervalSeconds));
                _lastPulseUtc = nowUtc;
                _lastPulseLabel = $"剛施放 {NormalizeHotkeyLabel(skill.Hotkey)}";
                return;
            }
        }

        public string? GetStatusHint(DateTime nowUtc)
        {
            if ((nowUtc - _lastPulseUtc).TotalMilliseconds < PulseHoldMs
                && !string.IsNullOrEmpty(_lastPulseLabel))
                return _lastPulseLabel;

            return null;
        }

        /// <summary>設定變更後重置排程，讓啟用中的技能盡快再排一次。</summary>
        public void ResetSchedule()
        {
            Array.Clear(_nextDueUtc);
            _lastAnyCastUtc = DateTime.MinValue;
        }

        private double ResolveNextIntervalSeconds(int intervalSeconds)
        {
            double baseSeconds = Math.Clamp(intervalSeconds, MinIntervalSeconds, MaxIntervalSeconds);
            double factor = 1.0 + ((_random.NextDouble() * 2.0) - 1.0) * (IntervalJitterPercent / 100.0);
            return Math.Max(MinIntervalSeconds, baseSeconds * factor);
        }

        private static string NormalizeHotkeyLabel(string? hotkey)
            => string.IsNullOrWhiteSpace(hotkey) ? "?" : hotkey.Trim();
    }
}
