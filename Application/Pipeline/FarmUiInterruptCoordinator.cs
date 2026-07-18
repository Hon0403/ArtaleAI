using ArtaleAI.Application.Movement;
using ArtaleAI.Models.Config;
using ArtaleAI.Shared;
using OpenCvSharp;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>
    /// 自動打怪開啟時處理突發視窗（與「遇人換頻」無關）。
    /// 小地圖連續消失 → Esc → 暗色 X；換頻進行中／隊伍建隊危險階段不插手。
    /// </summary>
    public sealed class FarmUiInterruptCoordinator : IDisposable
    {
        private readonly UiInterruptDismisser _dismisser = new();
        private DateTime _minimapMissingSinceUtc = DateTime.MinValue;
        private DateTime _lastAttemptUtc = DateTime.MinValue;
        private int _dismissInFlight;

        public bool IsDismissing => Volatile.Read(ref _dismissInFlight) != 0;

        public void ObserveFrame(
            Mat frameMat,
            bool hasMinimap,
            bool autoFarmActive,
            bool uiSequenceBusy,
            AutoFarmSettings settings,
            CharacterMovementController? movement,
            Action<string>? status)
        {
            ArgumentNullException.ThrowIfNull(frameMat);
            ArgumentNullException.ThrowIfNull(settings);

            // uiSequenceBusy：換頻中，或隊伍重建「開窗／點新建」危險階段。
            if (!autoFarmActive
                || uiSequenceBusy
                || movement == null
                || !settings.InterruptDismissEnabled
                || !settings.InterruptDismissDuringAutoFarm)
            {
                _minimapMissingSinceUtc = DateTime.MinValue;
                return;
            }

            if (hasMinimap)
            {
                _minimapMissingSinceUtc = DateTime.MinValue;
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (_minimapMissingSinceUtc == DateTime.MinValue)
                _minimapMissingSinceUtc = now;

            int lostMs = Math.Clamp(settings.FarmInterruptMinimapLostMs, 500, 15000);
            if ((now - _minimapMissingSinceUtc).TotalMilliseconds < lostMs)
                return;

            int cooldownMs = Math.Clamp(settings.FarmInterruptCooldownMs, 800, 30000);
            if ((now - _lastAttemptUtc).TotalMilliseconds < cooldownMs)
                return;

            if (Interlocked.CompareExchange(ref _dismissInFlight, 1, 0) != 0)
                return;

            _lastAttemptUtc = now;
            Mat clone = frameMat.Clone();
            CharacterMovementController movementRef = movement;
            bool allowEscape = settings.InterruptPreferEscape;

            _ = Task.Run(async () =>
            {
                try
                {
                    movementRef.StopMovement();
                    status?.Invoke("打怪：小地圖消失，處理突發介面…");

                    int dismissed = await _dismisser.TryDismissAsync(
                            settings,
                            () => clone.Empty() ? null : clone.Clone(),
                            async (x, y, w, h, token) =>
                                await movementRef.ClickCapturePointAsync(x, y, w, h, token)
                                    .ConfigureAwait(false),
                            async (vk, token) =>
                            {
                                movementRef.FocusGameWindow();
                                await movementRef.TapKeyAsync(vk, pressDurationMs: 100, intervalMs: 40, token)
                                    .ConfigureAwait(false);
                            },
                            allowEscape,
                            allowTemplateDismiss: true,
                            status,
                            CancellationToken.None,
                            maxDismissals: 2,
                            preferDarkOverlayFirst: true)
                        .ConfigureAwait(false);

                    if (dismissed > 0)
                        status?.Invoke($"打怪：已關閉突發介面（{dismissed}）");
                    else
                        status?.Invoke("打怪：Esc／暗色關閉鈕皆未成功");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[打怪中斷] 關閉突發視窗例外: {ex.Message}");
                }
                finally
                {
                    clone.Dispose();
                    Interlocked.Exchange(ref _dismissInFlight, 0);
                }
            });
        }

        public void Dispose() => _dismisser.Dispose();
    }
}
