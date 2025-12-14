using ArtaleAI.Config;
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
        #region Private Fields
        private readonly AppConfig config;
        private readonly ConcurrentQueue<Mat> frameQueue = new();
        private GraphicsCapturer? _capturer;
        private System.Threading.Timer? _captureTimer;
        private System.Threading.Timer? _detectionTimer;
        private bool _isLiveViewRunning = false;
        
        // 常數定義
        // ✅ 性能優化：降低檢測頻率（從 50ms 改為 100ms，從 20Hz 降到 10Hz）
        // 仍然足夠快速反應，但大幅降低CPU使用率
        private const int DetectionIntervalMs = 100; // 偵測處理間隔（毫秒）
        private const int MaxFrameQueueSize = 3; // 最大畫面隊列大小
        private const int ShutdownDelayMs = 50; // 關閉時等待時間（毫秒）
        #endregion

        #region Events
        /// <summary>
        /// 當有新畫面可供處理時觸發（傳給MainForm做偵測和繪圖）
        /// </summary>
        public event Action<Mat>? OnFrameReady;
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
            if (_isLiveViewRunning)
            {
                Logger.Debug("[LiveView] LiveView已在執行中");
                return;
            }

            try
            {
                _capturer = new GraphicsCapturer(captureItem);

                // 計算Timer間隔
                int targetFPS = config.CaptureFrameRate;
                int captureIntervalMs = 1000 / targetFPS;

                // 啟動兩個Timer
                _captureTimer = new System.Threading.Timer(OnCaptureTimer, null, 0, captureIntervalMs);
                _detectionTimer = new System.Threading.Timer(OnDetectionTimer, null, 100, DetectionIntervalMs);

                _isLiveViewRunning = true;
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
            if (!_isLiveViewRunning)
                return;

            try
            {
                _isLiveViewRunning = false;

                // 停止Timer（防止新的擷取和處理）
                _captureTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _detectionTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                // 清空畫面隊列（釋放待處理的畫面）
                while (frameQueue.TryDequeue(out var frame))
                {
                    frame?.Dispose();
                }

                // 等待一小段時間確保所有背景操作完成
                System.Threading.Thread.Sleep(ShutdownDelayMs);

                // 釋放Capturer（這會釋放 Direct3D 資源）
                _capturer?.Dispose();
                _capturer = null;

                // 釋放Timer
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
        public bool IsRunning => _isLiveViewRunning;
        #endregion

        #region Private Timer Callbacks
        /// <summary>
        /// 畫面抓取Timer回調 - 負責定時抓取遊戲畫面
        /// </summary>
        private void OnCaptureTimer(object? state)
        {
            if (!_isLiveViewRunning || _capturer == null)
                return;

            try
            {
                using var frameMat = _capturer.TryGetNextMat();
                if (frameMat != null && !frameMat.Empty())
                {
                    // 如果隊列太多就不加了（避免記憶體爆炸）
                    if (frameQueue.Count < MaxFrameQueueSize)
                    {
                        frameQueue.Enqueue(frameMat.Clone());
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
        /// </summary>
        private void OnDetectionTimer(object? state)
        {
            if (!_isLiveViewRunning)
                return;

            // 從隊列取出畫面
            if (!frameQueue.TryDequeue(out var frameMat))
                return;

            try
            {
                OnFrameReady?.Invoke(frameMat);
            }
            catch (Exception ex)
            {
                Logger.Error($"[LiveView] 偵測處理錯誤: {ex.Message}");
                // 確保異常時也釋放資源
                frameMat?.Dispose();
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
