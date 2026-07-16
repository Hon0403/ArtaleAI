using ArtaleAI.Models.Config;
using ArtaleAI.Shared;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>
    /// 決定本次攻擊按哪個鍵：冷卻就緒的輪轉技優先，否則主攻。
    /// 冷卻抖動寫死 ±10%（同補助技能策略）。
    /// </summary>
    public sealed class AttackRotationCoordinator
    {
        public const int MaxSlots = AutoFarmSettings.MaxAttackSkillSlots;
        private const int CooldownJitterPercent = 10;
        private const int MinCooldownSeconds = 5;
        private const int MaxCooldownSeconds = 600;
        private const int PulseHoldMs = 1500;

        private readonly DateTime[] _nextReadyUtc = new DateTime[MaxSlots];
        private DateTime _lastPulseUtc = DateTime.MinValue;
        private string _lastPulseLabel = string.Empty;
        private readonly Random _random = new();

        /// <summary>選出本次攻擊 Virtual-Key；無法解析主攻則失敗。</summary>
        public bool TrySelectAttackKey(
            AutoFarmSettings settings,
            DateTime nowUtc,
            out ushort virtualKey,
            out string displayLabel)
        {
            virtualKey = 0;
            displayLabel = string.Empty;
            ArgumentNullException.ThrowIfNull(settings);

            settings.EnsureAttackSkillSlots();
            IReadOnlyList<AttackSkillEntry> skills = settings.AttackSkills;
            int limit = Math.Min(MaxSlots, skills.Count);

            for (int i = 0; i < limit; i++)
            {
                AttackSkillEntry skill = skills[i];
                if (!skill.Enabled)
                    continue;

                if (_nextReadyUtc[i] != DateTime.MinValue && nowUtc < _nextReadyUtc[i])
                    continue;

                if (!VirtualKeyParser.TryParse(skill.Hotkey, out ushort skillVk))
                    continue;

                virtualKey = skillVk;
                displayLabel = NormalizeLabel(skill.Hotkey);
                _nextReadyUtc[i] = nowUtc.AddSeconds(ResolveCooldownSeconds(skill.CooldownSeconds));
                _lastPulseUtc = nowUtc;
                _lastPulseLabel = $"攻擊技 {displayLabel}";
                return true;
            }

            if (!VirtualKeyParser.TryParse(settings.AttackPrimaryHotkey, out virtualKey))
                return false;

            displayLabel = NormalizeLabel(settings.AttackPrimaryHotkey);
            return true;
        }

        public string? GetStatusHint(DateTime nowUtc)
        {
            if ((nowUtc - _lastPulseUtc).TotalMilliseconds < PulseHoldMs
                && !string.IsNullOrEmpty(_lastPulseLabel))
                return _lastPulseLabel;

            return null;
        }

        public void ResetCooldowns()
        {
            Array.Clear(_nextReadyUtc);
        }

        private double ResolveCooldownSeconds(int cooldownSeconds)
        {
            double baseSeconds = Math.Clamp(cooldownSeconds, MinCooldownSeconds, MaxCooldownSeconds);
            double factor = 1.0 + ((_random.NextDouble() * 2.0) - 1.0) * (CooldownJitterPercent / 100.0);
            return Math.Max(MinCooldownSeconds, baseSeconds * factor);
        }

        private static string NormalizeLabel(string? hotkey)
            => string.IsNullOrWhiteSpace(hotkey) ? "?" : hotkey.Trim();
    }
}
