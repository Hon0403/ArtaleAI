using ArtaleAI.Config;
using ArtaleAI.Core;
using ArtaleAI.GameWindow;
using System.Diagnostics;
using System.Drawing;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 路徑規劃管理器 - 統籌路徑追蹤、事件訂閱與狀態同步
    /// </summary>
    public class PathPlanningManager : IDisposable
    {
        private readonly PathPlanningTracker _tracker;
        private readonly AppConfig _config;
        private bool _isRunning;

        // 事件轉發 - 供外部訂閱
        public event Action<MinimapTrackingResult>? OnTrackingUpdated;
        public event Action<PathPlanningState>? OnPathStateChanged;
        public event Action<SdPoint>? OnWaypointReached;

        // 當前狀態快照
        public PathPlanningState? CurrentState => _tracker.CurrentPathState;
        public bool IsRunning => _isRunning;

        public PathPlanningManager(PathPlanningTracker tracker, AppConfig config)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 啟動路徑規劃追蹤
        /// </summary>
        public async Task StartAsync(string gameWindowTitle)
        {
            if (_isRunning)
            {
                Debug.WriteLine("[PathPlanningManager] 已在運行中");
                return;
            }

            var captureItem = WindowFinder.TryCreateItemForWindow(gameWindowTitle);
            if (captureItem == null)
            {
                throw new InvalidOperationException($"無法找到遊戲視窗: {gameWindowTitle}");
            }

            // 訂閱 Tracker 事件
            _tracker.OnTrackingUpdated += HandleTrackingUpdated;
            _tracker.OnPathStateChanged += HandlePathStateChanged;
            _tracker.OnWaypointReached += HandleWaypointReached;

            // 啟動追蹤
            _tracker.StartTracking(captureItem, _config.ContinuousDetectionIntervalMs);
            _isRunning = true;

            await Task.CompletedTask;
            Debug.WriteLine("[PathPlanningManager] 路徑規劃已啟動");
        }

        /// <summary>
        /// 停止路徑規劃追蹤
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                Debug.WriteLine("[PathPlanningManager] 尚未啟動");
                return;
            }

            // 取消訂閱
            _tracker.OnTrackingUpdated -= HandleTrackingUpdated;
            _tracker.OnPathStateChanged -= HandlePathStateChanged;
            _tracker.OnWaypointReached -= HandleWaypointReached;

            _tracker.StopTracking();
            _isRunning = false;

            await Task.CompletedTask;
            Debug.WriteLine("[PathPlanningManager] 路徑規劃已停止");
        }

        /// <summary>
        /// 載入規劃路徑
        /// </summary>
        public void LoadPlannedPath(List<SdPoint> waypoints)
        {
            if (waypoints == null || waypoints.Count < 2)
            {
                throw new ArgumentException("路徑點至少需要2個");
            }

            _tracker.SetPlannedPath(waypoints);
            Debug.WriteLine($"[PathPlanningManager] 已載入 {waypoints.Count} 個路徑點");
        }

        /// <summary>
        /// 手動處理追蹤結果(供 LiveView 調用)
        /// </summary>
        public void ProcessTrackingResult(MinimapTrackingResult result)
        {
            _tracker.ProcessTrackingResult(result);
        }

        // 私有事件處理器
        private void HandleTrackingUpdated(MinimapTrackingResult result)
        {
            OnTrackingUpdated?.Invoke(result);
        }

        private void HandlePathStateChanged(PathPlanningState state)
        {
            OnPathStateChanged?.Invoke(state);
        }

        private void HandleWaypointReached(SdPoint waypoint)
        {
            Debug.WriteLine($"[PathPlanningManager] 已到達路徑點: ({waypoint.X}, {waypoint.Y})");
            OnWaypointReached?.Invoke(waypoint);
        }

        public void Dispose()
        {
            if (_isRunning)
            {
                StopAsync().GetAwaiter().GetResult();
            }
            _tracker?.Dispose();
        }
    }
}
