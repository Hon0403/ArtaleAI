using ArtaleAI.Config;
using ArtaleAI.GameWindow;
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
        private GraphicsCapturer? capturer;
        private System.Threading.Timer? captureTimer;
        private System.Threading.Timer? detectionTimer;
        private readonly ConcurrentQueue<Mat> frameQueue = new();
        private bool isLiveViewRunning = false;
        private readonly AppConfig config;
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
            if (isLiveViewRunning)
            {
                Debug.WriteLine("LiveView已在執行中");
                return;
            }

            try
            {
                capturer = new GraphicsCapturer(captureItem);

                // 計算Timer間隔
                int targetFPS = config.CaptureFrameRate;
                int captureIntervalMs = 1000 / targetFPS;
                int detectionIntervalMs = 150; // 150ms做一次偵測處理

                // 啟動兩個Timer
                captureTimer = new System.Threading.Timer(OnCaptureTimer, null, 0, captureIntervalMs);
                detectionTimer = new System.Threading.Timer(OnDetectionTimer, null, 100, detectionIntervalMs);

                isLiveViewRunning = true;
                Debug.WriteLine($"LiveView已啟動: {targetFPS}FPS, 偵測頻率:{1000.0 / detectionIntervalMs:F1}Hz");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"啟動LiveView失敗: {ex.Message}");
                StopLiveView();
            }
        }

        /// <summary>
        /// 停止即時畫面更新
        /// </summary>
        public void StopLiveView()
        {
            if (!isLiveViewRunning)
                return;

            try
            {
                isLiveViewRunning = false;

                // 停止Timer
                captureTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                detectionTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                // 釋放Capturer
                capturer?.Dispose();
                capturer = null;

                // 清空畫面隊列
                while (frameQueue.TryDequeue(out var frame))
                {
                    frame?.Dispose();
                }

                // 釋放Timer
                captureTimer?.Dispose();
                detectionTimer?.Dispose();
                captureTimer = null;
                detectionTimer = null;

                Debug.WriteLine("LiveView已停止");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"停止LiveView失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 檢查LiveView是否正在執行
        /// </summary>
        public bool IsRunning => isLiveViewRunning;
        #endregion

        #region Private Timer Callbacks
        /// <summary>
        /// 畫面抓取Timer回調 - 負責定時抓取遊戲畫面
        /// </summary>
        private void OnCaptureTimer(object? state)
        {
            if (!isLiveViewRunning || capturer == null)
                return;

            try
            {
                using var frameMat = capturer.TryGetNextMat();
                if (frameMat != null && !frameMat.Empty())
                {
                    // 如果隊列太多就不加了（避免記憶體爆炸）
                    if (frameQueue.Count < 3)
                    {
                        frameQueue.Enqueue(frameMat.Clone());
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"畫面抓取錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 偵測處理Timer回調 - 從隊列取出畫面並分發給訂閱者處理
        /// </summary>
        private void OnDetectionTimer(object? state)
        {
            if (!isLiveViewRunning)
                return;

            // 從隊列取出畫面
            if (!frameQueue.TryDequeue(out var frameMat))
                return;

            try
            {
                // 通知訂閱者有新畫面可以處理
                OnFrameReady?.Invoke(frameMat);

                // 注意：frameMat的Dispose由訂閱者負責
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"偵測處理錯誤: {ex.Message}");
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
