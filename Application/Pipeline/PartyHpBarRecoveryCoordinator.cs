using ArtaleAI.Application.Movement;
using ArtaleAI.Models.Config;
using ArtaleAI.Shared;
using ArtaleAI.Vision;
using OpenCvSharp;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>
    /// 隊伍血條守門：持續監測＋相位驅動狀態機（對齊換頻）。
    /// 主條件＝畫面相位（PartyUiScreenProbe）；按鍵／點擊僅為動作。
    /// OpenWindow → ClickCreate → CloseWindow(Esc) → AwaitBloodBar。
    /// </summary>
    public sealed class PartyHpBarRecoveryCoordinator : IDisposable
    {
        private enum FlowStep
        {
            OpenWindow = 0,
            ClickCreate = 1,
            CloseWindow = 2,
            AwaitBloodBar = 3,
            Done = 4
        }

        private enum StepRunResult
        {
            Advanced,
            RetrySameStep,
            Abort
        }

        private static readonly string[] StepNames =
        [
            "開隊伍視窗",
            "點新建",
            "關隊伍視窗",
            "等血條恢復"
        ];

        private const ushort VkEscape = 0x1B;
        private const int PartyWindowSettleMs = 700;
        private const int TemplatePollIntervalMs = 250;
        private const int PhaseWaitTimeoutMs = 5000;
        private const int PostClickSettleMs = 800;
        private const int AwaitBloodBarTimeoutMs = 8000;
        private const int RequiredStableHits = 2;
        private const int MaxClickRetries = 4;
        private const int PulseHoldMs = 2500;
        private const int RecentBloodBarMs = 2000;

        private readonly object _templateLock = new();
        private Mat? _createTemplate;
        private string _templatePathLoaded = string.Empty;
        private DateTime _lastAttemptUtc = DateTime.MinValue;
        private DateTime _lastPulseUtc = DateTime.MinValue;
        private string _lastPulseLabel = string.Empty;
        private int _recoveryInFlight;
        private int _currentStepOrdinal = -1;

        public bool IsRecovering => Volatile.Read(ref _recoveryInFlight) != 0;

        /// <summary>開窗／點新建危險階段：禁止突發清窗 Esc。</summary>
        public bool BlocksInterruptDismiss
        {
            get
            {
                if (!IsRecovering)
                    return false;

                int step = Volatile.Read(ref _currentStepOrdinal);
                return step is (int)FlowStep.OpenWindow or (int)FlowStep.ClickCreate;
            }
        }

        public void Observe(
            AutoFarmSettings settings,
            DateTime lastBloodBarSeenUtc,
            DateTime nowUtc,
            Func<Mat?> getLatestFrame,
            Func<DateTime> getLastBloodBarSeenUtc,
            Func<Mat, bool> hasMinimapOnFrame,
            CharacterMovementController movement,
            Action<string>? status)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(getLatestFrame);
            ArgumentNullException.ThrowIfNull(getLastBloodBarSeenUtc);
            ArgumentNullException.ThrowIfNull(hasMinimapOnFrame);
            ArgumentNullException.ThrowIfNull(movement);

            int lostMs = Math.Clamp(settings.PartyHpBarLostMs, 1000, 60000);
            if ((nowUtc - lastBloodBarSeenUtc).TotalMilliseconds < lostMs)
                return;

            int cooldownMs = Math.Clamp(settings.PartyRecoveryCooldownMs, 3000, 120000);
            if ((nowUtc - _lastAttemptUtc).TotalMilliseconds < cooldownMs)
                return;

            if (!VirtualKeyParser.TryParse(settings.PartyWindowHotkey, out ushort partyKey))
            {
                Logger.Warning($"[隊伍重建] 快捷鍵無法解析: {settings.PartyWindowHotkey}");
                return;
            }

            if (Interlocked.CompareExchange(ref _recoveryInFlight, 1, 0) != 0)
                return;

            _lastAttemptUtc = nowUtc;
            SetPulse("隊伍重建中");
            EnsureTemplate(settings);
            double threshold = Math.Clamp(settings.PartyCreateMatchThreshold, 0.5, 0.95);

            _ = Task.Run(async () =>
            {
                try
                {
                    bool ok = await ExecuteAsync(
                            getLatestFrame,
                            getLastBloodBarSeenUtc,
                            hasMinimapOnFrame,
                            movement,
                            partyKey,
                            threshold,
                            status)
                        .ConfigureAwait(false);
                    SetPulse(ok ? "隊伍重建完成" : "隊伍重建失敗，冷卻後重試");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[隊伍重建] 狀態機例外: {ex.Message}");
                    SetPulse("隊伍重建例外");
                }
                finally
                {
                    Volatile.Write(ref _currentStepOrdinal, -1);
                    Interlocked.Exchange(ref _recoveryInFlight, 0);
                }
            });
        }

        public string? GetStatusHint(DateTime nowUtc)
        {
            if (IsRecovering)
            {
                int step = Volatile.Read(ref _currentStepOrdinal);
                if (step >= 0 && step < StepNames.Length)
                    return $"隊伍重建：{StepNames[step]}";
                return string.IsNullOrEmpty(_lastPulseLabel) ? "隊伍重建中" : _lastPulseLabel;
            }

            if ((nowUtc - _lastPulseUtc).TotalMilliseconds < PulseHoldMs
                && !string.IsNullOrEmpty(_lastPulseLabel))
                return _lastPulseLabel;

            return null;
        }

        private async Task<bool> ExecuteAsync(
            Func<Mat?> getLatestFrame,
            Func<DateTime> getLastBloodBarSeenUtc,
            Func<Mat, bool> hasMinimapOnFrame,
            CharacterMovementController movement,
            ushort partyKey,
            double threshold,
            Action<string>? status)
        {
            Mat? template;
            lock (_templateLock)
                template = _createTemplate;

            if (template == null || template.Empty())
            {
                Notify(status, "隊伍重建：「新建」模板未載入");
                return false;
            }

            var ctx = new StepContext(
                getLatestFrame,
                getLastBloodBarSeenUtc,
                hasMinimapOnFrame,
                template,
                threshold,
                DateTime.UtcNow,
                status);

            movement.StopMovement();
            movement.FocusGameWindow();
            await Task.Delay(120).ConfigureAwait(false);

            // 啟動前先探測：已有血條則不必跑。
            var boot = ProbePhase(ctx);
            Notify(status, $"隊伍重建：啟動相位={boot.Phase}/{boot.AnchorName} {boot.BestScore:F2}");
            if (boot.Phase == PartyUiPhase.InGameWithParty)
            {
                Notify(status, "隊伍重建：相位已是有血條，略過");
                return true;
            }

            var step = FlowStep.OpenWindow;
            int clickRetries = 0;

            while (step < FlowStep.Done)
            {
                Volatile.Write(ref _currentStepOrdinal, (int)step);
                string stepName = StepNames[(int)step];
                Notify(status, $"隊伍重建：狀態「{stepName}」（重試 {clickRetries}/{MaxClickRetries}）");

                StepRunResult result = step switch
                {
                    FlowStep.OpenWindow => await TryOpenWindowAsync(ctx, movement, partyKey)
                        .ConfigureAwait(false),
                    FlowStep.ClickCreate => await TryClickCreateAsync(ctx, movement)
                        .ConfigureAwait(false),
                    FlowStep.CloseWindow => await TryCloseWindowAsync(ctx, movement)
                        .ConfigureAwait(false),
                    FlowStep.AwaitBloodBar => await TryAwaitBloodBarAsync(ctx)
                        .ConfigureAwait(false),
                    _ => StepRunResult.Abort
                };

                switch (result)
                {
                    case StepRunResult.Advanced:
                        Logger.Info($"[隊伍重建] 狀態「{stepName}」完成 → 下一狀態");
                        clickRetries = 0;
                        step++;
                        break;

                    case StepRunResult.RetrySameStep:
                        clickRetries++;
                        if (clickRetries >= MaxClickRetries)
                        {
                            Notify(status, $"隊伍重建失敗：狀態「{stepName}」重試耗盡");
                            return false;
                        }

                        Notify(status, $"隊伍重建：「{stepName}」相位未就緒，重試…");
                        await Task.Delay(500).ConfigureAwait(false);
                        break;

                    default:
                        Notify(status, $"隊伍重建失敗：狀態「{stepName}」中止");
                        return false;
                }
            }

            Notify(status, "隊伍重建：血條已恢復");
            return true;
        }

        /// <summary>相位已是 PartyPanel → 軟前進；InGameNoParty → 按 P 後等面板；已有血條 → 完成前進。</summary>
        private async Task<StepRunResult> TryOpenWindowAsync(
            StepContext ctx,
            CharacterMovementController movement,
            ushort partyKey)
        {
            var snap = ProbePhase(ctx);
            ctx.Status?.Invoke($"隊伍重建：開窗前相位={snap.Phase}/{snap.AnchorName} {snap.BestScore:F2}");

            if (snap.Phase == PartyUiPhase.InGameWithParty)
                return StepRunResult.Advanced;

            if (snap.Phase == PartyUiPhase.PartyPanel)
            {
                Notify(ctx.Status, "隊伍重建：相位＝隊伍面板，軟前進");
                return StepRunResult.Advanced;
            }

            if (snap.Phase == PartyUiPhase.Unknown)
            {
                Notify(ctx.Status, "隊伍重建：相位無錨點，等待畫面…");
                var waited = await WaitForPhaseAsync(
                        ctx,
                        p => p is PartyUiPhase.PartyPanel
                             or PartyUiPhase.InGameNoParty
                             or PartyUiPhase.InGameWithParty,
                        PhaseWaitTimeoutMs / 2)
                    .ConfigureAwait(false);
                if (waited == null)
                    return StepRunResult.RetrySameStep;
                snap = waited.Value;
                if (snap.Phase is PartyUiPhase.PartyPanel or PartyUiPhase.InGameWithParty)
                    return StepRunResult.Advanced;
            }

            Notify(ctx.Status, "隊伍重建：相位＝遊戲中無隊，按 P 開面板");
            await movement.TapKeyAsync(partyKey, 100, 40, CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(PartyWindowSettleMs).ConfigureAwait(false);

            var after = await WaitForPhaseAsync(
                    ctx,
                    p => p is PartyUiPhase.PartyPanel or PartyUiPhase.InGameWithParty,
                    PhaseWaitTimeoutMs)
                .ConfigureAwait(false);

            if (after != null)
            {
                Logger.Info($"[隊伍重建] 開窗後相位={after.Value.Phase} score={after.Value.BestScore:F3}");
                return StepRunResult.Advanced;
            }

            // P 是切換鍵：可能關了又開錯 → 依相位分流。
                return ResolveMissingTarget(ctx, FlowStep.OpenWindow);
        }

        private async Task<StepRunResult> TryClickCreateAsync(
            StepContext ctx,
            CharacterMovementController movement)
        {
            var snap = ProbePhase(ctx);
            ctx.Status?.Invoke($"隊伍重建：點新建前相位={snap.Phase}/{snap.AnchorName} {snap.BestScore:F2}");

            if (snap.Phase == PartyUiPhase.InGameWithParty)
            {
                Notify(ctx.Status, "隊伍重建：已有血條 → 軟前進");
                return StepRunResult.Advanced;
            }

            if (snap.Phase is PartyUiPhase.InGameNoParty)
            {
                // 面板已關但尚未有血條：可能已建隊 → 軟前進到關窗／等血條。
                Notify(ctx.Status, "隊伍重建：面板已關、尚無血條 → 軟前進");
                return StepRunResult.Advanced;
            }

            if (snap.Phase != PartyUiPhase.PartyPanel)
                return ResolveMissingTarget(ctx, FlowStep.ClickCreate);

            var hit = await WaitForStableCreateHitAsync(ctx).ConfigureAwait(false);
            if (hit == null)
                return ResolveMissingTarget(ctx, FlowStep.ClickCreate);

            // 點擊前再確認相位仍是面板。
            var recheck = ProbePhase(ctx);
            if (recheck.Phase != PartyUiPhase.PartyPanel)
            {
                Notify(ctx.Status, $"隊伍重建：點擊前相位變為 {recheck.Phase} → 依相位分流");
                return ResolveMissingTarget(ctx, FlowStep.ClickCreate);
            }

            Notify(ctx.Status, $"隊伍重建：點「新建」（信心 {hit.Value.Score:F2}）");
            bool clicked = await movement.ClickCapturePointAsync(
                    hit.Value.ClickX,
                    hit.Value.ClickY,
                    hit.Value.FrameWidth,
                    hit.Value.FrameHeight)
                .ConfigureAwait(false);

            if (!clicked)
            {
                Logger.Warning("[隊伍重建] 點「新建」滑鼠失敗");
                return StepRunResult.RetrySameStep;
            }

            Logger.Info(
                $"[隊伍重建] 已點「新建」@ ({hit.Value.ClickX},{hit.Value.ClickY}) score={hit.Value.Score:F3}");
            await Task.Delay(PostClickSettleMs).ConfigureAwait(false);

            // 點完：面板應離開 PartyPanel，或已見血條。
            var after = await WaitForPhaseAsync(
                    ctx,
                    p => p is PartyUiPhase.InGameNoParty or PartyUiPhase.InGameWithParty,
                    PhaseWaitTimeoutMs)
                .ConfigureAwait(false);

            if (after != null)
            {
                Notify(ctx.Status, $"隊伍重建：點擊後相位={after.Value.Phase}");
                return StepRunResult.Advanced;
            }

            // 仍停在面板：可能點空 → 重試。
            return StepRunResult.RetrySameStep;
        }

        private async Task<StepRunResult> TryCloseWindowAsync(
            StepContext ctx,
            CharacterMovementController movement)
        {
            var snap = ProbePhase(ctx);
            ctx.Status?.Invoke($"隊伍重建：關窗前相位={snap.Phase}/{snap.AnchorName} {snap.BestScore:F2}");

            if (snap.Phase is PartyUiPhase.InGameNoParty or PartyUiPhase.InGameWithParty)
            {
                Notify(ctx.Status, "隊伍重建：相位已離開面板，軟前進");
                return StepRunResult.Advanced;
            }

            if (snap.Phase != PartyUiPhase.PartyPanel)
                return ResolveMissingTarget(ctx, FlowStep.CloseWindow);

            Notify(ctx.Status, "隊伍重建：相位＝面板仍開，按 Esc 關閉");
            await movement.TapKeyAsync(VkEscape, 100, 40, CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(PartyWindowSettleMs).ConfigureAwait(false);

            var after = await WaitForPhaseAsync(
                    ctx,
                    p => p is PartyUiPhase.InGameNoParty or PartyUiPhase.InGameWithParty,
                    PhaseWaitTimeoutMs)
                .ConfigureAwait(false);

            if (after != null)
            {
                Notify(ctx.Status, $"隊伍重建：Esc 後相位={after.Value.Phase}");
                return StepRunResult.Advanced;
            }

            return StepRunResult.RetrySameStep;
        }

        private async Task<StepRunResult> TryAwaitBloodBarAsync(StepContext ctx)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(AwaitBloodBarTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var snap = ProbePhase(ctx);
                if (snap.Phase == PartyUiPhase.InGameWithParty)
                {
                    Notify(ctx.Status, "隊伍重建：相位＝已有隊伍血條");
                    return StepRunResult.Advanced;
                }

                // 又冒出面板＝關窗失敗，回退由外層重試耗盡處理；此處標 Abort 讓冷卻後 Idle 再來。
                if (snap.Phase == PartyUiPhase.PartyPanel)
                {
                    Notify(ctx.Status, "隊伍重建：等血條時面板又開了 → 中止本輪");
                    return StepRunResult.Abort;
                }

                Notify(ctx.Status, $"隊伍重建：等待血條…（相位={snap.Phase}/{snap.AnchorName}）");
                await Task.Delay(TemplatePollIntervalMs).ConfigureAwait(false);
            }

            Notify(ctx.Status, "隊伍重建：等待血條逾時，冷卻後繼續監測");
            return StepRunResult.Abort;
        }

        private StepRunResult ResolveMissingTarget(StepContext ctx, FlowStep step)
        {
            var snap = ProbePhase(ctx);
            var expected = PartyUiScreenProbe.ExpectedPhase((int)step);
            ctx.Status?.Invoke(
                $"隊伍重建：相位={snap.Phase}/{snap.AnchorName} {snap.BestScore:F2}（本步期待 {expected}）");

            if (PartyUiScreenProbe.IsAtOrBeyondGoal((int)step, snap.Phase))
            {
                Notify(ctx.Status, "隊伍重建：相位已超前 → 軟前進");
                return StepRunResult.Advanced;
            }

            if (snap.Phase == expected)
                return StepRunResult.RetrySameStep;

            if (snap.Phase == PartyUiPhase.Unknown)
            {
                Notify(ctx.Status, "隊伍重建：相位無錨點 → 同一步重試");
                return StepRunResult.RetrySameStep;
            }

            if (PartyUiScreenProbe.IsBehind(snap.Phase, expected))
            {
                Notify(ctx.Status, "隊伍重建：相位落後 → 同一步重試（不盲回退按鍵）");
                return StepRunResult.RetrySameStep;
            }

            return StepRunResult.RetrySameStep;
        }

        private PartyUiPhaseSnapshot ProbePhase(StepContext ctx)
        {
            Mat? frame = ctx.GetLatestFrame();
            try
            {
                if (frame == null || frame.Empty())
                    return new PartyUiPhaseSnapshot(PartyUiPhase.Unknown, 0, "no-frame");

                bool hasMinimap = false;
                try
                {
                    hasMinimap = ctx.HasMinimapOnFrame(frame);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[隊伍重建] 小地圖相位探測例外: {ex.Message}");
                }

                bool hasBloodBar = IsBloodBarFresh(ctx);
                return PartyUiScreenProbe.Probe(
                    frame,
                    ctx.Threshold,
                    ctx.CreateTemplate,
                    hasMinimap,
                    hasBloodBar);
            }
            finally
            {
                frame?.Dispose();
            }
        }

        private static bool IsBloodBarFresh(StepContext ctx)
        {
            DateTime lastSeen = ctx.GetLastBloodBarSeenUtc();
            if (lastSeen > ctx.SequenceStartedUtc)
                return true;

            // 啟動前若血條其實還在（誤觸發），也算有隊。
            return (DateTime.UtcNow - lastSeen).TotalMilliseconds < RecentBloodBarMs
                   && lastSeen != DateTime.MinValue;
        }

        private async Task<PartyUiPhaseSnapshot?> WaitForPhaseAsync(
            StepContext ctx,
            Func<PartyUiPhase, bool> predicate,
            int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var snap = ProbePhase(ctx);
                if (predicate(snap.Phase))
                    return snap;

                await Task.Delay(TemplatePollIntervalMs).ConfigureAwait(false);
            }

            return null;
        }

        private async Task<TemplateHit?> WaitForStableCreateHitAsync(StepContext ctx)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(PhaseWaitTimeoutMs);
            int streak = 0;
            TemplateHit? lastHit = null;
            double best = 0;

            while (DateTime.UtcNow < deadline)
            {
                Mat? frame = ctx.GetLatestFrame();
                try
                {
                    if (frame == null || frame.Empty() || ctx.CreateTemplate.Empty())
                    {
                        streak = 0;
                        await Task.Delay(TemplatePollIntervalMs).ConfigureAwait(false);
                        continue;
                    }

                    var peek = GameVisionCore.PeekBestMatch(frame, ctx.CreateTemplate);
                    if (!peek.HasValue || peek.Value.MaxValue < ctx.Threshold)
                    {
                        if (peek.HasValue)
                            best = Math.Max(best, peek.Value.MaxValue);
                        streak = 0;
                        await Task.Delay(TemplatePollIntervalMs).ConfigureAwait(false);
                        continue;
                    }

                    best = Math.Max(best, peek.Value.MaxValue);
                    lastHit = new TemplateHit(
                        peek.Value.Location.X + ctx.CreateTemplate.Width / 2,
                        peek.Value.Location.Y + ctx.CreateTemplate.Height / 2,
                        peek.Value.MaxValue,
                        frame.Width,
                        frame.Height);
                    streak++;
                    if (streak >= RequiredStableHits)
                        return lastHit;
                }
                finally
                {
                    frame?.Dispose();
                }

                await Task.Delay(TemplatePollIntervalMs).ConfigureAwait(false);
            }

            Logger.Warning($"[隊伍重建] 等「新建」穩定逾時，最佳={best:F3}");
            return null;
        }

        private void Notify(Action<string>? status, string message)
        {
            SetPulse(message);
            status?.Invoke(message);
        }

        private void SetPulse(string label)
        {
            _lastPulseUtc = DateTime.UtcNow;
            _lastPulseLabel = label;
        }

        private void EnsureTemplate(AutoFarmSettings settings)
        {
            string relative = settings.PartyCreateTemplate?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(relative))
                return;

            string path = Path.IsPathRooted(relative)
                ? relative
                : Path.Combine(PathManager.ContentRoot, relative);

            lock (_templateLock)
            {
                if (string.Equals(_templatePathLoaded, path, StringComparison.OrdinalIgnoreCase)
                    && _createTemplate != null
                    && !_createTemplate.Empty())
                    return;

                _createTemplate?.Dispose();
                _createTemplate = null;
                _templatePathLoaded = path;

                if (!File.Exists(path))
                {
                    Logger.Warning($"[隊伍重建] 模板不存在: {path}");
                    return;
                }

                Mat loaded = Cv2.ImRead(path, ImreadModes.Color);
                if (loaded.Empty())
                {
                    loaded.Dispose();
                    Logger.Warning($"[隊伍重建] 無法讀取模板: {path}");
                    return;
                }

                _createTemplate = loaded;
                Logger.Info($"[隊伍重建] 已載入「新建」模板: {path} ({loaded.Width}x{loaded.Height})");
            }
        }

        public void Dispose()
        {
            lock (_templateLock)
            {
                _createTemplate?.Dispose();
                _createTemplate = null;
                _templatePathLoaded = string.Empty;
            }
        }

        private sealed class StepContext
        {
            public StepContext(
                Func<Mat?> getLatestFrame,
                Func<DateTime> getLastBloodBarSeenUtc,
                Func<Mat, bool> hasMinimapOnFrame,
                Mat createTemplate,
                double threshold,
                DateTime sequenceStartedUtc,
                Action<string>? status)
            {
                GetLatestFrame = getLatestFrame;
                GetLastBloodBarSeenUtc = getLastBloodBarSeenUtc;
                HasMinimapOnFrame = hasMinimapOnFrame;
                CreateTemplate = createTemplate;
                Threshold = threshold;
                SequenceStartedUtc = sequenceStartedUtc;
                Status = status;
            }

            public Func<Mat?> GetLatestFrame { get; }
            public Func<DateTime> GetLastBloodBarSeenUtc { get; }
            public Func<Mat, bool> HasMinimapOnFrame { get; }
            public Mat CreateTemplate { get; }
            public double Threshold { get; }
            public DateTime SequenceStartedUtc { get; }
            public Action<string>? Status { get; }
        }

        private readonly record struct TemplateHit(
            int ClickX,
            int ClickY,
            double Score,
            int FrameWidth,
            int FrameHeight);
    }
}
