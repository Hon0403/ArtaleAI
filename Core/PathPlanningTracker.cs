using ArtaleAI.Config;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Services;
using ArtaleAI.Utils;
using System.Diagnostics;
using System.Linq;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;
using Timer = System.Threading.Timer;

namespace ArtaleAI.Core
{
    /// <summary>
    /// 路徑規劃追蹤器 - 負責持續檢測玩家位置和路徑狀態
    /// </summary>
    public class PathPlanningTracker : IDisposable
    {
        #region 路徑規劃常數
        
        private const float BoundarySafetyMargin = 15f;           // 安全邊距（像素）
        private const float NarrowPlatformSafetyPercent = 0.10f;  // 窄平台安全邊距比例 (10%)
        private const float MinMoveDistancePercent = 0.20f;       // 最小移動距離比例 (20%)
        private const float ReachDistancePercent = 0.20f;         // 到達判定距離比例 (1/5)
        private const float MinReachDistance = 2.0f;              // 最小到達距離 (px)
        
        #endregion

        // 邊界處理相關欄位
        private PlatformBounds? _platformBounds;
        
        // Readonly 欄位
        private readonly GameVisionCore _gameVision;
        private readonly Random _random = new Random();
        private readonly object _randomLock = new object(); // 執行緒安全鎖
        private readonly List<MinimapTrackingResult> _trackingHistory;
        private readonly object _historyLock = new object(); // 執行緒安全鎖

        // 可變欄位 (volatile 確保多執行緒可見性)
        private volatile bool _isTracking = false;

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
        /// 邊界觸發事件 - 當角色接近或超出邊界時觸發
        /// 參數：(玩家位置, 邊界方向 "left"/"right")
        /// </summary>
        public event Action<SdPointF, string>? OnBoundaryHit;
        
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
        /// <param name="captureItem">擷取項目（保留用於未來擴充，目前未使用）</param>
        /// <param name="intervalMs">追蹤間隔毫秒數（保留用於未來擴充，目前未使用）</param>
        [System.Obsolete("captureItem 與 intervalMs 參數已由 LiveView 統一管理，保留供未來擴充")]
        public void StartTracking(GraphicsCaptureItem captureItem, int? intervalMs = null)
        {
            if (_isTracking) return;
            _isTracking = true;
            Logger.Info("[路徑追蹤] 路徑追蹤已啟動");
        }

        /// <summary>
        /// 停止路徑追蹤
        /// </summary>
        public void StopTracking()
        {
            _isTracking = false;
            Logger.Info("[路徑追蹤] 路徑追蹤已停止");
        }

        /// <summary>
        /// 設定平台邊界
        /// 用於隨機選點時過濾接近邊界的候選點
        /// </summary>
        /// <param name="minX">X 軸最小值（左邊界）</param>
        /// <param name="maxX">X 軸最大值（右邊界）</param>
        /// <param name="minY">Y 軸最小值（上邊界）</param>
        /// <param name="maxY">Y 軸最大值（下邊界）</param>
        public void SetBoundaries(float minX, float maxX, float minY, float maxY)
        {
            _platformBounds = new PlatformBounds
            {
                MinX = minX,
                MaxX = maxX,
                MinY = minY,
                MaxY = maxY
            };
            Logger.Info($"[路徑追蹤] 設定平台邊界：{_platformBounds}");
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

            Logger.Info($"[路徑追蹤] 設定路徑規劃（隨機模式），共 {path.Count} 個路徑點");
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
            // 使用區域快照避免多執行緒競爭條件
            var state = CurrentPathState;
            if (state == null || state.IsPathCompleted) return;

            var playerPos = trackingResult.PlayerPosition;
            if (!playerPos.HasValue || playerPos.Value == SdPointF.Empty) return;

            // 邊界檢測
            CheckBoundaryProximity(playerPos.Value);

            var nextWaypoint = state.NextWaypoint;
            if (!nextWaypoint.HasValue) return;

            // 計算到下個路徑點的距離
            var distance = CalculateDistance(playerPos.Value, nextWaypoint.Value);
            state.DistanceToNextWaypoint = distance;

            // 取得當前的動態判定距離（單一真理來源）
            float checkDistance = GetDynamicReachDistance();

            if (distance <= checkDistance)
            {
                Logger.Info($"[路徑追蹤] 到達路徑點 {state.CurrentWaypointIndex}: {{X={(int)nextWaypoint.Value.X},Y={(int)nextWaypoint.Value.Y}}} (距離 {distance:F1}px <= {checkDistance:F1}px)");
                
                // 到達目標，通知外部
                OnWaypointReached?.Invoke(new SdPoint((int)nextWaypoint.Value.X, (int)nextWaypoint.Value.Y));

                // 選擇下一個目標（隨機或循序）目標點（考慮邊界）
                SelectSafeRandomTarget();

                OnPathStateChanged?.Invoke(state);
            }
        }

        /// <summary>
        /// 檢查玩家是否接近或超出邊界，並觸發邊界事件
        /// </summary>
        /// <param name="playerPos">玩家當前位置</param>
        private void CheckBoundaryProximity(SdPointF playerPos)
        {
            if (_platformBounds == null) return;

            // 檢查是否超出邊界
            if (playerPos.X < _platformBounds.MinX)
            {
                Logger.Warning($"[邊界] 角色超出左邊界！X={playerPos.X:F1}, 範圍=[{_platformBounds.MinX:F1}, {_platformBounds.MaxX:F1}]");
                OnBoundaryHit?.Invoke(playerPos, "left");
                HandleBoundaryHit(playerPos, "left");
            }
            else if (playerPos.X > _platformBounds.MaxX)
            {
                Logger.Warning($"[邊界] 角色超出右邊界！X={playerPos.X:F1}, 範圍=[{_platformBounds.MinX:F1}, {_platformBounds.MaxX:F1}]");
                OnBoundaryHit?.Invoke(playerPos, "right");
                HandleBoundaryHit(playerPos, "right");
            }
        }

        /// <summary>
        /// 處理邊界碰撞：自動切換到遠離邊界的安全目標點
        /// </summary>
        private void HandleBoundaryHit(SdPointF playerPos, string boundary)
        {
            if (CurrentPathState == null || _platformBounds == null) return;

            var path = CurrentPathState.PlannedPath;
            float centerX = (_platformBounds.MinX + _platformBounds.MaxX) / 2f;

            // 根據邊界方向選擇反向的安全點
            var safeCandidates = path
                .Select((p, idx) => new { Point = p, Index = idx })
                .Where(x => {
                    float minSafeX = _platformBounds.MinX + BoundarySafetyMargin;
                    float maxSafeX = _platformBounds.MaxX - BoundarySafetyMargin;
                    return x.Point.X >= minSafeX && x.Point.X <= maxSafeX;
                })
                .ToList();

            if (safeCandidates.Count > 0)
            {
                // 選擇離當前邊界最遠的點
                var targetCandidate = boundary == "right"
                    ? safeCandidates.OrderBy(x => x.Point.X).First()  // 選最左邊的
                    : safeCandidates.OrderByDescending(x => x.Point.X).First();  // 選最右邊的

                CurrentPathState.CurrentWaypointIndex = targetCandidate.Index;
                Logger.Info($"[路徑追蹤] 邊界處理：切換到{(boundary == "right" ? "左" : "右")}側目標，索引 {targetCandidate.Index}, 座標 {targetCandidate.Point}");
            }
            else
            {
                // 找不到安全點，強制選擇中間點
                int middleIndex = path.Count / 2;
                CurrentPathState.CurrentWaypointIndex = middleIndex;
                Logger.Warning($"[路徑追蹤] 找不到安全目標點，強制選擇中間點 (索引 {middleIndex})");
            }

            OnPathStateChanged?.Invoke(CurrentPathState);
        }

        /// <summary>
        /// 選擇安全的隨機目標點（過濾接近邊界的候選點）
        /// </summary>
        private void SelectSafeRandomTarget()
        {
            if (CurrentPathState == null) return;

            var path = CurrentPathState.PlannedPath;
            if (path.Count <= 1)
            {
                CurrentPathState.IsPathCompleted = true;
                return;
            }

            // Step 1: 取得安全候選點
            var candidateIndices = GetSafeCandidateIndices(path);

            // Step 2: 過濾距離太近的點
            candidateIndices = FilterByMinimumMoveDistance(candidateIndices, path);

            // Step 3: 隨機選擇並套用
            ApplyRandomSelection(candidateIndices, path);
        }

        /// <summary>
        /// 取得安全候選點索引列表（過濾邊界附近的點）
        /// </summary>
        private List<int> GetSafeCandidateIndices(List<SdPoint> path)
        {
            // 取得所有候選索引（排除當前點）
            var candidateIndices = Enumerable.Range(0, path.Count)
                .Where(i => i != CurrentPathState!.CurrentWaypointIndex)
                .ToList();

            if (_platformBounds == null) return candidateIndices;

            // 計算平台寬度與動態安全邊距
            float platformWidth = _platformBounds.MaxX - _platformBounds.MinX;
            float safetyMargin = BoundarySafetyMargin;
            
            if (platformWidth < BoundarySafetyMargin * 2)
            {
                safetyMargin = platformWidth * NarrowPlatformSafetyPercent;
                Logger.Warning($"[路徑規劃] 平台過窄 ({platformWidth:F1}px)，自動縮小安全邊距至 {safetyMargin:F1}px");
            }

            float minSafeX = _platformBounds.MinX + safetyMargin;
            float maxSafeX = _platformBounds.MaxX - safetyMargin;

            var safeCandidates = candidateIndices
                .Where(idx => path[idx].X >= minSafeX && path[idx].X <= maxSafeX)
                .ToList();

            if (safeCandidates.Count > 0) return safeCandidates;

            // 候選點都不安全，搜尋最接近中心的安全點
            Logger.Warning("[路徑規劃] 候選點都不安全，搜尋替代點...");
            float centerX = (_platformBounds.MinX + _platformBounds.MaxX) / 2f;
            var closestToCenter = path
                .Select((p, idx) => new { Point = p, Index = idx })
                .Where(x => x.Point.X >= minSafeX && x.Point.X <= maxSafeX)
                .OrderBy(x => Math.Abs(x.Point.X - centerX))
                .FirstOrDefault();

            if (closestToCenter != null)
            {
                Logger.Info($"[路徑規劃] 找到安全替代點：索引={closestToCenter.Index}");
                return new List<int> { closestToCenter.Index };
            }

            return candidateIndices; // 無安全點，回傳原始列表
        }

        /// <summary>
        /// 過濾距離太近的候選點（確保最小移動距離）
        /// </summary>
        private List<int> FilterByMinimumMoveDistance(List<int> candidateIndices, List<SdPoint> path)
        {
            if (_platformBounds == null || CurrentPathState == null) return candidateIndices;

            var currentPos = path[CurrentPathState.CurrentWaypointIndex];
            float platformWidth = _platformBounds.MaxX - _platformBounds.MinX;
            float minMoveDistance = platformWidth * MinMoveDistancePercent;

            var farCandidates = candidateIndices
                .Where(idx => Math.Abs(path[idx].X - currentPos.X) >= minMoveDistance)
                .ToList();

            if (farCandidates.Count > 0)
            {
                Logger.Debug($"[路徑規劃] 距離過濾：保留 {farCandidates.Count}/{candidateIndices.Count} 個點");
                return farCandidates;
            }

            Logger.Warning($"[路徑規劃] 找不到夠遠的點，使用所有候選點");
            return candidateIndices;
        }

        /// <summary>
        /// 從候選列表中隨機選擇並套用目標點
        /// </summary>
        private void ApplyRandomSelection(List<int> candidateIndices, List<SdPoint> path)
        {
            if (CurrentPathState == null || candidateIndices.Count == 0) return;

            lock (_randomLock)
            {
                int selectedIdx = candidateIndices[_random.Next(candidateIndices.Count)];
                CurrentPathState.CurrentWaypointIndex = selectedIdx;
                Logger.Debug($"[路徑規劃] 選擇目標點: {path[selectedIdx]}");
            }
        }

        /// <summary>
        /// 計算兩點之間的歐幾里得距離
        /// 使用畢氏定理計算直線距離
        /// </summary>
        /// <param name="p1">第一個點（浮點數座標）</param>
        /// <param name="p2">第二個點（整數座標）</param>
        /// <returns>兩點之間的距離（像素）</returns>
        private double CalculateDistance(SdPointF p1, SdPoint p2)
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
            Logger.Debug("[路徑追蹤] PathPlanningTracker 已釋放");
        }
        /// <summary>
        /// 取得動態判定距離（Single Source of Truth）
        /// 根據當前平台寬度和設定值，計算出最適合的到達判定距離
        /// </summary>
        public float GetDynamicReachDistance()
        {
            float reachDistance = (float)AppConfig.Instance.WaypointReachDistance;
            
            if (_platformBounds != null)
            {
                float platformWidth = _platformBounds.MaxX - _platformBounds.MinX;
                // 如果平台很小，判定距離最多只能是寬度的 1/5
                float maxAllowedDist = platformWidth / 5.0f;
                if (reachDistance > maxAllowedDist)
                {
                    // 至少保留 2.0px，除非平台真的很小
                    return Math.Max(2.0f, maxAllowedDist);
                }
            }
            return reachDistance;
        }

    }
}
