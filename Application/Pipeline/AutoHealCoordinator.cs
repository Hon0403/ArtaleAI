using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Shared;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>
    /// 依血魔％決定是否按下藥水快捷鍵。不持有鍵盤；由 Pipeline 注入 tap 動作。
    /// </summary>
    public sealed class AutoHealCoordinator
    {
        private const int PulseHoldMs = 2000;

        private DateTime _lastHpHealUtc = DateTime.MinValue;
        private DateTime _lastMpHealUtc = DateTime.MinValue;
        private DateTime _lastPulseUtc = DateTime.MinValue;
        private string _lastPulseLabel = string.Empty;

        public void TryHeal(
            AutoFarmSettings settings,
            PlayerVitalsSnapshot? vitals,
            DateTime nowUtc,
            Action<ushort> tapKey)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(tapKey);

            if (vitals?.HasFillReading != true)
                return;

            int cooldownMs = Math.Clamp(settings.HealCooldownMs, 200, 5000);
            bool tappedHp = false;
            bool tappedMp = false;

            if (settings.HealHpEnabled)
            {
                tappedHp = TryOne(
                    vitals.HpRatio,
                    settings.HealHpThresholdPercent,
                    settings.HealHpHotkey,
                    cooldownMs,
                    nowUtc,
                    ref _lastHpHealUtc,
                    tapKey);
            }

            if (settings.HealMpEnabled)
            {
                tappedMp = TryOne(
                    vitals.MpRatio,
                    settings.HealMpThresholdPercent,
                    settings.HealMpHotkey,
                    cooldownMs,
                    nowUtc,
                    ref _lastMpHealUtc,
                    tapKey);
            }

            if (tappedHp || tappedMp)
            {
                _lastPulseUtc = nowUtc;
                _lastPulseLabel = (tappedHp, tappedMp) switch
                {
                    (true, true) => "剛補 HP+MP",
                    (true, false) => "剛補 HP",
                    _ => "剛補 MP"
                };
            }
        }

        /// <summary>StatusBar 短提示：剛按過鍵優先；否則顯示仍低於門檻但在冷卻中。</summary>
        public string? GetStatusHint(
            AutoFarmSettings settings,
            PlayerVitalsSnapshot? vitals,
            DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if ((nowUtc - _lastPulseUtc).TotalMilliseconds < PulseHoldMs
                && !string.IsNullOrEmpty(_lastPulseLabel))
            {
                return _lastPulseLabel;
            }

            if (vitals?.HasFillReading != true)
                return null;

            int cooldownMs = Math.Clamp(settings.HealCooldownMs, 200, 5000);
            bool hpCooling = settings.HealHpEnabled
                && IsBelowThreshold(vitals.HpRatio, settings.HealHpThresholdPercent)
                && IsCoolingDown(nowUtc, _lastHpHealUtc, cooldownMs);
            bool mpCooling = settings.HealMpEnabled
                && IsBelowThreshold(vitals.MpRatio, settings.HealMpThresholdPercent)
                && IsCoolingDown(nowUtc, _lastMpHealUtc, cooldownMs);

            return (hpCooling, mpCooling) switch
            {
                (true, true) => "HP/MP 冷卻中",
                (true, false) => "HP 冷卻中",
                (false, true) => "MP 冷卻中",
                _ => null
            };
        }

        private static bool TryOne(
            double ratio,
            int thresholdPercent,
            string? hotkey,
            int cooldownMs,
            DateTime nowUtc,
            ref DateTime lastHealUtc,
            Action<ushort> tapKey)
        {
            if (!IsBelowThreshold(ratio, thresholdPercent))
                return false;

            if (IsCoolingDown(nowUtc, lastHealUtc, cooldownMs))
                return false;

            if (!VirtualKeyParser.TryParse(hotkey, out ushort vk))
                return false;

            tapKey(vk);
            lastHealUtc = nowUtc;
            return true;
        }

        private static bool IsBelowThreshold(double ratio, int thresholdPercent)
        {
            int threshold = Math.Clamp(thresholdPercent, 1, 99);
            int percent = (int)Math.Round(Math.Clamp(ratio, 0, 1) * 100);
            return percent < threshold;
        }

        private static bool IsCoolingDown(DateTime nowUtc, DateTime lastHealUtc, int cooldownMs)
            => lastHealUtc != DateTime.MinValue
               && (nowUtc - lastHealUtc).TotalMilliseconds < cooldownMs;
    }
}
