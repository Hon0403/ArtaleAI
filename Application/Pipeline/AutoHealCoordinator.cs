using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Shared;

namespace ArtaleAI.Application.Pipeline
{
    public enum HealResourceKind
    {
        Hp,
        Mp
    }

    /// <summary>補給效果判定後是否需撤退至安全區。</summary>
    public readonly record struct HealRetreatSignal(
        bool ShouldRetreat,
        HealResourceKind? FailedResource);

    /// <summary>
    /// 依血魔％決定是否按下藥水快捷鍵，並追蹤「按了仍未回升」的連續失敗。
    /// 不持有鍵盤；由 Pipeline 注入 tap 動作。
    /// </summary>
    public sealed class AutoHealCoordinator
    {
        private const int PulseHoldMs = 2000;
        private const double PercentToRatio = 0.01;

        private readonly ResourceWatch _hp = new();
        private readonly ResourceWatch _mp = new();
        private DateTime _lastPulseUtc = DateTime.MinValue;
        private string _lastPulseLabel = string.Empty;

        public void TryHeal(
            AutoFarmSettings settings,
            PlayerVitalsSnapshot? vitals,
            DateTime nowUtc,
            Action<ushort> tapKey)
        {
            _ = EvaluateAndHeal(settings, vitals, nowUtc, tapKey);
        }

        /// <summary>
        /// 評估待觀察的補給效果、必要時按鍵，並在連續無效達標時回報撤退。
        /// </summary>
        public HealRetreatSignal EvaluateAndHeal(
            AutoFarmSettings settings,
            PlayerVitalsSnapshot? vitals,
            DateTime nowUtc,
            Action<ushort> tapKey)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(tapKey);

            if (vitals?.HasFillReading != true)
                return default;

            int cooldownMs = Math.Clamp(settings.HealCooldownMs, 200, 5000);
            int observeMs = Math.Max(cooldownMs, Math.Clamp(settings.HealObserveMs, 200, 10000));
            double minRecovery = Math.Clamp(settings.HealMinRecoveryPercent, 1, 50) * PercentToRatio;
            int failLimit = Math.Clamp(settings.HealFailureAttempts, 1, 10);

            HealResourceKind? failed = null;

            if (settings.HealHpEnabled)
            {
                TryResolveOutcome(
                    _hp, vitals.HpRatio, vitals.ReadingId, nowUtc, minRecovery, failLimit, out bool hpFailed);
                if (hpFailed || _hp.ConsecutiveFailures >= failLimit)
                    failed = HealResourceKind.Hp;
            }

            if (settings.HealMpEnabled)
            {
                TryResolveOutcome(
                    _mp, vitals.MpRatio, vitals.ReadingId, nowUtc, minRecovery, failLimit, out bool mpFailed);
                if (failed == null && (mpFailed || _mp.ConsecutiveFailures >= failLimit))
                    failed = HealResourceKind.Mp;
            }

            // 已達撤退門檻：停止再按，避免無效藥水空轉。
            if (failed.HasValue)
                return new HealRetreatSignal(true, failed);

            bool tappedHp = false;
            bool tappedMp = false;

            if (settings.HealHpEnabled)
            {
                tappedHp = TryOne(
                    _hp,
                    vitals.HpRatio,
                    vitals.ReadingId,
                    settings.HealHpThresholdPercent,
                    settings.HealHpHotkey,
                    cooldownMs,
                    observeMs,
                    nowUtc,
                    tapKey);
            }

            if (settings.HealMpEnabled)
            {
                tappedMp = TryOne(
                    _mp,
                    vitals.MpRatio,
                    vitals.ReadingId,
                    settings.HealMpThresholdPercent,
                    settings.HealMpHotkey,
                    cooldownMs,
                    observeMs,
                    nowUtc,
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

            return default;
        }

        /// <summary>所有已啟用項目皆高於各自門檻（恢復打怪條件）。</summary>
        public bool AreEnabledVitalsAboveThreshold(
            AutoFarmSettings settings,
            PlayerVitalsSnapshot? vitals)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (vitals?.HasFillReading != true)
                return false;

            if (settings.HealHpEnabled
                && IsBelowThreshold(vitals.HpRatio, settings.HealHpThresholdPercent))
                return false;

            if (settings.HealMpEnabled
                && IsBelowThreshold(vitals.MpRatio, settings.HealMpThresholdPercent))
                return false;

            return settings.HealHpEnabled || settings.HealMpEnabled;
        }

        /// <summary>撤退解除或停止打怪時清掉失敗／觀察狀態，避免殘留觸發。</summary>
        public void ClearFailureState()
        {
            _hp.ResetOutcome();
            _mp.ResetOutcome();
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
                && IsCoolingDown(nowUtc, _hp.LastHealUtc, cooldownMs);
            bool mpCooling = settings.HealMpEnabled
                && IsBelowThreshold(vitals.MpRatio, settings.HealMpThresholdPercent)
                && IsCoolingDown(nowUtc, _mp.LastHealUtc, cooldownMs);

            return (hpCooling, mpCooling) switch
            {
                (true, true) => "HP/MP 冷卻中",
                (true, false) => "HP 冷卻中",
                (false, true) => "MP 冷卻中",
                _ => null
            };
        }

        /// <summary>
        /// 待觀察補給：必須等到觀察窗結束且出現比按鍵當下更新的 ReadingId。
        /// 無新鮮讀值時不計失敗，避免偵測空窗誤判。
        /// </summary>
        private static void TryResolveOutcome(
            ResourceWatch watch,
            double ratio,
            long readingId,
            DateTime nowUtc,
            double minRecovery,
            int failLimit,
            out bool reachedFailLimit)
        {
            reachedFailLimit = watch.ConsecutiveFailures >= failLimit;
            if (!watch.AwaitingOutcome)
                return;

            if (nowUtc < watch.ObserveUntilUtc)
                return;

            if (readingId <= watch.BaselineReadingId)
                return;

            watch.AwaitingOutcome = false;
            double delta = ratio - watch.BaselineRatio;
            if (delta >= minRecovery)
            {
                watch.ConsecutiveFailures = 0;
                reachedFailLimit = false;
                return;
            }

            watch.ConsecutiveFailures++;
            reachedFailLimit = watch.ConsecutiveFailures >= failLimit;
        }

        private static bool TryOne(
            ResourceWatch watch,
            double ratio,
            long readingId,
            int thresholdPercent,
            string? hotkey,
            int cooldownMs,
            int observeMs,
            DateTime nowUtc,
            Action<ushort> tapKey)
        {
            if (watch.AwaitingOutcome)
                return false;

            if (!IsBelowThreshold(ratio, thresholdPercent))
            {
                watch.ConsecutiveFailures = 0;
                return false;
            }

            if (IsCoolingDown(nowUtc, watch.LastHealUtc, cooldownMs))
                return false;

            if (!VirtualKeyParser.TryParse(hotkey, out ushort vk))
                return false;

            tapKey(vk);
            watch.LastHealUtc = nowUtc;
            watch.BaselineRatio = ratio;
            watch.BaselineReadingId = readingId;
            watch.ObserveUntilUtc = nowUtc.AddMilliseconds(observeMs);
            watch.AwaitingOutcome = true;
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

        private sealed class ResourceWatch
        {
            public DateTime LastHealUtc = DateTime.MinValue;
            public double BaselineRatio;
            public long BaselineReadingId;
            public DateTime ObserveUntilUtc = DateTime.MinValue;
            public int ConsecutiveFailures;
            public bool AwaitingOutcome;

            public void ResetOutcome()
            {
                AwaitingOutcome = false;
                ConsecutiveFailures = 0;
                BaselineRatio = 0;
                BaselineReadingId = 0;
                ObserveUntilUtc = DateTime.MinValue;
            }
        }
    }
}
