using ArtaleAI.Config;
using ArtaleAI.Core;
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

        // 儲存 lambda 引用以便取消訂閱
        private Action<MinimapTrackingResult>? _trackingUpdatedHandler;
        private Action<PathPlanningState>? _pathStateChangedHandler;
        private Action<SdPoint>? _waypointReachedHandler;

        /// <summary>
        /// 追蹤更新事件 - 當小地圖追蹤有新資料時觸發
        /// </summary>
        public event Action<MinimapTrackingResult>? OnTrackingUpdated;
        
        /// <summary>
        /// 路徑狀態變更事件 - 當路徑點切換或路徑完成時觸發
        /// </summary>
        public event Action<PathPlanningState>? OnPathStateChanged;
        
        /// <summary>
        /// 路徑點到達事件 - 當角色到達指定路徑點時觸發
        /// </summary>
        public event Action<SdPoint>? OnWaypointReached;

        /// <summary>
        /// 取得當前路徑規劃狀態快照
        /// </summary>
        public PathPlanningState? CurrentState => _tracker.CurrentPathState;
        
        /// <summary>
        /// 檢查路徑規劃是否正在運行
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 初始化路徑規劃管理器
        /// </summary>
        /// <param name="tracker">路徑追蹤器實例</param>
        /// <param name="config">應用程式設定</param>
        public PathPlanningManager(PathPlanningTracker tracker, AppConfig config)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 啟動路徑規劃追蹤系統
        /// 自動尋找遊戲視窗並開始追蹤小地圖玩家位置
        /// </summary>
        /// <param name="gameWindowTitle">遊戲視窗標題</param>
        /// <exception cref="InvalidOperationException">找不到指定的遊戲視窗時拋出</exception>
        public Task StartAsync(string gameWindowTitle)
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

            // 簡化：使用 lambda 訂閱（儲存引用以便取消訂閱）
            _trackingUpdatedHandler = result => OnTrackingUpdated?.Invoke(result);
            _pathStateChangedHandler = state => OnPathStateChanged?.Invoke(state);
            _waypointReachedHandler = waypoint =>
            {
                Debug.WriteLine($"[PathPlanningManager] 已到達路徑點: ({waypoint.X}, {waypoint.Y})");
                OnWaypointReached?.Invoke(waypoint);
            };

            _tracker.OnTrackingUpdated += _trackingUpdatedHandler;
            _tracker.OnPathStateChanged += _pathStateChangedHandler;
            _tracker.OnWaypointReached += _waypointReachedHandler;

            // 啟動追蹤
            _tracker.StartTracking(captureItem, _config.ContinuousDetectionIntervalMs);
            _isRunning = true;

            Debug.WriteLine("[PathPlanningManager] 路徑規劃已啟動");
        }

        /// <summary>
        /// 停止路徑規劃追蹤系統
        /// 取消所有事件訂閱並停止追蹤器
        /// </summary>
        public Task StopAsync()
        {
            if (!_isRunning)
            {
                Debug.WriteLine("[PathPlanningManager] 尚未啟動");
                return;
            }

            // 取消訂閱（使用儲存的 lambda 引用）
            if (_trackingUpdatedHandler != null)
                _tracker.OnTrackingUpdated -= _trackingUpdatedHandler;
            if (_pathStateChangedHandler != null)
                _tracker.OnPathStateChanged -= _pathStateChangedHandler;
            if (_waypointReachedHandler != null)
                _tracker.OnWaypointReached -= _waypointReachedHandler;

            _tracker.StopTracking();
            _isRunning = false;

            Debug.WriteLine("[PathPlanningManager] 路徑規劃已停止");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 載入規劃路徑到追蹤器
        /// 設定角色需要隨機走訪的路徑點列表
        /// </summary>
        /// <param name="waypoints">路徑點列表（至少需要2個點）</param>
        /// <exception cref="ArgumentException">路徑點數量少於2個時拋出</exception>
        public void LoadPlannedPath(List<SdPoint> waypoints)
        {
            if (waypoints == null || waypoints.Count < 2)
            {
                throw new ArgumentException("路徑點至少需要2個");
            }

            _tracker.SetPlannedPath(waypoints);
            Debug.WriteLine($"[PathPlanningManager] 已載入 {waypoints.Count} 個路徑點（隨機模式）");
        }

        /// <summary>
        /// 手動處理小地圖追蹤結果
        /// 供 LiveView 模式直接傳入追蹤資料使用
        /// </summary>
        /// <param name="result">小地圖追蹤結果</param>
        public void ProcessTrackingResult(MinimapTrackingResult result)
        {
            _tracker.ProcessTrackingResult(result);
        }

        /// <summary>
        /// 釋放路徑規劃管理器使用的所有資源
        /// 如果正在運行會自動停止追蹤
        /// </summary>
        public void Dispose()
        {
            if (_isRunning)
            {
                // 修復：避免死鎖，使用 Task.Run 在背景執行緒執行
                try
                {
                    Task.Run(async () => await StopAsync()).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PathPlanningManager] Dispose 時停止追蹤失敗: {ex.Message}");
                }
            }
            _tracker?.Dispose();
        }
    }
}
