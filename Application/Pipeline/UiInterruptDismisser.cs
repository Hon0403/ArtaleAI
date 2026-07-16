using ArtaleAI.Models.Config;
using ArtaleAI.Shared;
using ArtaleAI.Vision;
using OpenCvSharp;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>
    /// 突發 UI 中斷：變暗遮罩優先點暗色 X → 白名單關閉鈕 →（可選）Esc。
    /// </summary>
    public sealed class UiInterruptDismisser : IDisposable
    {
        private const ushort VkEscape = 0x1B;
        private const int PostEscapeSettleMs = 450;
        private const int PostClickSettleMs = 450;

        private readonly object _lock = new();
        private readonly List<(string Path, Mat Template)> _templates = [];
        private string _fingerprint = string.Empty;
        private Mat? _darkOverlayTemplate;
        private string _darkOverlayPathLoaded = string.Empty;
        private DateTime _lastEscapeUtc = DateTime.MinValue;

        /// <summary>
        /// 畫面變暗／誤開選單：只辨識暗色關閉 X 並點擊。
        /// </summary>
        public async Task<bool> TryDismissDarkOverlayAsync(
            AutoFarmSettings settings,
            Func<Mat?> getLatestFrame,
            Func<int, int, int, int, CancellationToken, Task<bool>> clickCapturePointAsync,
            Action<string>? status,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(getLatestFrame);
            ArgumentNullException.ThrowIfNull(clickCapturePointAsync);

            if (!settings.InterruptDismissEnabled)
                return false;

            EnsureDarkOverlayTemplate(settings);
            Mat? template;
            lock (_lock)
                template = _darkOverlayTemplate;

            if (template == null || template.Empty())
                return false;

            double threshold = Math.Clamp(settings.InterruptMatchThreshold, 0.55, 0.95);
            if (!TryMatchTemplate(getLatestFrame, template, threshold, out DismissHit hit))
                return false;

            status?.Invoke($"中斷：變暗介面，點暗色關閉鈕（信心 {hit.Score:F2}）");
            bool ok = await clickCapturePointAsync(
                    hit.ClickX,
                    hit.ClickY,
                    hit.FrameWidth,
                    hit.FrameHeight,
                    ct)
                .ConfigureAwait(false);

            if (!ok)
            {
                Logger.Warning("[中斷] 點暗色關閉鈕失敗");
                return false;
            }

            Logger.Info(
                $"[中斷] 已點暗色關閉 @ ({hit.ClickX},{hit.ClickY}) score={hit.Score:F3}");
            await Task.Delay(PostClickSettleMs, ct).ConfigureAwait(false);
            return true;
        }

        public async Task<int> TryDismissAsync(
            AutoFarmSettings settings,
            Func<Mat?> getLatestFrame,
            Func<int, int, int, int, CancellationToken, Task<bool>> clickCapturePointAsync,
            Func<ushort, CancellationToken, Task>? tapKeyAsync,
            bool allowEscape,
            bool allowTemplateDismiss,
            Action<string>? status,
            CancellationToken ct,
            int maxDismissals = 3,
            bool preferDarkOverlayFirst = true)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(getLatestFrame);
            ArgumentNullException.ThrowIfNull(clickCapturePointAsync);

            if (!settings.InterruptDismissEnabled)
                return 0;

            if (!allowEscape && !allowTemplateDismiss)
                return 0;

            int dismissed = 0;

            // 順序：Esc（多數介面）→ 暗色 X（Esc 關不掉的變暗遮罩）→ 其餘白名單（可空）。
            // allowEscape 由呼叫端決定（換頻安全步／打怪開關）；此處不再二次閘 InterruptPreferEscape。
            if (allowEscape
                && tapKeyAsync != null
                && IsEscapeCooldownReady(settings))
            {
                status?.Invoke("中斷：Esc 嘗試關閉突發介面");
                await tapKeyAsync(VkEscape, ct).ConfigureAwait(false);
                _lastEscapeUtc = DateTime.UtcNow;
                dismissed++;
                Logger.Info("[中斷] 已按 Esc 嘗試關閉介面");
                await Task.Delay(PostEscapeSettleMs, ct).ConfigureAwait(false);
            }

            if (!allowTemplateDismiss)
                return dismissed;

            if (preferDarkOverlayFirst
                && await TryDismissDarkOverlayAsync(
                        settings,
                        getLatestFrame,
                        clickCapturePointAsync,
                        status,
                        ct)
                    .ConfigureAwait(false))
            {
                dismissed++;
            }

            EnsureTemplates(settings);
            if (_templates.Count == 0)
                return dismissed;

            double threshold = Math.Clamp(settings.InterruptMatchThreshold, 0.55, 0.95);
            int clickBudget = Math.Max(0, maxDismissals - dismissed);

            while (clickBudget-- > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (!TryFindBestDismissTarget(getLatestFrame, threshold, out var hit, out string name))
                    break;

                status?.Invoke($"中斷：關閉突發視窗「{name}」（信心 {hit.Score:F2}）");
                bool ok = await clickCapturePointAsync(
                        hit.ClickX,
                        hit.ClickY,
                        hit.FrameWidth,
                        hit.FrameHeight,
                        ct)
                    .ConfigureAwait(false);

                if (!ok)
                {
                    Logger.Warning($"[中斷] 點關閉失敗: {name}");
                    break;
                }

                dismissed++;
                Logger.Info($"[中斷] 已點關閉 {name} @ ({hit.ClickX},{hit.ClickY}) score={hit.Score:F3}");
                await Task.Delay(PostClickSettleMs, ct).ConfigureAwait(false);
            }

            return dismissed;
        }

        private bool IsEscapeCooldownReady(AutoFarmSettings settings)
        {
            int cooldownMs = Math.Clamp(settings.InterruptEscapeCooldownMs, 800, 15000);
            return (DateTime.UtcNow - _lastEscapeUtc).TotalMilliseconds >= cooldownMs;
        }

        private static bool TryMatchTemplate(
            Func<Mat?> getLatestFrame,
            Mat template,
            double threshold,
            out DismissHit hit)
        {
            hit = default;
            Mat? frame = getLatestFrame();
            try
            {
                if (frame == null || frame.Empty() || template.Empty())
                    return false;

                var peek = GameVisionCore.PeekBestMatch(frame, template);
                if (!peek.HasValue || peek.Value.MaxValue < threshold)
                    return false;

                hit = new DismissHit(
                    peek.Value.Location.X + template.Width / 2,
                    peek.Value.Location.Y + template.Height / 2,
                    peek.Value.MaxValue,
                    frame.Width,
                    frame.Height);
                return true;
            }
            finally
            {
                frame?.Dispose();
            }
        }

        private bool TryFindBestDismissTarget(
            Func<Mat?> getLatestFrame,
            double threshold,
            out DismissHit hit,
            out string templateName)
        {
            hit = default;
            templateName = string.Empty;
            Mat? frame = getLatestFrame();
            try
            {
                if (frame == null || frame.Empty())
                    return false;

                double best = 0;
                DismissHit bestHit = default;
                string bestName = string.Empty;

                lock (_lock)
                {
                    foreach (var (path, template) in _templates)
                    {
                        if (template.Empty())
                            continue;

                        var peek = GameVisionCore.PeekBestMatch(frame, template);
                        if (!peek.HasValue || peek.Value.MaxValue < threshold)
                            continue;

                        if (peek.Value.MaxValue <= best)
                            continue;

                        best = peek.Value.MaxValue;
                        bestHit = new DismissHit(
                            peek.Value.Location.X + template.Width / 2,
                            peek.Value.Location.Y + template.Height / 2,
                            peek.Value.MaxValue,
                            frame.Width,
                            frame.Height);
                        bestName = Path.GetFileNameWithoutExtension(path);
                    }
                }

                if (best < threshold)
                    return false;

                hit = bestHit;
                templateName = bestName;
                return true;
            }
            finally
            {
                frame?.Dispose();
            }
        }

        private void EnsureDarkOverlayTemplate(AutoFarmSettings settings)
        {
            string relative = settings.InterruptDarkOverlayCloseTemplate?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(relative))
                relative = "templates/MainScreen/interrupt_close_dark_x.png";

            string path = Path.IsPathRooted(relative)
                ? relative
                : Path.Combine(PathManager.ContentRoot, relative);

            lock (_lock)
            {
                if (string.Equals(_darkOverlayPathLoaded, path, StringComparison.Ordinal)
                    && _darkOverlayTemplate != null
                    && !_darkOverlayTemplate.Empty())
                    return;

                _darkOverlayTemplate?.Dispose();
                _darkOverlayTemplate = null;
                _darkOverlayPathLoaded = path;

                if (!File.Exists(path))
                {
                    Logger.Warning($"[中斷] 變暗關閉模板不存在: {path}");
                    return;
                }

                Mat loaded = Cv2.ImRead(path, ImreadModes.Color);
                if (loaded.Empty())
                {
                    loaded.Dispose();
                    Logger.Warning($"[中斷] 無法讀取變暗關閉模板: {path}");
                    return;
                }

                _darkOverlayTemplate = loaded;
                Logger.Info($"[中斷] 已載入變暗關閉模板: {path} ({loaded.Width}x{loaded.Height})");
            }
        }

        private void EnsureTemplates(AutoFarmSettings settings)
        {
            settings.InterruptDismissTemplates ??= [];
            string fingerprint = string.Join("|", settings.InterruptDismissTemplates);
            lock (_lock)
            {
                if (string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal)
                    && _templates.Count > 0)
                    return;

                ClearTemplates_NoLock();
                _fingerprint = fingerprint;

                foreach (string relative in settings.InterruptDismissTemplates)
                {
                    string path = relative?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (!Path.IsPathRooted(path))
                        path = Path.Combine(PathManager.ContentRoot, path);

                    if (!File.Exists(path))
                    {
                        Logger.Warning($"[中斷] 模板不存在: {path}");
                        continue;
                    }

                    Mat loaded = Cv2.ImRead(path, ImreadModes.Color);
                    if (loaded.Empty())
                    {
                        loaded.Dispose();
                        Logger.Warning($"[中斷] 無法讀取模板: {path}");
                        continue;
                    }

                    _templates.Add((path, loaded));
                    Logger.Info($"[中斷] 已載入關閉模板: {path} ({loaded.Width}x{loaded.Height})");
                }
            }
        }

        private void ClearTemplates_NoLock()
        {
            foreach (var (_, mat) in _templates)
                mat.Dispose();
            _templates.Clear();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                ClearTemplates_NoLock();
                _fingerprint = string.Empty;
                _darkOverlayTemplate?.Dispose();
                _darkOverlayTemplate = null;
                _darkOverlayPathLoaded = string.Empty;
            }
        }

        private readonly record struct DismissHit(
            int ClickX,
            int ClickY,
            double Score,
            int FrameWidth,
            int FrameHeight);
    }
}
