using System.Drawing;
using ArtaleAI.Application.Pipeline.ChannelPick;
using ArtaleAI.Models.Config;
using ArtaleAI.Shared;
using ArtaleAI.Vision;
using OpenCvSharp;

namespace ArtaleAI.Application.Pipeline
{
        /// <summary>
        /// 遇人換頻狀態機：畫面相位為主條件，模板點擊為動作，清彈窗僅次條件。
        /// Identify → Click → AwaitLoad；找不到時依相位分流（前進／重試／回退／清窗）。
        /// </summary>
        public sealed class ChangeChannelSequence : IDisposable
        {
        private enum FlowStep
        {
            OpenMenu = 0,
            ClickChannel = 1,
            SelectCell = 2,
            ClickConfirm = 3,
            ClickLogin = 4,
            ClickSelectCharacter = 5,
            Done = 6
        }

        /// <summary>單次執行結果：把「讀取中」與「該回退」拆開。</summary>
        private enum StepRunResult
        {
            Advanced,
            RetrySameStep,
            Rollback
        }

        /// <summary>目標模板未命中時，依畫面相位決定下一步。</summary>
        private enum ObserveVerdict
        {
            SoftAdvance,
            Retry,
            Rollback,
            DismissThenRetry
        }

        private static readonly string[] StepNames =
        [
            "開選單",
            "點頻道",
            "選格",
            "點確定",
            "點登入",
            "點選擇角色"
        ];

        private const ushort VkEscape = 0x1B;
        private const int PostEscapeMinSettleMs = 800;
        private const int TemplatePollIntervalMs = 250;
        private const int ExtraSettleAfterFoundMs = 400;
        private const int AfterClickSettleMs = 800;
        private const int PostConfirmSettleMs = 2000;
        private const int PostLoginSettleMs = 1500;
        private const int MaxClickRetries = 4;
        private const int MaxRollbacks = 8;
        private const int RequiredStableHits = 3;

        private const int FindTimeoutMs = 8000;
        private const int CellFindTimeoutMs = 8000;
        private const int ConfirmFindTimeoutMs = 8000;
        private const int LoginFindTimeoutMs = 20000;
        private const int SelectCharacterFindTimeoutMs = 20000;

        /// <summary>點完後等下一畫面：只算「讀取時間」，不當作重試失敗。</summary>
        private const int PatientLoadTimeoutMs = 60000;
        private const int ShortTransitionTimeoutMs = 12000;

        private readonly UiInterruptDismisser _interruptDismisser = new();
        private AutoFarmSettings? _activeSettings;
        private Func<ushort, CancellationToken, Task>? _tapKeyAsync;
        private readonly object _templateLock = new();
        private Mat? _menuTemplate;
        private Mat? _pickTemplate;
        private Mat? _confirmTemplate;
        private Mat? _loginTemplate;
        private Mat? _selectCharacterTemplate;
        private string _menuPathLoaded = string.Empty;
        private string _pickPathLoaded = string.Empty;
        private string _confirmPathLoaded = string.Empty;
        private string _loginPathLoaded = string.Empty;
        private string _selectCharacterPathLoaded = string.Empty;

        public async Task<bool> ExecuteAsync(
            AutoFarmSettings settings,
            Func<Mat?> getLatestFrame,
            Action focusWindow,
            Func<ushort, CancellationToken, Task> tapKeyAsync,
            Func<int, int, int, int, CancellationToken, Task<bool>> clickCapturePointAsync,
            Action<string>? status,
            Func<Mat, bool>? hasMinimapOnFrame = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(getLatestFrame);
            ArgumentNullException.ThrowIfNull(tapKeyAsync);
            ArgumentNullException.ThrowIfNull(clickCapturePointAsync);

            EnsureTemplates(settings);
            settings.ChangeChannelGrid ??= new ChannelPickGridSettings();
            settings.ChangeChannelGrid.Clamp();
            settings.InterruptDismissTemplates ??= [];
            _activeSettings = settings;
            _tapKeyAsync = tapKeyAsync;

            var ctx = new StepContext(
                getLatestFrame,
                clickCapturePointAsync,
                status,
                Math.Clamp(settings.ChangeChannelMatchThreshold, 0.5, 0.95),
                SoftThreshold(Math.Clamp(settings.ChangeChannelMatchThreshold, 0.5, 0.95)),
                hasMinimapOnFrame,
                ct);

            focusWindow();
            await Task.Delay(120, ct).ConfigureAwait(false);

            var step = FlowStep.OpenMenu;
            int clickRetries = 0;
            int rollbacks = 0;

            while (step < FlowStep.Done)
            {
                ct.ThrowIfCancellationRequested();
                ctx.CurrentStep = step;
                ctx.AllowTemplateDismiss = AllowsTemplateInterruptWhileWaiting(step);

                string stepName = StepNames[(int)step];
                status?.Invoke(
                    $"換頻：狀態「{stepName}」（點擊重試 {clickRetries}/{MaxClickRetries}，回退 {rollbacks}/{MaxRollbacks}）");

                StepRunResult result = await RunOneAttemptAsync(step, ctx, settings, tapKeyAsync)
                    .ConfigureAwait(false);

                switch (result)
                {
                    case StepRunResult.Advanced:
                        Logger.Info($"[換頻] 狀態「{stepName}」完成 → 下一狀態");
                        clickRetries = 0;
                        step++;
                        break;

                    case StepRunResult.RetrySameStep:
                        clickRetries++;
                        if (clickRetries < MaxClickRetries)
                        {
                            status?.Invoke($"換頻：「{stepName}」點擊可能無效，先辨識本步目標再重試…");
                            break;
                        }

                        // 同一步點擊多次仍無效 → 回退
                        result = StepRunResult.Rollback;
                        goto case StepRunResult.Rollback;

                    case StepRunResult.Rollback:
                        if (step == FlowStep.OpenMenu || rollbacks >= MaxRollbacks)
                        {
                            status?.Invoke($"換頻失敗：狀態「{stepName}」無法完成");
                            return false;
                        }

                        rollbacks++;
                        clickRetries = 0;
                        step--;
                        status?.Invoke(
                            $"換頻：回退到上一狀態「{StepNames[(int)step]}」（回退 {rollbacks}/{MaxRollbacks}）");
                        Logger.Warning($"[換頻] 回退 → {StepNames[(int)step]}");
                        await Task.Delay(700, ct).ConfigureAwait(false);
                        break;
                }
            }

            status?.Invoke("換頻：已離開選角並完成換頻序列");
            return true;
        }

        private async Task<StepRunResult> RunOneAttemptAsync(
            FlowStep step,
            StepContext ctx,
            AutoFarmSettings settings,
            Func<ushort, CancellationToken, Task> tapKeyAsync)
        {
            return step switch
            {
                FlowStep.OpenMenu => await TryOpenMenuOnceAsync(ctx, tapKeyAsync).ConfigureAwait(false),
                FlowStep.ClickChannel => await TryClickThenPatientAwaitAsync(
                    ctx,
                    () => _menuTemplate,
                    () => _pickTemplate,
                    "頻道",
                    "頻道列表",
                    FindTimeoutMs,
                    ShortTransitionTimeoutMs).ConfigureAwait(false),
                FlowStep.SelectCell => await TrySelectCellOnceAsync(ctx, settings).ConfigureAwait(false),
                FlowStep.ClickConfirm => await TryConfirmOnceAsync(ctx).ConfigureAwait(false),
                FlowStep.ClickLogin => await TryClickThenPatientAwaitAsync(
                    ctx,
                    () => _loginTemplate,
                    () => _selectCharacterTemplate,
                    "登入",
                    "選擇角色",
                    LoginFindTimeoutMs,
                    PatientLoadTimeoutMs,
                    requireStableCurrent: true,
                    preClickSettleMs: PostLoginSettleMs).ConfigureAwait(false),
                FlowStep.ClickSelectCharacter => await TrySelectCharacterOnceAsync(ctx).ConfigureAwait(false),
                _ => StepRunResult.Rollback
            };
        }

        /// <summary>
        /// 點「選擇角色」後必須確認按鈕消失（已離開選角）；
        /// 僅點一下就宣告成功會卡在選角畫面空等。
        /// </summary>
        private async Task<StepRunResult> TrySelectCharacterOnceAsync(StepContext ctx)
        {
            bool clicked = await TryIdentifyAndClickAsync(
                    ctx,
                    () => _selectCharacterTemplate,
                    "選擇角色",
                    SelectCharacterFindTimeoutMs,
                    ctx.Threshold,
                    useGrayscale: false,
                    requireStable: true)
                .ConfigureAwait(false);

            if (!clicked)
                return await ResolveMissingTargetAsync(ctx).ConfigureAwait(false);

            ctx.Status?.Invoke("換頻：已點「選擇角色」，等待離開選角畫面（最長 60s）…");
            await Task.Delay(AfterClickSettleMs, ctx.Ct).ConfigureAwait(false);

            bool leftScreen = await WaitForTemplateGoneAsync(
                    () => _selectCharacterTemplate,
                    ctx.Threshold,
                    ctx,
                    "選擇角色",
                    PatientLoadTimeoutMs,
                    useGrayscale: false)
                .ConfigureAwait(false);

            if (!leftScreen)
            {
                var snap = ProbePhase(ctx);
                if (snap.Phase == ChangeChannelPhase.InGame)
                {
                    ctx.Status?.Invoke("換頻：相位＝遊戲中（小地圖），視為已離開選角");
                    leftScreen = true;
                }
            }

            if (leftScreen)
            {
                ctx.Status?.Invoke("換頻：已離開選角畫面，等待進入遊戲");
                await Task.Delay(PostLoginSettleMs, ctx.Ct).ConfigureAwait(false);
                return StepRunResult.Advanced;
            }

            // 按鈕仍在：依相位決定重試／清窗，避免盲回退。
            return await ResolveMissingTargetAsync(ctx).ConfigureAwait(false);
        }

        private async Task<StepRunResult> TryOpenMenuOnceAsync(
            StepContext ctx,
            Func<ushort, CancellationToken, Task> tapKeyAsync)
        {
            var already = await WaitForStableTemplateAsync(
                    () => _menuTemplate,
                    ctx.Threshold,
                    ctx,
                    "頻道",
                    findTimeoutMs: 1200,
                    useGrayscale: false)
                .ConfigureAwait(false);
            if (already != null)
                return StepRunResult.Advanced;

            ctx.Status?.Invoke("換頻：未見到「頻道」，按 Esc 後再辨識");
            await tapKeyAsync(VkEscape, ctx.Ct).ConfigureAwait(false);
            await Task.Delay(PostEscapeMinSettleMs, ctx.Ct).ConfigureAwait(false);

            var afterEsc = await WaitForStableTemplateAsync(
                    () => _menuTemplate,
                    ctx.Threshold,
                    ctx,
                    "頻道",
                    FindTimeoutMs,
                    useGrayscale: false)
                .ConfigureAwait(false);

            return afterEsc != null ? StepRunResult.Advanced : StepRunResult.RetrySameStep;
        }

        private async Task<StepRunResult> TryConfirmOnceAsync(StepContext ctx)
        {
            // 確定 → 登入：讀取常很慢，用長等待，勿急著回退。
            var result = await TryClickThenPatientAwaitAsync(
                    ctx,
                    () => _confirmTemplate,
                    () => _loginTemplate,
                    "確定",
                    "登入",
                    ConfirmFindTimeoutMs,
                    PatientLoadTimeoutMs,
                    useGrayscaleCurrent: true,
                    useSoftThresholdCurrent: true,
                    requireStableCurrent: true,
                    preClickSettleMs: 500)
                .ConfigureAwait(false);

            if (result != StepRunResult.Advanced)
                return result;

            ctx.Status?.Invoke("換頻：已到登入階段，稍候畫面穩定…");
            await Task.Delay(PostConfirmSettleMs, ctx.Ct).ConfigureAwait(false);
            return StepRunResult.Advanced;
        }

        /// <summary>
        /// 先辨識並點 current → 長時間耐心等 next（讀取中）；
        /// next 出現＝前進；next 沒來但 current 仍在＝同一步重點；current 也沒了＝回退。
        /// </summary>
        private async Task<StepRunResult> TryClickThenPatientAwaitAsync(
            StepContext ctx,
            Func<Mat?> getCurrent,
            Func<Mat?> getNext,
            string currentLabel,
            string nextLabel,
            int findCurrentTimeoutMs,
            int patientLoadTimeoutMs,
            bool useGrayscaleCurrent = false,
            bool useGrayscaleNext = false,
            bool useSoftThresholdCurrent = false,
            bool requireStableCurrent = true,
            int preClickSettleMs = 0)
        {
            double currentThreshold = useSoftThresholdCurrent ? ctx.SoftThreshold : ctx.Threshold;

            if (preClickSettleMs > 0)
                await Task.Delay(preClickSettleMs, ctx.Ct).ConfigureAwait(false);

            if (!await TryIdentifyAndClickAsync(
                    ctx,
                    getCurrent,
                    currentLabel,
                    findCurrentTimeoutMs,
                    currentThreshold,
                    useGrayscaleCurrent,
                    requireStableCurrent).ConfigureAwait(false))
            {
                return await ResolveMissingTargetAsync(ctx).ConfigureAwait(false);
            }

            await Task.Delay(AfterClickSettleMs, ctx.Ct).ConfigureAwait(false);

            ctx.Status?.Invoke($"換頻：已點「{currentLabel}」，等待「{nextLabel}」讀取中（最長 {patientLoadTimeoutMs / 1000}s）…");
            var nextHit = await WaitForStableTemplateAsync(
                    getNext,
                    ctx.Threshold,
                    ctx,
                    nextLabel,
                    patientLoadTimeoutMs,
                    useGrayscaleNext)
                .ConfigureAwait(false);

            if (nextHit != null)
            {
                Logger.Info($"[換頻] 「{currentLabel}」→「{nextLabel}」就緒 score={nextHit.Value.Score:F3}");
                return StepRunResult.Advanced;
            }

            return await ResolvePatientTimeoutAsync(ctx, currentLabel, nextLabel).ConfigureAwait(false);
        }

        private async Task<StepRunResult> TrySelectCellOnceAsync(StepContext ctx, AutoFarmSettings settings)
        {
            if (!await TryClickOneChannelCellAsync(ctx, settings).ConfigureAwait(false))
                return StepRunResult.Rollback;

            await Task.Delay(AfterClickSettleMs, ctx.Ct).ConfigureAwait(false);

            ctx.Status?.Invoke("換頻：已選格，等待「確定」就緒…");
            var confirmHit = await WaitForStableTemplateAsync(
                    () => _confirmTemplate,
                    ctx.SoftThreshold,
                    ctx,
                    "確定",
                    ShortTransitionTimeoutMs,
                    useGrayscale: true)
                .ConfigureAwait(false);

            if (confirmHit != null)
            {
                Logger.Info($"[換頻] 選格成功，確定就緒 score={confirmHit.Value.Score:F3}");
                return StepRunResult.Advanced;
            }

            // 列表面板多半還在 → 換一格重試，不算回退。
            var panelStill = await WaitForStableTemplateAsync(
                    () => _pickTemplate,
                    ctx.Threshold,
                    ctx,
                    "頻道列表",
                    findTimeoutMs: 2000,
                    useGrayscale: false,
                    requiredHits: 2)
                .ConfigureAwait(false);

            if (panelStill != null)
            {
                ctx.Status?.Invoke("換頻：選格後未出現確定，列表仍在 → 換格重試");
                return StepRunResult.RetrySameStep;
            }

            ctx.Status?.Invoke("換頻：列表已消失 → 回退");
            return StepRunResult.Rollback;
        }

        private async Task<bool> TryIdentifyAndClickAsync(
            StepContext ctx,
            Func<Mat?> getTemplate,
            string label,
            int findTimeoutMs,
            double threshold,
            bool useGrayscale,
            bool requireStable)
        {
            // 順序：先辨識 → 找不到才清彈窗再短等 → 點擊前再辨識一次。
            TemplateHit? hit = await WaitForTargetAsync(
                    ctx,
                    getTemplate,
                    label,
                    findTimeoutMs,
                    threshold,
                    useGrayscale,
                    requireStable,
                    requiredHits: RequiredStableHits)
                .ConfigureAwait(false);

            if (hit == null)
            {
                // SoftAdvance／Rollback 交由外層 ResolveMissingTarget；此處只處理擋路清窗。
                if (ClassifyMissingTarget(ctx) == ObserveVerdict.DismissThenRetry)
                {
                    ctx.Status?.Invoke($"換頻：尚未見到「{label}」，相位無錨點 → 嘗試關閉擋路後再辨識…");
                    await DismissInterruptsAsync(ctx, allowEscape: false, allowTemplateDismiss: true)
                        .ConfigureAwait(false);
                    hit = await WaitForTargetAsync(
                            ctx,
                            getTemplate,
                            label,
                            findTimeoutMs: 2500,
                            threshold,
                            useGrayscale,
                            requireStable,
                            requiredHits: 2)
                        .ConfigureAwait(false);
                }
            }

            if (hit == null)
                return false;

            await Task.Delay(ExtraSettleAfterFoundMs, ctx.Ct).ConfigureAwait(false);

            var recheck = await WaitForTargetAsync(
                    ctx,
                    getTemplate,
                    label,
                    findTimeoutMs: 1500,
                    threshold,
                    useGrayscale,
                    requireStable: true,
                    requiredHits: 2)
                .ConfigureAwait(false);

            if (recheck == null && ctx.AllowTemplateDismiss
                && ClassifyMissingTarget(ctx) == ObserveVerdict.DismissThenRetry)
            {
                await DismissInterruptsAsync(ctx, allowEscape: false, allowTemplateDismiss: true)
                    .ConfigureAwait(false);
                recheck = await WaitForTargetAsync(
                        ctx,
                        getTemplate,
                        label,
                        findTimeoutMs: 1500,
                        threshold,
                        useGrayscale,
                        requireStable: true,
                        requiredHits: 2)
                    .ConfigureAwait(false);
            }

            if (recheck == null)
            {
                ctx.Status?.Invoke($"換頻：點擊前「{label}」已不穩定，放棄本輪");
                return false;
            }

            hit = recheck;
            ctx.Status?.Invoke($"換頻：點「{label}」（信心 {hit.Value.Score:F2}）");

            bool clicked = await ctx.ClickCapturePointAsync(
                    hit.Value.ClickX,
                    hit.Value.ClickY,
                    hit.Value.FrameWidth,
                    hit.Value.FrameHeight,
                    ctx.Ct)
                .ConfigureAwait(false);

            if (!clicked)
            {
                Logger.Warning($"[換頻] 點「{label}」滑鼠失敗");
                return false;
            }

            Logger.Info(
                $"[換頻] 已點「{label}」@ ({hit.Value.ClickX},{hit.Value.ClickY}) score={hit.Value.Score:F3}");
            return true;
        }

        private async Task<TemplateHit?> WaitForTargetAsync(
            StepContext ctx,
            Func<Mat?> getTemplate,
            string label,
            int findTimeoutMs,
            double threshold,
            bool useGrayscale,
            bool requireStable,
            int requiredHits)
        {
            if (requireStable)
            {
                return await WaitForStableTemplateAsync(
                        getTemplate,
                        threshold,
                        ctx,
                        label,
                        findTimeoutMs,
                        useGrayscale,
                        requiredHits)
                    .ConfigureAwait(false);
            }

            return await WaitForTemplateAsync(
                    getTemplate,
                    threshold,
                    ctx.GetLatestFrame,
                    findTimeoutMs,
                    label,
                    ctx.Status,
                    ctx.Ct,
                    useGrayscale)
                .ConfigureAwait(false);
        }

        private async Task<bool> TryClickOneChannelCellAsync(StepContext ctx, AutoFarmSettings settings)
        {
            IChannelCellSelector selector = ChannelCellSelectorFactory.Create(settings.ChangeChannelPickStrategy);
            var deadline = DateTime.UtcNow.AddMilliseconds(CellFindTimeoutMs);
            double bestScore = 0;

            while (DateTime.UtcNow < deadline)
            {
                ctx.Ct.ThrowIfCancellationRequested();
                Mat? template = _pickTemplate;
                Mat? frame = ctx.GetLatestFrame();
                try
                {
                    if (template == null || template.Empty() || frame == null || frame.Empty())
                    {
                        await Task.Delay(TemplatePollIntervalMs, ctx.Ct).ConfigureAwait(false);
                        continue;
                    }

                    var peek = GameVisionCore.PeekBestMatch(frame, template);
                    if (peek.HasValue)
                        bestScore = Math.Max(bestScore, peek.Value.MaxValue);

                    if (!peek.HasValue || peek.Value.MaxValue < ctx.Threshold)
                    {
                        ctx.Status?.Invoke($"換頻：等待頻道列表…（目前最佳 {bestScore:F2}）");
                        await Task.Delay(TemplatePollIntervalMs, ctx.Ct).ConfigureAwait(false);
                        continue;
                    }

                    await Task.Delay(ExtraSettleAfterFoundMs, ctx.Ct).ConfigureAwait(false);
                    frame.Dispose();
                    frame = ctx.GetLatestFrame();
                    if (frame == null || frame.Empty())
                        continue;

                    peek = GameVisionCore.PeekBestMatch(frame, template);
                    if (!peek.HasValue || peek.Value.MaxValue < ctx.Threshold)
                        continue;

                    var panelBounds = new Rectangle(
                        peek.Value.Location.X,
                        peek.Value.Location.Y,
                        template.Width,
                        template.Height);
                    if (panelBounds.Right > frame.Width || panelBounds.Bottom > frame.Height)
                        continue;

                    ChannelPickResult pick = selector.Select(frame, panelBounds, settings.ChangeChannelGrid);
                    ctx.Status?.Invoke(
                        $"換頻：點格 ({pick.Column},{pick.Row}) [{pick.Strategy}] 信心 {peek.Value.MaxValue:F2}");

                    bool ok = await ctx.ClickCapturePointAsync(
                            pick.ClientX,
                            pick.ClientY,
                            frame.Width,
                            frame.Height,
                            ctx.Ct)
                        .ConfigureAwait(false);

                    if (!ok)
                    {
                        Logger.Warning("[換頻] 點格滑鼠失敗");
                        return false;
                    }

                    Logger.Info(
                        $"[換頻] 已點格 ({pick.Column},{pick.Row}) @ ({pick.ClientX},{pick.ClientY})");
                    return true;
                }
                finally
                {
                    frame?.Dispose();
                }
            }

            Logger.Warning($"[換頻] 找不到頻道列表，最佳={bestScore:F3}");
            return false;
        }

        /// <summary>連續多幀低於閾值＝目標已離開畫面（讀取進入遊戲中）。</summary>
        private async Task<bool> WaitForTemplateGoneAsync(
            Func<Mat?> getTemplate,
            double threshold,
            StepContext ctx,
            string label,
            int timeoutMs,
            bool useGrayscale,
            int requiredAbsentHits = RequiredStableHits)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            int absentStreak = 0;
            int pollIndex = 0;
            double lastScore = 0;

            while (DateTime.UtcNow < deadline)
            {
                ctx.Ct.ThrowIfCancellationRequested();

                Mat? template = getTemplate();
                Mat? frame = ctx.GetLatestFrame();
                try
                {
                    if (template == null || template.Empty() || frame == null || frame.Empty())
                    {
                        absentStreak = 0;
                        await Task.Delay(TemplatePollIntervalMs, ctx.Ct).ConfigureAwait(false);
                        continue;
                    }

                    var peek = GameVisionCore.PeekBestMatch(frame, template, useGrayscale);
                    double score = peek?.MaxValue ?? 0;
                    lastScore = score;

                    if (score >= threshold)
                    {
                        absentStreak = 0;
                        if (pollIndex++ % 8 == 7 && ctx.AllowTemplateDismiss)
                        {
                            var snap = ProbePhase(frame, ctx);
                            // 仍在選角時點 X 可能關錯；僅無錨點才清擋路。
                            if (snap.Phase == ChangeChannelPhase.Unknown)
                            {
                                await DismissInterruptsAsync(ctx, allowEscape: false, allowTemplateDismiss: true)
                                    .ConfigureAwait(false);
                            }
                        }

                        ctx.Status?.Invoke(
                            $"換頻：等待離開「{label}」…（仍在畫面 信心 {score:F2}，連續消失 {absentStreak}/{requiredAbsentHits}）");
                    }
                    else
                    {
                        absentStreak++;
                        pollIndex = 0;
                        ctx.Status?.Invoke(
                            $"換頻：等待離開「{label}」…（目前 {score:F2}/{threshold:F2}，連續消失 {absentStreak}/{requiredAbsentHits}）");
                        if (absentStreak >= requiredAbsentHits)
                            return true;
                    }
                }
                finally
                {
                    frame?.Dispose();
                }

                await Task.Delay(TemplatePollIntervalMs, ctx.Ct).ConfigureAwait(false);
            }

            Logger.Warning($"[換頻] 等待「{label}」消失逾時，最後信心={lastScore:F3}");
            return false;
        }

        /// <summary>連續多幀達標才算畫面就緒（避免半載入就點）。</summary>
        private async Task<TemplateHit?> WaitForStableTemplateAsync(
            Func<Mat?> getTemplate,
            double threshold,
            StepContext ctx,
            string label,
            int findTimeoutMs,
            bool useGrayscale,
            int requiredHits = RequiredStableHits)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(findTimeoutMs);
            double bestScore = 0;
            int streak = 0;
            int pollIndex = 0;
            TemplateHit? lastHit = null;

            while (DateTime.UtcNow < deadline)
            {
                ctx.Ct.ThrowIfCancellationRequested();

                Mat? template = getTemplate();
                Mat? frame = ctx.GetLatestFrame();
                try
                {
                    if (template == null || template.Empty() || frame == null || frame.Empty())
                    {
                        streak = 0;
                        await Task.Delay(TemplatePollIntervalMs, ctx.Ct).ConfigureAwait(false);
                        continue;
                    }

                    var peek = GameVisionCore.PeekBestMatch(frame, template, useGrayscale);
                    if (!peek.HasValue || peek.Value.MaxValue < threshold)
                    {
                        if (peek.HasValue)
                            bestScore = Math.Max(bestScore, peek.Value.MaxValue);
                        streak = 0;

                        // 先辨識：僅相位 Unknown（讀取／可能被擋）才清窗，避免誤關已知畫面。
                        if (pollIndex++ % 6 == 5 && ctx.AllowTemplateDismiss)
                        {
                            var snap = ProbePhase(frame, ctx);
                            if (snap.Phase == ChangeChannelPhase.Unknown)
                            {
                                await DismissInterruptsAsync(ctx, allowEscape: false, allowTemplateDismiss: true)
                                    .ConfigureAwait(false);
                            }
                        }

                        ctx.Status?.Invoke(
                            $"換頻：等待{label}穩定…（目前最佳 {bestScore:F2}/{threshold:F2}，連續 {streak}/{requiredHits}）");
                        await Task.Delay(TemplatePollIntervalMs, ctx.Ct).ConfigureAwait(false);
                        continue;
                    }

                    pollIndex = 0;
                    bestScore = Math.Max(bestScore, peek.Value.MaxValue);
                    lastHit = new TemplateHit(
                        peek.Value.Location.X + template.Width / 2,
                        peek.Value.Location.Y + template.Height / 2,
                        peek.Value.MaxValue,
                        frame.Width,
                        frame.Height);
                    streak++;
                    if (streak >= requiredHits)
                        return lastHit;

                    ctx.Status?.Invoke(
                        $"換頻：等待{label}穩定…（信心 {peek.Value.MaxValue:F2}，連續 {streak}/{requiredHits}）");
                }
                finally
                {
                    frame?.Dispose();
                }

                await Task.Delay(TemplatePollIntervalMs, ctx.Ct).ConfigureAwait(false);
            }

            Logger.Warning($"[換頻] 等待{label}穩定逾時，最佳={bestScore:F3}");
            return null;
        }

        private async Task DismissInterruptsAsync(
            StepContext ctx,
            bool allowEscape,
            bool allowTemplateDismiss)
        {
            if (_activeSettings == null)
                return;

            if (!allowEscape && !allowTemplateDismiss)
                return;

            try
            {
                await _interruptDismisser.TryDismissAsync(
                        _activeSettings,
                        ctx.GetLatestFrame,
                        ctx.ClickCapturePointAsync,
                        _tapKeyAsync,
                        allowEscape,
                        allowTemplateDismiss,
                        ctx.Status,
                        ctx.Ct,
                        maxDismissals: 3,
                        preferDarkOverlayFirst: allowTemplateDismiss)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warning($"[中斷] 關閉突發視窗例外: {ex.Message}");
            }
        }

        private ChangeChannelPhaseSnapshot ProbePhase(StepContext ctx)
        {
            Mat? frame = ctx.GetLatestFrame();
            try
            {
                if (frame == null || frame.Empty())
                    return new ChangeChannelPhaseSnapshot(ChangeChannelPhase.Unknown, 0, "no-frame");

                return ProbePhase(frame, ctx);
            }
            finally
            {
                frame?.Dispose();
            }
        }

        private ChangeChannelPhaseSnapshot ProbePhase(Mat frame, StepContext ctx)
        {
            bool hasMinimap = false;
            try
            {
                hasMinimap = ctx.HasMinimapOnFrame?.Invoke(frame) == true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[換頻] 小地圖相位探測例外: {ex.Message}");
            }

            return ChangeChannelScreenProbe.Probe(
                frame,
                ctx.SoftThreshold,
                _menuTemplate,
                _pickTemplate,
                _confirmTemplate,
                _loginTemplate,
                _selectCharacterTemplate,
                hasMinimap);
        }

        private ObserveVerdict ClassifyMissingTarget(StepContext ctx)
        {
            var expected = ChangeChannelScreenProbe.ExpectedPhase((int)ctx.CurrentStep);
            var next = ChangeChannelScreenProbe.NextPhase((int)ctx.CurrentStep);
            var snap = ProbePhase(ctx);
            ctx.Status?.Invoke(
                $"換頻：相位={snap.Phase}/{snap.AnchorName} {snap.BestScore:F2}（本步期待 {expected}，下一 {next}）");

            if (snap.Phase == expected)
                return ObserveVerdict.Retry;

            if (snap.Phase == next
                || (snap.Phase is not ChangeChannelPhase.Unknown and not ChangeChannelPhase.InGame
                    && (int)snap.Phase > (int)expected))
                return ObserveVerdict.SoftAdvance;

            if (snap.Phase == ChangeChannelPhase.InGame)
            {
                return ctx.CurrentStep >= FlowStep.ClickSelectCharacter
                    ? ObserveVerdict.SoftAdvance
                    : ObserveVerdict.Rollback;
            }

            if (ChangeChannelScreenProbe.IsBehind(snap.Phase, expected))
                return ObserveVerdict.Rollback;

            return ctx.AllowTemplateDismiss
                ? ObserveVerdict.DismissThenRetry
                : ObserveVerdict.Retry;
        }

        private async Task<StepRunResult> ResolveMissingTargetAsync(StepContext ctx)
        {
            switch (ClassifyMissingTarget(ctx))
            {
                case ObserveVerdict.SoftAdvance:
                    ctx.Status?.Invoke("換頻：相位已超前 → 軟前進");
                    return StepRunResult.Advanced;

                case ObserveVerdict.DismissThenRetry:
                    ctx.Status?.Invoke("換頻：相位無錨點 → 清擋路後重試本步");
                    await DismissInterruptsAsync(ctx, allowEscape: false, allowTemplateDismiss: true)
                        .ConfigureAwait(false);
                    return StepRunResult.RetrySameStep;

                case ObserveVerdict.Rollback:
                    ctx.Status?.Invoke("換頻：相位落後 → 回退");
                    return StepRunResult.Rollback;

                default:
                    ctx.Status?.Invoke("換頻：相位未就緒 → 同一步重試");
                    return StepRunResult.RetrySameStep;
            }
        }

        private async Task<StepRunResult> ResolvePatientTimeoutAsync(
            StepContext ctx,
            string currentLabel,
            string nextLabel)
        {
            var expected = ChangeChannelScreenProbe.ExpectedPhase((int)ctx.CurrentStep);
            var next = ChangeChannelScreenProbe.NextPhase((int)ctx.CurrentStep);
            var snap = ProbePhase(ctx);
            ctx.Status?.Invoke(
                $"換頻：等「{nextLabel}」逾時，相位={snap.Phase}/{snap.AnchorName} {snap.BestScore:F2}");

            if (snap.Phase == next
                || (snap.Phase is not ChangeChannelPhase.Unknown and not ChangeChannelPhase.InGame
                    && (int)snap.Phase > (int)expected)
                || snap.Phase == ChangeChannelPhase.InGame && next == ChangeChannelPhase.InGame)
            {
                ctx.Status?.Invoke($"換頻：模板不穩但相位已到 {snap.Phase} → 軟前進");
                return StepRunResult.Advanced;
            }

            if (snap.Phase == expected)
            {
                ctx.Status?.Invoke($"換頻：仍在「{currentLabel}」相位 → 同一步重點擊");
                return StepRunResult.RetrySameStep;
            }

            if (ChangeChannelScreenProbe.IsBehind(snap.Phase, expected))
            {
                ctx.Status?.Invoke("換頻：相位落後 → 回退");
                return StepRunResult.Rollback;
            }

            // Unknown：讀取中 ≠ 失敗；可清窗時清一次再重試，永不因無錨點直接回退。
            if (ctx.AllowTemplateDismiss)
            {
                ctx.Status?.Invoke("換頻：讀取中無錨點 → 清擋路後重試");
                await DismissInterruptsAsync(ctx, allowEscape: false, allowTemplateDismiss: true)
                    .ConfigureAwait(false);
            }
            else
            {
                ctx.Status?.Invoke("換頻：讀取中無錨點 → 同一步再試（非回退）");
            }

            return StepRunResult.RetrySameStep;
        }

        /// <summary>
        /// 等待中：頻道選單／列表／確定上的 X 與活動關閉鈕同款 → 禁止誤點關選單。
        /// 僅登入／選角讀取期間允許「相位 Unknown」時清彈窗。
        /// </summary>
        private static bool AllowsTemplateInterruptWhileWaiting(FlowStep step) =>
            step is FlowStep.ClickLogin or FlowStep.ClickSelectCharacter;

        private async Task<TemplateHit?> WaitForTemplateAsync(
            Func<Mat?> getTemplate,
            double threshold,
            Func<Mat?> getLatestFrame,
            int timeoutMs,
            string label,
            Action<string>? status,
            CancellationToken ct,
            bool useGrayscale = false)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            double bestScore = 0;

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                Mat? template = getTemplate();
                Mat? frame = getLatestFrame();
                try
                {
                    if (template == null || template.Empty() || frame == null || frame.Empty())
                    {
                        await Task.Delay(TemplatePollIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    var peek = GameVisionCore.PeekBestMatch(frame, template, useGrayscale);
                    if (!peek.HasValue)
                    {
                        await Task.Delay(TemplatePollIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    bestScore = Math.Max(bestScore, peek.Value.MaxValue);
                    if (peek.Value.MaxValue < threshold)
                    {
                        status?.Invoke($"換頻：等待{label}…（目前最佳 {bestScore:F2}/{threshold:F2}）");
                        await Task.Delay(TemplatePollIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    return new TemplateHit(
                        peek.Value.Location.X + template.Width / 2,
                        peek.Value.Location.Y + template.Height / 2,
                        peek.Value.MaxValue,
                        frame.Width,
                        frame.Height);
                }
                finally
                {
                    frame?.Dispose();
                }
            }

            Logger.Warning($"[換頻] 等待{label}逾時，最佳={bestScore:F3}（閾值={threshold:F2}）");
            return null;
        }

        private static double SoftThreshold(double threshold)
            => Math.Clamp(threshold - 0.12, 0.52, 0.90);

        private readonly record struct TemplateHit(
            int ClickX,
            int ClickY,
            double Score,
            int FrameWidth,
            int FrameHeight);

        private sealed class StepContext
        {
            public StepContext(
                Func<Mat?> getLatestFrame,
                Func<int, int, int, int, CancellationToken, Task<bool>> clickCapturePointAsync,
                Action<string>? status,
                double threshold,
                double softThreshold,
                Func<Mat, bool>? hasMinimapOnFrame,
                CancellationToken ct)
            {
                GetLatestFrame = getLatestFrame;
                ClickCapturePointAsync = clickCapturePointAsync;
                Status = status;
                Threshold = threshold;
                SoftThreshold = softThreshold;
                HasMinimapOnFrame = hasMinimapOnFrame;
                Ct = ct;
            }

            public Func<Mat?> GetLatestFrame { get; }
            public Func<int, int, int, int, CancellationToken, Task<bool>> ClickCapturePointAsync { get; }
            public Action<string>? Status { get; }
            public double Threshold { get; }
            public double SoftThreshold { get; }
            public Func<Mat, bool>? HasMinimapOnFrame { get; }
            public CancellationToken Ct { get; }

            public FlowStep CurrentStep { get; set; }

            /// <summary>目前步驟等待期間是否允許點關閉模板（頻道選單階段必須為 false）。</summary>
            public bool AllowTemplateDismiss { get; set; }
        }

        private void EnsureTemplates(AutoFarmSettings settings)
        {
            lock (_templateLock)
            {
                LoadIfNeeded(ref _menuTemplate, ref _menuPathLoaded, settings.ChangeChannelMenuTemplate);
                LoadIfNeeded(ref _pickTemplate, ref _pickPathLoaded, settings.ChangeChannelPickTemplate);
                LoadIfNeeded(ref _confirmTemplate, ref _confirmPathLoaded, settings.ChangeChannelConfirmTemplate);
                LoadIfNeeded(ref _loginTemplate, ref _loginPathLoaded, settings.ChangeChannelLoginTemplate);
                LoadIfNeeded(
                    ref _selectCharacterTemplate,
                    ref _selectCharacterPathLoaded,
                    settings.ChangeChannelSelectCharacterTemplate);
            }
        }

        private static void LoadIfNeeded(ref Mat? cache, ref string loadedPath, string relativePath)
        {
            string path = relativePath?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path))
                return;

            if (!Path.IsPathRooted(path))
                path = Path.Combine(PathManager.ContentRoot, path);

            if (string.Equals(loadedPath, path, StringComparison.OrdinalIgnoreCase)
                && cache != null && !cache.Empty())
                return;

            cache?.Dispose();
            cache = null;
            loadedPath = path;

            if (!File.Exists(path))
            {
                Logger.Warning($"[換頻] 模板不存在: {path}");
                return;
            }

            Mat loaded = Cv2.ImRead(path, ImreadModes.Color);
            if (loaded.Empty())
            {
                loaded.Dispose();
                Logger.Warning($"[換頻] 無法讀取模板: {path}");
                return;
            }

            cache = loaded;
            Logger.Info($"[換頻] 已載入模板: {path} ({loaded.Width}x{loaded.Height})");
        }

        public void Dispose()
        {
            _interruptDismisser.Dispose();
            _activeSettings = null;
            _tapKeyAsync = null;
            lock (_templateLock)
            {
                _menuTemplate?.Dispose();
                _pickTemplate?.Dispose();
                _confirmTemplate?.Dispose();
                _loginTemplate?.Dispose();
                _selectCharacterTemplate?.Dispose();
                _menuTemplate = null;
                _pickTemplate = null;
                _confirmTemplate = null;
                _loginTemplate = null;
                _selectCharacterTemplate = null;
            }
        }
    }
}
