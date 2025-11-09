using ArtaleAI.Config;
using ArtaleAI.Engine;
using ArtaleAI.GameWindow;
using System.Diagnostics;
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
        private bool _isTracking = false;

        // 路徑規劃狀態
        public PathPlanningState? CurrentPathState { get; private set; }
        private readonly List<MinimapTrackingResult> _trackingHistory;

        // 事件
        public event Action<MinimapTrackingResult>? OnTrackingUpdated;
        public event Action<PathPlanningState>? OnPathStateChanged;
        public event Action<SdPoint>? OnWaypointReached;
        private readonly GameVisionCore _gameVision;
        public PathPlanningTracker(GameVisionCore gameVision)
        {
            _gameVision = gameVision;
        }

        /// <summary>
        /// 開始路徑追蹤
        /// </summary>
        public void StartTracking(GraphicsCaptureItem captureItem, int? intervalMs = null)
        {
            if (_isTracking) return;
            _isTracking = true;
            Debug.WriteLine("🚀 路徑追蹤已啟動");
        }

        /// <summary>
        /// 停止路徑追蹤
        /// </summary>
        public void StopTracking()
        {
            _isTracking = false;
            Debug.WriteLine("⏹️ 路徑追蹤已停止");
        }

        /// <summary>
        /// 設定路徑規劃目標
        /// </summary>
        public void SetPlannedPath(List<SdPoint> path)
        {
            CurrentPathState = new PathPlanningState
            {
                PlannedPath = new List<SdPoint>(path),
                CurrentWaypointIndex = 0,
                IsPathCompleted = false
            };

            Debug.WriteLine($"🗺️ 設定路徑規劃，共 {path.Count} 個路徑點");
            OnPathStateChanged?.Invoke(CurrentPathState);
        }

        /// <summary>
        /// 處理來自主程式的追蹤結果
        /// </summary>
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
        /// </summary>
        private void UpdateTrackingHistory(MinimapTrackingResult result)
        {
            _trackingHistory.Add(result);

            var maxHistory = AppConfig.Instance.MaxTrackingHistory;
            if (_trackingHistory.Count > maxHistory)
            {
                _trackingHistory.RemoveAt(0);
            }
        }

        /// <summary>
        /// 更新路徑規劃狀態
        /// </summary>
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
                Debug.WriteLine($"✅ 到達路徑點 {CurrentPathState.CurrentWaypointIndex}: {nextWaypoint.Value}");
                OnWaypointReached?.Invoke(nextWaypoint.Value);

                // 移動到下個路徑點
                CurrentPathState.CurrentWaypointIndex++;

                // 檢查路徑是否完成
                if (CurrentPathState.CurrentWaypointIndex >= CurrentPathState.PlannedPath.Count)
                {
                    CurrentPathState.IsPathCompleted = true;
                    Debug.WriteLine("🏁 路徑規劃完成！");
                }

                OnPathStateChanged?.Invoke(CurrentPathState);
            }
        }

        /// <summary>
        /// 計算兩點距離
        /// </summary>
        private double CalculateDistance(SdPoint p1, SdPoint p2)
        {
            var dx = p1.X - p2.X;
            var dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 取得追蹤歷史記錄
        /// </summary>
        public List<MinimapTrackingResult> GetTrackingHistory()
        {
            return new List<MinimapTrackingResult>(_trackingHistory);
        }

        public void Dispose()
        {
            StopTracking();
            Debug.WriteLine("🗑️ PathPlanningTracker 已釋放");
        }
    }
}
