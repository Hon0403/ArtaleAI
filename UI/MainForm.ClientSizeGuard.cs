using ArtaleAI.Infrastructure.Capture;
using ArtaleAI.Shared;
using ArtaleAI.UI.MapEditor;

namespace ArtaleAI
{
    public partial class MainForm
    {
        private System.Windows.Forms.Timer? _clientSizeGuardTimer;
        private DateTime _lastClientSizeForceUtc = DateTime.MinValue;
        private bool _clientSizeGuardBusy;

        private void EnsureClientSizeGuardTimerCreated()
        {
            if (_clientSizeGuardTimer != null)
                return;

            var general = Config.General;
            int intervalMs = Math.Clamp(general.ForceClientSizeCheckIntervalMs, 500, 10_000);
            _clientSizeGuardTimer = new System.Windows.Forms.Timer { Interval = intervalMs };
            _clientSizeGuardTimer.Tick += async (_, _) => await OnClientSizeGuardTickAsync();
        }

        /// <summary>僅在 LiveView 擷取中啟用；關閉擷取即停止，避免與「視窗存在」輪詢打架。</summary>
        private void SyncClientSizeGuardTimer()
        {
            EnsureClientSizeGuardTimerCreated();
            if (_clientSizeGuardTimer == null)
                return;

            bool shouldRun = Config.General.ForceClientSizeWhileCapture
                && liveViewManager?.IsRunning == true;

            int intervalMs = Math.Clamp(Config.General.ForceClientSizeCheckIntervalMs, 500, 10_000);
            if (_clientSizeGuardTimer.Interval != intervalMs)
                _clientSizeGuardTimer.Interval = intervalMs;

            if (shouldRun)
            {
                if (!_clientSizeGuardTimer.Enabled)
                    _clientSizeGuardTimer.Start();
            }
            else
            {
                _clientSizeGuardTimer.Stop();
            }
        }

        private async Task OnClientSizeGuardTickAsync()
        {
            if (_clientSizeGuardBusy || IsDisposed || Disposing)
                return;
            if (!Config.General.ForceClientSizeWhileCapture)
                return;
            if (liveViewManager?.IsRunning != true)
            {
                SyncClientSizeGuardTimer();
                return;
            }

            await EnsureGameClientSizeAsync(forceImmediate: false, relocateMinimapIfResized: true);
        }

        /// <summary>
        /// 校正遊戲客戶區至設定尺寸（預設 1280×720）。
        /// 通過條件只有一個：目前客戶區是否等於目標寬高，與現況是 1920×1080 或其他無關。
        /// </summary>
        /// <returns>true 表示已符合目標尺寸（原本就對或已成功改過）。</returns>
        private async Task<bool> EnsureGameClientSizeAsync(
            bool forceImmediate,
            bool relocateMinimapIfResized)
        {
            var general = Config.General;
            if (!general.ForceClientSizeWhileCapture)
                return true;

            IntPtr hwnd = WindowFinder.FindGameWindowHandle(Config);
            int targetW = general.ForceClientWidth;
            int targetH = general.ForceClientHeight;
            if (targetW < 640 || targetH < 480)
                return false;

            if (hwnd == IntPtr.Zero)
            {
                MsgLog.ShowError(textBox1, "找不到遊戲視窗，無法校正客戶區尺寸");
                return false;
            }

            if (!WindowFinder.TryGetClientSize(hwnd, out int width, out int height))
                return false;

            if (WindowFinder.IsClientSizeMatch(width, height, targetW, targetH))
                return true;

            int cooldownMs = Math.Clamp(general.ForceClientSizeCooldownMs, 500, 30_000);
            if (!forceImmediate
                && (DateTime.UtcNow - _lastClientSizeForceUtc).TotalMilliseconds < cooldownMs)
            {
                return false;
            }

            if (_clientSizeGuardBusy)
                return false;

            _clientSizeGuardBusy = true;
            try
            {
                MsgLog.ShowStatus(
                    textBox1,
                    $"遊戲視窗客戶區 {width}x{height} ≠ {targetW}x{targetH}，正在校正…");

                bool resized = WindowFinder.ForceGameWindowSize(
                    hwnd,
                    targetW,
                    targetH,
                    msg =>
                    {
                        Logger.Info($"[視窗校正] {msg}");
                        MsgLog.ShowStatus(textBox1, msg);
                    });

                _lastClientSizeForceUtc = DateTime.UtcNow;

                if (!resized)
                {
                    MsgLog.ShowError(
                        textBox1,
                        $"遊戲視窗尺寸校正失敗（仍非 {targetW}x{targetH}）。請改為視窗模式、取消最大化後再開擷取。");
                    return false;
                }

                // 給系統／擷取器一點時間吃到新客戶區
                await Task.Delay(200);

                if (relocateMinimapIfResized)
                    await RelocateMinimapAfterClientResizeAsync();

                MsgLog.ShowStatus(textBox1, $"遊戲視窗已校正為客戶區 {targetW}x{targetH}");
                return true;
            }
            finally
            {
                _clientSizeGuardBusy = false;
            }
        }

        private async Task RelocateMinimapAfterClientResizeAsync()
        {
            try
            {
                var minimapResult = await LoadMinimapWithMat(MinimapUsage.LiveViewOverlay);
                if (minimapResult?.MinimapScreenRect.HasValue == true)
                {
                    minimapBounds = minimapResult.MinimapScreenRect.Value;
                    _gamePipeline?.SetMinimapBoxes(new List<Rectangle> { minimapResult.MinimapScreenRect.Value });
                    Logger.Info("[視窗校正] 尺寸變更後已重新定位小地圖");
                }
                else
                {
                    Logger.Warning("[視窗校正] 尺寸變更後無法重新定位小地圖");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[視窗校正] 重定位小地圖失敗: {ex.Message}");
            }
        }

        private void DisposeClientSizeGuardTimer()
        {
            if (_clientSizeGuardTimer == null)
                return;

            _clientSizeGuardTimer.Stop();
            _clientSizeGuardTimer.Dispose();
            _clientSizeGuardTimer = null;
        }
    }
}
