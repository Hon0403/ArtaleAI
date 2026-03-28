using ArtaleAI.Models.Config;
using ArtaleAI.Core;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Utils;
using System.Drawing;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
{
    /// <summary>訂閱 <see cref="PathPlanningTracker"/> 事件並對外轉發路徑狀態。</summary>
    public class PathPlanningManager : IDisposable
    {
        private readonly PathPlanningTracker _tracker;
        private readonly AppConfig _config;
        private bool _isRunning;

        private Action<PathPlanningState>? _pathStateChangedHandler;
        private Action<SdPointF>? _waypointReachedHandler;

        public event Action<PathPlanningState>? OnPathStateChanged;

        public event Action<SdPointF>? OnWaypointReached;

        public string MapDataDirectory => "MapData";
        public string MonstersDirectory => "Monsters";


        public PathPlanningTracker Tracker => _tracker;

        public PathPlanningState? CurrentState => _tracker.CurrentPathState;

        public bool IsRunning => _isRunning;

        public PathPlanningManager(PathPlanningTracker tracker, AppConfig config)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>訂閱追蹤器事件並標記為運行中（實際幀資料由外部 <c>ProcessTrackingResult</c> 餵入）。</summary>
        /// <exception cref="InvalidOperationException">找不到遊戲視窗時。</exception>
        public Task StartAsync(string gameWindowTitle)
        {
            if (_isRunning)
            {
                Logger.Debug("[路徑規劃管理] 已在運行中");
                return Task.CompletedTask;
            }

            var captureItem = WindowFinder.TryCreateItemForWindow(gameWindowTitle);
            if (captureItem == null)
            {
                throw new InvalidOperationException($"無法找到遊戲視窗: {gameWindowTitle}");
            }

            _pathStateChangedHandler = state => OnPathStateChanged?.Invoke(state);
            _waypointReachedHandler = waypoint =>
            {
                Logger.Info($"[路徑規劃管理] 已到達路徑點: ({waypoint.X:F1}, {waypoint.Y:F1})");
                OnWaypointReached?.Invoke(waypoint);
            };

            _tracker.OnPathStateChanged += _pathStateChangedHandler;
            _tracker.OnWaypointReached += _waypointReachedHandler;


            _isRunning = true;

            Logger.Info("[路徑規劃管理] 路徑規劃已啟動");
            return Task.CompletedTask;
        }

        /// <summary>取消事件訂閱並清除運行旗標。</summary>
        public Task StopAsync()
        {
            if (!_isRunning)
            {
                Logger.Debug("[路徑規劃管理] 尚未啟動");
                return Task.CompletedTask;
            }

            if (_pathStateChangedHandler != null)
                _tracker.OnPathStateChanged -= _pathStateChangedHandler;
            if (_waypointReachedHandler != null)
                _tracker.OnWaypointReached -= _waypointReachedHandler;

            _isRunning = false;

            Logger.Info("[路徑規劃管理] 路徑規劃已停止");
            return Task.CompletedTask;
        }

        public void LoadMap(ArtaleAI.Models.Map.MapData mapData)
        {
            if (mapData == null) return;
            _tracker.LoadMap(mapData);
            Logger.Info("[路徑規劃管理] 已載入導航圖");
        }


        /// <summary>將一幀小地圖追蹤結果交給內部 <see cref="PathPlanningTracker"/>。</summary>
        public void ProcessTrackingResult(MinimapTrackingResult result)
        {
            _tracker.ProcessTrackingResult(result);
        }

        /// <summary>若仍在運行則非同步停止後釋放追蹤器。</summary>
        public void Dispose()
        {
            if (_isRunning)
            {
                try
                {
                    Task.Run(async () => await StopAsync()).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Logger.Error($"[路徑規劃管理] Dispose 時停止追蹤失敗: {ex.Message}");
                }
            }
            _tracker?.Dispose();
        }
    }
}
