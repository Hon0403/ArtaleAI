using ArtaleAI.Config;
using ArtaleAI.Services;
using System.Diagnostics;
using System.Linq;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using Timer = System.Threading.Timer;

namespace ArtaleAI.Core
{
    /// <summary>
    /// 路徑規劃追蹤器 - 負責持續檢測玩家位置和路徑狀態
    /// </summary>
    public class PathPlanningTracker : IDisposable
    {
        // Readonly 欄位
        private readonly GameVisionCore _gameVision;
        private readonly Random _random = new Random();
        private readonly object _randomLock = new object(); // 執行緒安全鎖
        private readonly List<MinimapTrackingResult> _trackingHistory;
        private readonly object _historyLock = new object(); // 執行緒安全鎖

        // 可變欄位
        private bool _isTracking = false;

        /// <summary>
        /// 取得當前路徑規劃狀態
        /// 包含路徑點列表、當前進度、完成狀態等資訊
        /// </summary>
        public PathPlanningState? CurrentPathState { get; private set; }

        /// <summary>
        /// 追蹤更新事件 - 每次檢測到新的玩家位置時觸發
        /// </summary>
        public event Action<MinimapTrackingResult>? OnTrackingUpdated;
        
        /// <summary>
        /// 路徑狀態變更事件 - 當切換路徑點或路徑完成時觸發
        /// </summary>
        public event Action<PathPlanningState>? OnPathStateChanged;
        
        /// <summary>
        /// 路徑點到達事件 - 當角色到達路徑點時觸發
        /// </summary>
        public event Action<SdPoint>? OnWaypointReached;
        
        /// <summary>
        /// 初始化路徑規劃追蹤器
        /// </summary>
        /// <param name="gameVision">遊戲視覺核心實例（用於小地圖追蹤）</param>
        public PathPlanningTracker(GameVisionCore gameVision)
        {
            _gameVision = gameVision ?? throw new ArgumentNullException(nameof(gameVision));
            _trackingHistory = new List<MinimapTrackingResult>();
        }

        /// <summary>
        /// 開始路徑追蹤
        /// 啟動追蹤狀態標記（實際追蹤由外部 LiveView 提供畫面）
        /// </summary>
        /// <param name="captureItem">擷取項目（暫不使用，保留用於未來擴充）</param>
        /// <param name="intervalMs">追蹤間隔毫秒數（可選）</param>
        public void StartTracking(GraphicsCaptureItem captureItem, int? intervalMs = null)
        {
            if (_isTracking) return;
            _isTracking = true;
            Debug.WriteLine("路徑追蹤已啟動");
        }

        /// <summary>
        /// 停止路徑追蹤
        /// </summary>
        public void StopTracking()
        {
            _isTracking = false;
            Debug.WriteLine("路徑追蹤已停止");
        }

        /// <summary>
        /// 設定路徑規劃目標路徑
        /// 建立新的路徑規劃狀態並初始化進度追蹤（使用隨機模式）
        /// </summary>
        /// <param name="path">路徑點列表</param>
        public void SetPlannedPath(List<SdPoint> path)
        {
            CurrentPathState = new PathPlanningState
            {
                PlannedPath = new List<SdPoint>(path),
                CurrentWaypointIndex = 0,
                IsPathCompleted = false
            };

            // 隨機選擇第一個目標點（執行緒安全）
            if (path.Count > 0)
            {
                lock (_randomLock)
                {
                    CurrentPathState.CurrentWaypointIndex = _random.Next(0, path.Count);
                }
            }

            Debug.WriteLine($"設定路徑規劃（隨機模式），共 {path.Count} 個路徑點");
            OnPathStateChanged?.Invoke(CurrentPathState);
        }

        /// <summary>
        /// 處理來自主程式的追蹤結果
        /// 更新追蹤歷史、檢查路徑狀態並觸發相應事件
        /// </summary>
        /// <param name="result">小地圖追蹤結果（包含玩家位置等資訊）</param>
        public void ProcessTrackingResult(MinimapTrackingResult result)
        {
            if (!_isTracking || result == null) return;

            // 更新追蹤歷史
            UpdateTrackingHistory(result);

            // 更新路徑狀態
            UpdatePathState(result);

            // 觸發事件
            OnTrackingUpdated?.Invoke(result);
        }

        /// <summary>
        /// 更新追蹤歷史記錄
        /// 將新的追蹤結果加入歷史，超過上限時移除最舊的記錄
        /// </summary>
        /// <param name="result">追蹤結果</param>
        private void UpdateTrackingHistory(MinimapTrackingResult result)
        {
            lock (_historyLock)
            {
                _trackingHistory.Add(result);

                var maxHistory = AppConfig.Instance.MaxTrackingHistory;
                if (_trackingHistory.Count > maxHistory)
                {
                    _trackingHistory.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// 更新路徑規劃狀態
        /// 計算玩家到下一個路徑點的距離，檢查是否到達並更新進度
        /// </summary>
        /// <param name="trackingResult">追蹤結果（包含玩家當前位置）</param>
        private void UpdatePathState(MinimapTrackingResult trackingResult)
        {
            if (CurrentPathState == null || CurrentPathState.IsPathCompleted) return;

            var playerPos = trackingResult.PlayerPosition;
            if (!playerPos.HasValue || playerPos.Value == SdPoint.Empty) return;

            var nextWaypoint = CurrentPathState.NextWaypoint;
            if (!nextWaypoint.HasValue) return;

            // 計算到下個路徑點的距離
            var distance = CalculateDistance(playerPos.Value, nextWaypoint.Value);
            CurrentPathState.DistanceToNextWaypoint = distance;

            var reachDistance = AppConfig.Instance.WaypointReachDistance;

            // 檢查是否到達路徑點
            if (distance <= reachDistance)
            {
                Debug.WriteLine($" 到達路徑點 {CurrentPathState.CurrentWaypointIndex}: {nextWaypoint.Value}");
                OnWaypointReached?.Invoke(nextWaypoint.Value);

                // 隨機模式：從所有路徑點中隨機選擇一個（不包括當前點，執行緒安全）
                if (CurrentPathState.PlannedPath.Count > 1)
                {
                    var availableIndices = Enumerable.Range(0, CurrentPathState.PlannedPath.Count)
                        .Where(i => i != CurrentPathState.CurrentWaypointIndex)
                        .ToList();
                    lock (_randomLock)
                    {
                        CurrentPathState.CurrentWaypointIndex = availableIndices[_random.Next(availableIndices.Count)];
                    }
                }
                else
                {
                    // 只有一個路徑點時，標記為完成
                    CurrentPathState.IsPathCompleted = true;
                }

                OnPathStateChanged?.Invoke(CurrentPathState);
            }
        }

        /// <summary>
        /// 計算兩點之間的歐幾里得距離
        /// 使用畢氏定理計算直線距離
        /// </summary>
        /// <param name="p1">第一個點</param>
        /// <param name="p2">第二個點</param>
        /// <returns>兩點之間的距離（像素）</returns>
        private double CalculateDistance(SdPoint p1, SdPoint p2)
        {
            var dx = p1.X - p2.X;
            var dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 取得追蹤歷史記錄的副本
        /// 返回所有儲存的追蹤結果列表
        /// </summary>
        /// <returns>追蹤結果歷史列表的副本</returns>
        public List<MinimapTrackingResult> GetTrackingHistory()
        {
            lock (_historyLock)
            {
                return new List<MinimapTrackingResult>(_trackingHistory);
            }
        }

        /// <summary>
        /// 釋放路徑規劃追蹤器的資源
        /// 停止追蹤並清理相關資料
        /// </summary>
        public void Dispose()
        {
            StopTracking();
            lock (_historyLock)
            {
                _trackingHistory.Clear();
            }
            CurrentPathState = null;
            Debug.WriteLine("PathPlanningTracker 已釋放");
        }
    }
}
