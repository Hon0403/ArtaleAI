using ArtaleAI.Models.Config;
using ArtaleAI.Services;
using ArtaleAI.Utils;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Windows.Graphics.Capture;

namespace ArtaleAI.UI
{
    /// <summary>
    /// 負責管理即時畫面更新：控制Timer、抓取畫面、分發給處理模組
    /// </summary>
    public class LiveViewManager : IDisposable
    {
        #region Timestamped Frame
        /// <summary>
        /// 包含擷取時間戳的畫面資料
        /// </summary>
        public record TimestampedFrame(Mat Frame, DateTime CaptureTime);
        #endregion

        #region Private Fields
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
        #endregion

        #region Events
        /// <summary>
        /// 當有新畫面可供處理時觸發（傳給MainForm做偵測和繪圖）
        /// 包含擷取時間戳，確保時間同步
        /// </summary>
        public event Action<Mat, DateTime>? OnFrameReady;
        #endregion

        #region Constructor
        public LiveViewManager(AppConfig appConfig)
        {
            config = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 啟動即時畫面更新
        /// </summary>
        public void StartLiveView(GraphicsCaptureItem captureItem)
        {
            if (System.Threading.Interlocked.CompareExchange(ref _isRunningState, 1, 0) == 1)
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

        /// <summary>
        /// 停止即時畫面更新
        /// </summary>
        public void StopLiveView()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _isRunningState, 0, 1) == 0)
                return;

            try
            {

                _captureTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _detectionTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                while (frameQueue.TryDequeue(out var frame))
                {
                    frame?.Frame?.Dispose();
                }

                System.Threading.Thread.Sleep(ShutdownDelayMs);

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

        /// <summary>
        /// 檢查LiveView是否正在執行
        /// </summary>
        public bool IsRunning => _isRunningState == 1;
        #endregion

        #region Private Timer Callbacks
        /// <summary>
        /// 畫面抓取Timer回調 - 負責定時抓取遊戲畫面
        /// </summary>
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

        /// <summary>
        /// 偵測處理Timer回調 - 從隊列取出畫面並分發給訂閱者處理
        /// 性能優化：只處理最新幀，丟棄堆積的舊幀以保持同步
        /// </summary>
        private void OnDetectionTimer(object? state)
        {
            if (_isRunningState == 0)
                return;

            if (System.Threading.Interlocked.CompareExchange(ref _isProcessingFrame, 1, 0) == 1)
                return;

            try
            {
                TimestampedFrame? latestFrame = null;
                int skippedFrames = 0;

                while (frameQueue.TryDequeue(out var frame))
                {
                    latestFrame?.Frame?.Dispose();
                    latestFrame = frame;
                    
                    if (frameQueue.Count == 0)
                        break;
                    
                    skippedFrames++;
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
                System.Threading.Interlocked.Exchange(ref _isProcessingFrame, 0);
            }
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            StopLiveView();
        }
        #endregion
    }
}
