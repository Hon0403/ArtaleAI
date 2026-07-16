using ArtaleAI.Models.Config;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>
    /// 小地圖偵到其他玩家時：進入退避視窗並回傳「剛觸發」（由 Pipeline 啟動換頻序列）。
    /// </summary>
    public sealed class OtherPlayerAvoidanceCoordinator
    {
        private const int MinCooldownSeconds = 30;
        private const int MaxCooldownSeconds = 600;
        private const int MinPauseSeconds = 5;
        private const int MaxPauseSeconds = 180;
        private const int PulseHoldMs = 2500;

        private DateTime _avoidUntilUtc = DateTime.MinValue;
        private DateTime _nextTriggerUtc = DateTime.MinValue;
        private DateTime _lastPulseUtc = DateTime.MinValue;
        private string _lastPulseLabel = string.Empty;

        public bool IsAvoiding { get; private set; }

        /// <summary>若剛觸發退避，回傳 true（呼叫端應停移動並跑換頻）。</summary>
        public bool TryUpdate(
            AutoFarmSettings settings,
            int otherPlayerCount,
            DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.ChangeChannelOnOtherPlayers)
            {
                ClearAvoidance();
                return false;
            }

            if (IsAvoiding && nowUtc >= _avoidUntilUtc)
            {
                if (otherPlayerCount > 0)
                {
                    _avoidUntilUtc = nowUtc.AddSeconds(5);
                    IsAvoiding = true;
                    return false;
                }

                ClearAvoidanceEpisode();
                return false;
            }

            if (IsAvoiding)
                return false;

            if (otherPlayerCount <= 0)
                return false;

            if (_nextTriggerUtc != DateTime.MinValue && nowUtc < _nextTriggerUtc)
                return false;

            int pauseSec = Math.Clamp(settings.ChangeChannelPauseSeconds, MinPauseSeconds, MaxPauseSeconds);
            int cooldownSec = Math.Clamp(settings.ChangeChannelCooldownSeconds, MinCooldownSeconds, MaxCooldownSeconds);

            IsAvoiding = true;
            _avoidUntilUtc = nowUtc.AddSeconds(pauseSec);
            _nextTriggerUtc = nowUtc.AddSeconds(cooldownSec);
            _lastPulseUtc = nowUtc;
            _lastPulseLabel = "遇人換頻中";
            return true;
        }

        public string? GetStatusHint(DateTime nowUtc)
        {
            if (IsAvoiding)
                return string.IsNullOrEmpty(_lastPulseLabel) ? "遇人退避中" : _lastPulseLabel;

            if ((nowUtc - _lastPulseUtc).TotalMilliseconds < PulseHoldMs
                && !string.IsNullOrEmpty(_lastPulseLabel))
                return _lastPulseLabel;

            return null;
        }

        public void SetPulse(string label)
        {
            _lastPulseUtc = DateTime.UtcNow;
            _lastPulseLabel = label;
        }

        private void ClearAvoidance()
        {
            IsAvoiding = false;
            _avoidUntilUtc = DateTime.MinValue;
        }

        private void ClearAvoidanceEpisode()
        {
            IsAvoiding = false;
            _avoidUntilUtc = DateTime.MinValue;
            _lastPulseUtc = DateTime.UtcNow;
            _lastPulseLabel = "遇人退避結束";
        }
    }
}
