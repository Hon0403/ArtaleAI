using ArtaleAI.Config;
using ArtaleAI.GameWindow;
using ArtaleAI.Interfaces;

namespace ArtaleAI.Display
{
    /// <summary>
    /// 即時顯示服務 - 負責實際的畫面捕捉和處理
    /// </summary>
    public class LiveViewService : IDisposable
    {
        private GraphicsCapturer? _capturer;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _captureTask;
        private readonly IMainFormEvents _eventHandler;
        private bool _isRunning;

        public GraphicsCapturer? Capturer => _capturer;
        public bool IsRunning => _isRunning;

        public LiveViewService(IMainFormEvents eventHandler)
        {
            _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
        }

        /// <summary>
        /// 開始即時顯示
        /// </summary>
        public async Task StartAsync(AppConfig config)
        {
            if (_isRunning)
            {
                _eventHandler.OnStatusMessage("即時顯示已經在運行中");
                return;
            }

            try
            {
                _eventHandler.OnStatusMessage("正在尋找遊戲視窗...");

                // 尋找遊戲視窗
                var captureItem = WindowFinder.TryCreateItemForWindow(config.General.GameWindowTitle);
                if (captureItem == null)
                {
                    _eventHandler.OnError($"找不到名為 '{config.General.GameWindowTitle}' 的遊戲視窗");
                    return;
                }

                _eventHandler.OnStatusMessage(" 成功找到遊戲視窗");

                // 建立捕捉器
                _capturer = new GraphicsCapturer(captureItem);
                _cancellationTokenSource = new CancellationTokenSource();

                _eventHandler.OnStatusMessage("🎥 即時顯示已啟動");
                _isRunning = true;

                // 開始捕捉任務
                _captureTask = CaptureLoopAsync(_cancellationTokenSource.Token);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"啟動即時顯示失敗: {ex.Message}");
                await StopAsync();
            }
        }

        /// <summary>
        /// 停止即時顯示
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning) return;

            try
            {
                _isRunning = false;
                _cancellationTokenSource?.Cancel();

                if (_captureTask != null && !_captureTask.IsCompleted)
                {
                    await _captureTask;
                }

                _eventHandler.OnStatusMessage("🛑 即時顯示已停止");
            }
            catch (TaskCanceledException)
            {
                // 正常的取消操作，忽略
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"停止即時顯示時發生錯誤: {ex.Message}");
            }
            finally
            {
                _capturer?.Dispose();
                _capturer = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _captureTask = null;
            }
        }

        /// <summary>
        /// 捕捉循環的核心邏輯
        /// </summary>
        private async Task CaptureLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Yield();
                while (!cancellationToken.IsCancellationRequested && _capturer != null)
                {
                    using (var frame = _capturer.TryGetNextFrame())
                    {
                        if (frame != null)
                        {
                            // ✅ 創建執行緒安全的副本
                            Bitmap safeCopy;
                            try
                            {
                                // ✅ 在 using 塊內立即創建副本
                                safeCopy = new Bitmap(frame.Width, frame.Height, frame.PixelFormat);
                                using (var g = Graphics.FromImage(safeCopy))
                                {
                                    g.DrawImage(frame, 0, 0);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"創建 frame 副本失敗: {ex.Message}");
                                continue;
                            }

                            // ✅ 直接調用，不使用額外的 Task.Run
                            _eventHandler.OnFrameAvailable(safeCopy);
                        }
                    }

                    await Task.Delay(67, cancellationToken); // ~15 FPS
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _eventHandler.OnError($"捕捉過程中發生錯誤: {ex.Message}");
            }
        }

        public void Dispose()
        {
            StopAsync().Wait(5000); // 最多等待5秒
            _capturer?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
