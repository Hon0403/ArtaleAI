using ArtaleAI.Config;
using ArtaleAI.GameWindow;
using ArtaleAI.Interfaces;

namespace ArtaleAI.GameCapture
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

                _eventHandler.OnStatusMessage("✅ 成功找到遊戲視窗");

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
                // 確保在背景執行緒中執行
                await Task.Yield();

                while (!cancellationToken.IsCancellationRequested && _capturer != null)
                {
                    using (var frame = _capturer.TryGetNextFrame())
                    {
                        if (frame != null)
                        {
                            var frameCopy = new Bitmap(frame);

                            // 強制在背景執行緒中調用
                            await Task.Run(() => _eventHandler.OnFrameAvailable(frameCopy));
                        }
                    }

                    await Task.Delay(16, cancellationToken);
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
