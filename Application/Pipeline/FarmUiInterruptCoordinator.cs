using ArtaleAI.Application.Movement;
using ArtaleAI.Domain.Input;
using ArtaleAI.Models.Config;
using ArtaleAI.Shared;
using ArtaleAI.Vision;
using OpenCvSharp;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>
    /// 自動打怪開啟時處理突發視窗（與「遇人換頻」無關）。
    /// 兩條觸發：
    /// 1) 小地圖連續消失 → Esc → 暗色 X；
    /// 2) 小地圖橙色 ROI 內探針模板連續命中 → Esc → 最新幀驗證消失。
    /// 換頻／隊伍建隊危險階段不插手。
    /// 清窗期間透過 InputLease 獨佔鍵盤（可強佔 Combat）。
    /// </summary>
    public sealed class FarmUiInterruptCoordinator : IDisposable
    {
        private const ushort VkEscape = 0x1B;
        private const int PostEscapeSettleMs = 450;

        private readonly InputLease _inputLease;
        private readonly Action _onCombatPreempted;
        private readonly UiInterruptDismisser _dismisser = new();
        private readonly object _probeLock = new();
        private readonly List<(string Path, Mat Template)> _probeTemplates = [];
        private string _probeFingerprint = string.Empty;

        private DateTime _minimapMissingSinceUtc = DateTime.MinValue;
        private DateTime _lastAttemptUtc = DateTime.MinValue;
        private DateTime _lastProbeScanUtc = DateTime.MinValue;
        private int _probeHitStreak;
        private int _dismissInFlight;

        public FarmUiInterruptCoordinator(InputLease inputLease, Action onCombatPreempted)
        {
            _inputLease = inputLease ?? throw new ArgumentNullException(nameof(inputLease));
            _onCombatPreempted = onCombatPreempted
                ?? throw new ArgumentNullException(nameof(onCombatPreempted));
        }

        public bool IsDismissing => Volatile.Read(ref _dismissInFlight) != 0;

        public void ObserveFrame(
            Mat frameMat,
            bool hasMinimap,
            bool autoFarmActive,
            bool uiSequenceBusy,
            AutoFarmSettings settings,
            VisionSettings vision,
            CharacterMovementController? movement,
            Func<Mat?> getLatestFrame,
            Action<string>? status)
        {
            ArgumentNullException.ThrowIfNull(frameMat);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(vision);
            ArgumentNullException.ThrowIfNull(getLatestFrame);

            // uiSequenceBusy：換頻中，或隊伍重建「開窗／點新建」危險階段。
            if (!autoFarmActive
                || uiSequenceBusy
                || movement == null
                || !settings.InterruptDismissEnabled
                || !settings.InterruptDismissDuringAutoFarm)
            {
                ResetObservationState();
                return;
            }

            DateTime now = DateTime.UtcNow;

            // 探針路徑優先：即使小地圖仍在，也能關閉中央遮罩式視窗（如圖鑑）。
            if (TryObserveEscapeProbe(
                    frameMat, settings, vision, movement, getLatestFrame, status, now))
                return;

            if (hasMinimap)
            {
                _minimapMissingSinceUtc = DateTime.MinValue;
                return;
            }

            if (_minimapMissingSinceUtc == DateTime.MinValue)
                _minimapMissingSinceUtc = now;

            int lostMs = Math.Clamp(settings.FarmInterruptMinimapLostMs, 500, 15000);
            if ((now - _minimapMissingSinceUtc).TotalMilliseconds < lostMs)
                return;

            TryStartMinimapLostDismiss(frameMat, settings, movement, status, now);
        }

        /// <summary>
        /// 在橙色小地圖 ROI 內匹配 Esc 觸發模板；連續命中達標才停手＋Esc＋最新幀驗證。
        /// </summary>
        private bool TryObserveEscapeProbe(
            Mat frameMat,
            AutoFarmSettings settings,
            VisionSettings vision,
            CharacterMovementController movement,
            Func<Mat?> getLatestFrame,
            Action<string>? status,
            DateTime now)
        {
            settings.InterruptEscapeTriggerTemplates ??= [];
            if (settings.InterruptEscapeTriggerTemplates.Count == 0)
            {
                _probeHitStreak = 0;
                return false;
            }

            int intervalMs = Math.Clamp(settings.InterruptProbeIntervalMs, 100, 5000);
            if ((now - _lastProbeScanUtc).TotalMilliseconds < intervalMs)
                return false;

            _lastProbeScanUtc = now;

            int cooldownMs = Math.Clamp(settings.FarmInterruptCooldownMs, 800, 30000);
            if ((now - _lastAttemptUtc).TotalMilliseconds < cooldownMs)
                return false;

            if (Volatile.Read(ref _dismissInFlight) != 0)
                return false;

            EnsureProbeTemplates(settings);
            double threshold = Math.Clamp(settings.InterruptProbeThreshold, 0.55, 0.98);
            if (!TryMatchProbeInMinimapRoi(frameMat, vision, threshold, out string hitName, out double score))
            {
                _probeHitStreak = 0;
                return false;
            }

            _probeHitStreak++;
            int requiredHits = Math.Clamp(settings.InterruptProbeRequiredHits, 1, 5);
            if (_probeHitStreak < requiredHits)
            {
                Logger.Debug(
                    $"[打怪中斷] 探針命中 {hitName} score={score:F3} streak={_probeHitStreak}/{requiredHits}");
                return false;
            }

            if (!_inputLease.TryAcquirePreemptingCombat(InputOwner.FarmDismiss, _onCombatPreempted))
                return false;

            if (Interlocked.CompareExchange(ref _dismissInFlight, 1, 0) != 0)
            {
                _inputLease.Release(InputOwner.FarmDismiss);
                return false;
            }

            _probeHitStreak = 0;
            _lastAttemptUtc = now;
            CharacterMovementController movementRef = movement;
            string probeName = hitName;

            _ = Task.Run(async () =>
            {
                try
                {
                    movementRef.StopMovement();
                    status?.Invoke($"打怪：偵測到突發視窗「{probeName}」，按 Esc…");

                    movementRef.FocusGameWindow();
                    await movementRef.TapKeyAsync(VkEscape, pressDurationMs: 100, intervalMs: 40, CancellationToken.None)
                        .ConfigureAwait(false);
                    await Task.Delay(PostEscapeSettleMs, CancellationToken.None).ConfigureAwait(false);

                    // 必須用 Esc 後的最新幀驗證；舊幀會永遠顯示「仍存在」。
                    using Mat? latest = getLatestFrame();
                    double verifyScore = 0;
                    bool stillPresent = latest != null
                        && !latest.Empty()
                        && TryMatchProbeInMinimapRoi(
                            latest, vision, threshold, out _, out verifyScore);

                    if (!stillPresent)
                    {
                        status?.Invoke($"打怪：突發視窗「{probeName}」已關閉");
                        Logger.Info($"[打怪中斷] Esc 後探針消失: {probeName}");
                        return;
                    }

                    status?.Invoke($"打怪：Esc 後「{probeName}」仍在（{verifyScore:F2}），再試一次…");
                    await movementRef.TapKeyAsync(VkEscape, pressDurationMs: 100, intervalMs: 40, CancellationToken.None)
                        .ConfigureAwait(false);
                    await Task.Delay(PostEscapeSettleMs, CancellationToken.None).ConfigureAwait(false);

                    using Mat? latest2 = getLatestFrame();
                    bool stillPresent2 = latest2 != null
                        && !latest2.Empty()
                        && TryMatchProbeInMinimapRoi(latest2, vision, threshold, out _, out _);

                    status?.Invoke(
                        stillPresent2
                            ? $"打怪：突發視窗「{probeName}」未能關閉"
                            : $"打怪：突發視窗「{probeName}」已關閉");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[打怪中斷] Esc 探針關閉例外: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _dismissInFlight, 0);
                    _inputLease.Release(InputOwner.FarmDismiss);
                }
            });

            return true;
        }

        private void TryStartMinimapLostDismiss(
            Mat frameMat,
            AutoFarmSettings settings,
            CharacterMovementController movement,
            Action<string>? status,
            DateTime now)
        {
            int cooldownMs = Math.Clamp(settings.FarmInterruptCooldownMs, 800, 30000);
            if ((now - _lastAttemptUtc).TotalMilliseconds < cooldownMs)
                return;

            if (!_inputLease.TryAcquirePreemptingCombat(InputOwner.FarmDismiss, _onCombatPreempted))
                return;

            if (Interlocked.CompareExchange(ref _dismissInFlight, 1, 0) != 0)
            {
                _inputLease.Release(InputOwner.FarmDismiss);
                return;
            }

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
                    _inputLease.Release(InputOwner.FarmDismiss);
                }
            });
        }

        private bool TryMatchProbeInMinimapRoi(
            Mat frameMat,
            VisionSettings vision,
            double threshold,
            out string hitName,
            out double bestScore)
        {
            hitName = string.Empty;
            bestScore = 0;

            SdRect searchRect = GameVisionCore.ResolveMinimapSearchRect(
                frameMat.Width, frameMat.Height, vision);
            if (searchRect.Width < 8 || searchRect.Height < 8)
                return false;

            using var roi = new Mat(
                frameMat,
                new Rect(searchRect.X, searchRect.Y, searchRect.Width, searchRect.Height));

            lock (_probeLock)
            {
                foreach (var (path, template) in _probeTemplates)
                {
                    if (template.Empty()
                        || template.Width > roi.Width
                        || template.Height > roi.Height)
                        continue;

                    var peek = GameVisionCore.PeekBestMatch(roi, template);
                    if (!peek.HasValue || peek.Value.MaxValue < threshold)
                        continue;

                    if (peek.Value.MaxValue <= bestScore)
                        continue;

                    bestScore = peek.Value.MaxValue;
                    hitName = Path.GetFileNameWithoutExtension(path);
                }
            }

            return bestScore >= threshold && !string.IsNullOrEmpty(hitName);
        }

        private void EnsureProbeTemplates(AutoFarmSettings settings)
        {
            settings.InterruptEscapeTriggerTemplates ??= [];
            string fingerprint = string.Join("|", settings.InterruptEscapeTriggerTemplates);
            lock (_probeLock)
            {
                if (string.Equals(_probeFingerprint, fingerprint, StringComparison.Ordinal)
                    && _probeTemplates.Count > 0)
                    return;

                ClearProbeTemplates_NoLock();
                _probeFingerprint = fingerprint;

                foreach (string relative in settings.InterruptEscapeTriggerTemplates)
                {
                    string path = relative?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (!Path.IsPathRooted(path))
                        path = Path.Combine(PathManager.ContentRoot, path);

                    if (!File.Exists(path))
                    {
                        Logger.Warning($"[打怪中斷] 探針模板不存在: {path}");
                        continue;
                    }

                    Mat loaded = Cv2.ImRead(path, ImreadModes.Color);
                    if (loaded.Empty())
                    {
                        loaded.Dispose();
                        Logger.Warning($"[打怪中斷] 無法讀取探針模板: {path}");
                        continue;
                    }

                    _probeTemplates.Add((path, loaded));
                    Logger.Info($"[打怪中斷] 已載入 Esc 探針: {path} ({loaded.Width}x{loaded.Height})");
                }
            }
        }

        private void ClearProbeTemplates_NoLock()
        {
            foreach (var (_, mat) in _probeTemplates)
                mat.Dispose();
            _probeTemplates.Clear();
        }

        private void ResetObservationState()
        {
            _minimapMissingSinceUtc = DateTime.MinValue;
            _probeHitStreak = 0;
        }

        public void Dispose()
        {
            lock (_probeLock)
            {
                ClearProbeTemplates_NoLock();
                _probeFingerprint = string.Empty;
            }

            _dismisser.Dispose();
        }
    }
}
