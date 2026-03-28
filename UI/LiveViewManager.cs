using ArtaleAI.Models.Config;
using ArtaleAI.Services;
using ArtaleAI.Utils;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;

namespace ArtaleAI.UI
{
    /// <summary>定時擷取畫面、佇列限長、以最新幀觸發 <see cref="OnFrameReady"/>。</summary>
    public class LiveViewManager : IDisposable
    {
        /// <summary>畫面與該幀擷取時間（UTC）。</summary>
        public record TimestampedFrame(Mat Frame, DateTime CaptureTime);

        private readonly AppConfig config;
        private readonly ConcurrentQueue<TimestampedFrame> frameQueue = new();

        private int _isProcessingFrame = 0;

        private GraphicsCapturer? _capturer;
        private System.Threading.Timer? _captureTimer;
        private System.Threading.Timer? _detectionTimer;
        private int _isRunningState = 0;

        private const int DetectionIntervalMs = 20;
        private const int MaxFrameQueueSize = 3;
        private const int ShutdownDelayMs = 50;
        private static int _skippedDetectionTicksWhileBusy;

        /// <summary>新幀就緒（含擷取時間）；訂閱者負責處理／釋放 <see cref="Mat"/>。</summary>
        public event Action<Mat, DateTime>? OnFrameReady;

        public LiveViewManager(AppConfig appConfig)
        {
            config = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        }

        /// <summary>建立擷取器與擷取／分發計時器。</summary>
        public void StartLiveView(GraphicsCaptureItem captureItem)
        {
            if (Interlocked.CompareExchange(ref _isRunningState, 1, 0) == 1)
            {
                Logger.Debug("[LiveView] LiveView已在執行中");
                return;
            }

            try
            {
                _capturer = new GraphicsCapturer(captureItem);

                int targetFPS = config.Vision.CaptureFrameRate;
                int captureIntervalMs = 1000 / targetFPS;

                _captureTimer = new System.Threading.Timer(OnCaptureTimer, null, 0, captureIntervalMs);
                _detectionTimer = new System.Threading.Timer(OnDetectionTimer, null, 100, DetectionIntervalMs);

                Logger.Info($"[LiveView] LiveView已啟動: {targetFPS}FPS, 偵測頻率:{1000.0 / DetectionIntervalMs:F1}Hz");
            }
            catch (Exception ex)
            {
                Logger.Error($"[LiveView] 啟動LiveView失敗: {ex.Message}");
                StopLiveView();
            }
        }

        /// <summary>停止計時器、清空佇列並釋放擷取器。</summary>
        public void StopLiveView()
        {
            if (Interlocked.CompareExchange(ref _isRunningState, 0, 1) == 0)
                return;

            try
            {
                _captureTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _detectionTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                while (frameQueue.TryDequeue(out var frame))
                {
                    frame?.Frame?.Dispose();
                }

                Thread.Sleep(ShutdownDelayMs);

                _capturer?.Dispose();
                _capturer = null;

                _captureTimer?.Dispose();
                _detectionTimer?.Dispose();
                _captureTimer = null;
                _detectionTimer = null;

                Logger.Info("[LiveView] LiveView已停止");
            }
            catch (Exception ex)
            {
                Logger.Error($"[LiveView] 停止LiveView失敗: {ex.Message}");
            }
        }

        public bool IsRunning => _isRunningState == 1;

        /// <summary>等待 <see cref="OnFrameReady"/> 首次分發有效幀，或逾時。取代固定 <c>Task.Delay</c> 猜測擷取管線已就緒。</summary>
        public async Task<bool> WaitForFirstFrameAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (!IsRunning)
                return false;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnFirst(Mat _, DateTime __) => tcs.TrySetResult(true);

            OnFrameReady += OnFirst;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                try
                {
                    await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
            finally
            {
                OnFrameReady -= OnFirst;
            }
        }

        private void OnCaptureTimer(object? state)
        {
            if (_isRunningState == 0 || _capturer == null)
                return;

            try
            {
                using var frameMat = _capturer.TryGetNextMat();
                if (frameMat != null && !frameMat.Empty())
                {
                    var captureTime = DateTime.UtcNow;

                    if (frameQueue.Count < MaxFrameQueueSize)
                    {
                        frameQueue.Enqueue(new TimestampedFrame(frameMat.Clone(), captureTime));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[LiveView] 畫面抓取錯誤: {ex.Message}");
            }
        }

        private void OnDetectionTimer(object? state)
        {
            if (_isRunningState == 0)
                return;

            if (Interlocked.CompareExchange(ref _isProcessingFrame, 1, 0) == 1)
            {
                var n = Interlocked.Increment(ref _skippedDetectionTicksWhileBusy);
                if (n == 1 || n % 200 == 0)
                    Logger.Debug($"[LiveView] 偵測 tick 略過（上一幀 ProcessFrame 尚未結束），累計≈{n}");
                return;
            }

            try
            {
                TimestampedFrame? latestFrame = null;

                while (frameQueue.TryDequeue(out var frame))
                {
                    latestFrame?.Frame?.Dispose();
                    latestFrame = frame;

                    if (frameQueue.Count == 0)
                        break;
                }

                if (latestFrame == null)
                    return;

                try
                {
                    OnFrameReady?.Invoke(latestFrame.Frame, latestFrame.CaptureTime);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[LiveView] 偵測處理錯誤: {ex.Message}");
                    latestFrame?.Frame?.Dispose();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessingFrame, 0);
            }
        }

        public void Dispose()
        {
            StopLiveView();
        }
    }
}
