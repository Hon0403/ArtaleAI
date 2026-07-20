using ArtaleAI.Vision;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Domain.Navigation;
using ArtaleAI.Infrastructure.Capture;
using ArtaleAI.Shared;

namespace ArtaleAI.Application.Navigation
{
    /// <summary>包裝 <see cref="PathPlanningTracker"/> 的啟動／停止與地圖載入。</summary>
    public class PathPlanningManager : IDisposable
    {
        private readonly PathPlanningTracker _tracker;
        private bool _isRunning;

        public PathPlanningTracker Tracker => _tracker;

        public PathPlanningState? CurrentState => _tracker.CurrentPathState;

        public bool IsRunning => _isRunning;

        /// <summary>是否有進行中的導航飛行（Walk／Climb／Jump 尚未完成）。</summary>
        public bool HasActiveNavigationFlight => _tracker.HasActiveNavigationFlight;

        /// <summary>進行中飛行的 ActionType；無飛行時為 null。</summary>
        public NavigationActionType? ActiveFlightActionType => _tracker.ActiveFlightActionType;

        public PathPlanningManager(PathPlanningTracker tracker)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        }

        /// <summary>驗證遊戲視窗存在後標記為運行中（幀資料由外部 <c>ProcessTrackingResult</c> 餵入）。</summary>
        public Task StartAsync(string gameWindowTitle)
        {
            if (_isRunning)
            {
                Logger.Debug("[路徑規劃管理] 已在運行中");
                return Task.CompletedTask;
            }

            var captureItem = WindowFinder.TryCreateItemForWindow(gameWindowTitle);
            if (captureItem == null)
                throw new InvalidOperationException($"無法找到遊戲視窗: {gameWindowTitle}");

            // 每次開打怪都清熔斷／Approach 黑名單，否則關過再開會被舊狀態擋死。
            _tracker.ResetFarmSessionState();
            _isRunning = true;
            Logger.Info("[路徑規劃管理] 路徑規劃已啟動");
            return Task.CompletedTask;
        }

        /// <summary>清除運行旗標，並重置打怪工作階段殘留狀態。</summary>
        public Task StopAsync()
        {
            if (!_isRunning)
            {
                Logger.Debug("[路徑規劃管理] 尚未啟動");
                return Task.CompletedTask;
            }

            _isRunning = false;
            _tracker.ResetFarmSessionState();
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
