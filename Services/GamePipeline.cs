using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using ArtaleAI.Models.Config;
using ArtaleAI.Core;
using ArtaleAI.Core.Vision;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Utils;
using OpenCvSharp;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Services
{
    // ============================================================
    // 架構考量：
    // GamePipeline 是從 MainForm.OnFrameAvailable 提取出來的「遊戲迴圈協調器」。
    // 它負責每幀的 5 步處理流程：
    //   1. 小地圖追蹤
    //   2. 自動攻擊決策
    //   3. 路徑規劃觸發
    //   4. 血條偵測排程
    //   5. 怪物偵測排程
    //
    // 此類別不引用任何 System.Windows.Forms 元件。
    // UI 更新全部透過 OnFrameProcessed 事件通知 MainForm 訂閱處理。
    //
    // 線程安全：
    // 所有偵測結果使用 lock 保護（從 MainForm 原封不動搬遷），
    // Pipeline 在背景執行緒被 LiveViewManager 呼叫，結果事件需在 UI 執行緒消費。
    // ============================================================

    /// <summary>
    /// 每幀處理結果 — 供 MainForm / OverlayRenderer 訂閱使用
    /// </summary>
    public class FrameProcessingResult
    {
        public List<SdRect> BloodBars { get; init; } = new();
        public List<SdRect> DetectionBoxes { get; init; } = new();
        public List<SdRect> AttackRangeBoxes { get; init; } = new();
        public List<DetectionResult> Monsters { get; init; } = new();
        public List<SdRect> MinimapBoxes { get; init; } = new();
        public List<SdRect> MinimapMarkers { get; init; } = new();
        public string? StatusMessage { get; set; }
    }

    /// <summary>
    /// 遊戲迴圈協調器 — Application 層核心
    /// </summary>
    public class GamePipeline
    {
        private readonly GameVisionCore _gameVision;
        private readonly PathPlanningManager? _pathPlanningManager;
        private readonly CharacterMovementController? _movementController;

        // 偵測結果（線程安全存取）
        private readonly object _bloodBarLock = new();
        private readonly object _monsterLock = new();
        private readonly object _minimapBoxLock = new();
        private readonly object _minimapMarkerLock = new();

        private List<SdRect> _currentBloodBars = new();
        private List<SdRect> _currentDetectionBoxes = new();
        private List<SdRect> _currentAttackRangeBoxes = new();
        private List<DetectionResult> _currentMonsters = new();
        private List<SdRect> _currentMinimapBoxes = new();
        private List<SdRect> _currentMinimapMarkers = new();

        // 偵測排程計時器
        private DateTime _lastBloodBarDetection = DateTime.MinValue;
        private DateTime _lastMonsterDetection = DateTime.MinValue;

        // 攻擊狀態
        private volatile bool _isAttacking = false;
        private DateTime _lastAttackTime = DateTime.MinValue;
        private DateTime _lastDirectionChangeTime = DateTime.MinValue;
        private const int AttackCooldownMs = 500;
        private const int DirectionChangeCooldownMs = 200;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_LEFT = 0x25;
        private const ushort VK_RIGHT = 0x27;

        // 幀驅動同步機制 (Vision-Driven Sync)
        private readonly SemaphoreSlim _frameSignal = new SemaphoreSlim(0, 1);
        private volatile bool _visionDataReady = false;

        /// <summary>
        /// 提供給執行層的同步介面：等待下一幀視覺數據處理完成。
        /// </summary>
        public async Task WaitForNextFrameAsync(CancellationToken ct)
        {
            try { await _frameSignal.WaitAsync(ct); }
            catch (OperationCanceledException) { }
        }

        // ============================================================
        // 外部可配置狀態（由 MainForm 透過屬性設定）
        // ============================================================

        /// <summary>自動攻擊是否啟用（由 UI 執行緒更新）</summary>
        public volatile bool AutoAttackEnabled;

        /// <summary>已選擇的怪物名稱</summary>
        public string SelectedMonsterName { get; set; } = string.Empty;

        /// <summary>已載入的怪物模板（由 UI 管理）</summary>
        public List<Mat> MonsterTemplates { get; set; } = new();

        /// <summary>幀驅動同步：視覺處理完成旗標</summary>
        public bool VisionDataReady
        {
            get => _visionDataReady;
            set => _visionDataReady = value;
        }

        /// <summary>攻擊中狀態（供外部查詢）</summary>
        public bool IsAttacking => _isAttacking;

        // ============================================================
        // 事件 — UI 層訂閱來更新畫面
        // ============================================================

        /// <summary>每幀處理完成後觸發，攜帶所有偵測結果的快照</summary>
        public event Action<FrameProcessingResult>? OnFrameProcessed;

        /// <summary>狀態訊息事件（取代直接呼叫 MsgLog.ShowStatus）</summary>
        public event Action<string>? OnStatusMessage;

        /// <summary>路徑規劃追蹤結果事件（傳遞給 OnPathTrackingUpdated）</summary>
        public event Action<MinimapTrackingResult>? OnPathTrackingResult;

        public GamePipeline(
            GameVisionCore gameVision,
            PathPlanningManager? pathPlanningManager = null,
            CharacterMovementController? movementController = null)
        {
            _gameVision = gameVision ?? throw new ArgumentNullException(nameof(gameVision));
            _pathPlanningManager = pathPlanningManager;
            _movementController = movementController;
        }

        // ============================================================
        // 外部設定方法
        // ============================================================

        /// <summary>更新小地圖邊界範圍（由 MainForm 傳入）</summary>
        public void SetMinimapBoxes(List<SdRect> boxes)
        {
            lock (_minimapBoxLock) { _currentMinimapBoxes = boxes.ToList(); }
        }

        /// <summary>取得當前偵測結果的線程安全快照</summary>
        public FrameProcessingResult GetCurrentSnapshot()
        {
            List<SdRect> bloodBars, detectionBoxes, attackRangeBoxes, minimapBoxes, minimapMarkers;
            List<DetectionResult> monsters;

            lock (_bloodBarLock) bloodBars = _currentBloodBars.ToList();
            lock (_monsterLock) monsters = _currentMonsters.ToList();
            lock (_minimapBoxLock) minimapBoxes = _currentMinimapBoxes.ToList();
            lock (_minimapMarkerLock) minimapMarkers = _currentMinimapMarkers.ToList();
            detectionBoxes = _currentDetectionBoxes.ToList();
            attackRangeBoxes = _currentAttackRangeBoxes.ToList();

            return new FrameProcessingResult
            {
                BloodBars = bloodBars,
                DetectionBoxes = detectionBoxes,
                AttackRangeBoxes = attackRangeBoxes,
                Monsters = monsters,
                MinimapBoxes = minimapBoxes,
                MinimapMarkers = minimapMarkers
            };
        }

        // ============================================================
        // 核心：每幀處理入口
        // 架構考量：這是原 MainForm.OnFrameAvailable L821-1016 的乾淨替代品
        // ============================================================

        /// <summary>
        /// 處理一幀畫面 — 由 LiveViewManager.OnFrameReady 觸發
        /// </summary>
        /// <param name="frameMat">當前幀的 Mat（呼叫者負責 using）</param>
        /// <param name="captureTime">畫面擷取時間戳</param>
        /// <param name="config">應用程式配置</param>
        public void ProcessFrame(Mat frameMat, DateTime captureTime, AppConfig config)
        {
            if (frameMat == null || frameMat.Empty()) return;

            try
            {
                var now = DateTime.UtcNow;

                // ── Step 1: 小地圖追蹤與路徑規劃同步 ──
                MinimapTrackingResult? trackingResult = null;
                List<SdRect> minimapBoxes;
                lock (_minimapBoxLock) minimapBoxes = _currentMinimapBoxes.ToList();

                if (minimapBoxes.Any())
                {
                    trackingResult = _gameVision.GetMinimapTracking(frameMat, captureTime);
                    if (trackingResult != null)
                    {
                        ProcessMinimapTracking(trackingResult, minimapBoxes);
                    }
                }

                // ── Step 2: 自動攻擊決策 ──
                bool attackTriggered = false;
                if (AutoAttackEnabled)
                {
                    attackTriggered = ProcessAutoAttackDecision();
                }

                // ── Step 3: 路徑規劃觸發 ──
                if (!attackTriggered)
                {
                    _isAttacking = false;
                    if (trackingResult != null)
                    {
                        ProcessPathPlanningUpdate(trackingResult);
                    }
                }

                // ── Step 4: 血條偵測排程 ──
                ProcessBloodBarDetection(frameMat, config, now);

                // ── Step 5: 怪物偵測排程 ──
                ProcessMonsterDetection(frameMat, config, now);

                // 發布結果快照事件
                var result = GetCurrentSnapshot();
                OnFrameProcessed?.Invoke(result);
            }
            catch (Exception ex)
            {
                Logger.Error($"[GamePipeline] 處理畫面錯誤: {ex.Message}");
            }
            finally
            {
                // 🚀 核心同步啟動：確保無論處理成功與否，都喚醒等待中的移動執行緒
                if (_frameSignal.CurrentCount == 0)
                {
                    _frameSignal.Release();
                }
            }
        }

        // ============================================================
        // Step 1: 小地圖追蹤
        // ============================================================

        private void ProcessMinimapTracking(MinimapTrackingResult? trackingResult, List<SdRect> minimapBoxes)
        {
            try
            {
                SdRect? minimapRect = minimapBoxes.FirstOrDefault();
                if (minimapRect.HasValue && !minimapRect.Value.IsEmpty)
                {
                    lock (_minimapMarkerLock)
                    {
                        _currentMinimapMarkers.Clear();
                        if (trackingResult?.PlayerPosition.HasValue == true)
                        {
                            var playerPos = trackingResult.PlayerPosition.Value;
                            var screenPlayerPos = new SdPoint(
                                minimapRect.Value.X + (int)playerPos.X,
                                minimapRect.Value.Y + (int)playerPos.Y);
                            _currentMinimapMarkers.Add(new SdRect(
                                screenPlayerPos.X - 5, screenPlayerPos.Y - 5, 10, 10));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[GamePipeline] 小地圖標記處理錯誤: {ex.Message}");
            }
        }

        // ============================================================
        // Step 2: 自動攻擊決策
        // 架構考量：攻擊判定純邏輯（碰撞偵測），按鍵發送透過 MovementController
        // ============================================================

        private bool ProcessAutoAttackDecision()
        {
            List<SdRect> attackRanges = _currentAttackRangeBoxes.ToList();
            List<DetectionResult> monsters;
            lock (_monsterLock) monsters = _currentMonsters.ToList();

            if (!attackRanges.Any() || !monsters.Any()) return false;

            SdRect playerAttackBox = attackRanges.FirstOrDefault();
            if (playerAttackBox.IsEmpty) return false;

            var targets = new List<DetectionResult>();
            foreach (var m in monsters)
            {
                if (playerAttackBox.IntersectsWith(m.BoundingBox))
                    targets.Add(m);
            }

            if (targets.Any())
            {
                _isAttacking = true;
                PerformAutoAttack(playerAttackBox, targets);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 執行自動攻擊（轉向 + 攻擊鍵）
        /// 從 MainForm.PerformAutoAttack L2462-2511 提取
        /// </summary>
        private async void PerformAutoAttack(SdRect playerBox, List<DetectionResult> targets)
        {
            if (targets == null || targets.Count == 0) return;
            if (_movementController == null) return;

            var now = DateTime.UtcNow;
            if ((now - _lastAttackTime).TotalMilliseconds < AttackCooldownMs) return;

            var playerCenter = new SdPoint(
                playerBox.X + playerBox.Width / 2,
                playerBox.Y + playerBox.Height / 2);
            var target = targets.OrderBy(m =>
                Math.Abs((m.BoundingBox.X + m.BoundingBox.Width / 2) - playerCenter.X)).FirstOrDefault();
            if (target == null) return;

            // 方向控制（冷卻時間防止頻繁轉向）
            if ((now - _lastDirectionChangeTime).TotalMilliseconds > DirectionChangeCooldownMs)
            {
                int monsterCenterX = target.BoundingBox.X + target.BoundingBox.Width / 2;
                ushort directionKey = monsterCenterX < playerCenter.X ? VK_LEFT : VK_RIGHT;

                _movementController.SendKeyInput(directionKey, false);
                await Task.Delay(20);
                _movementController.SendKeyInput(directionKey, true);
                _lastDirectionChangeTime = now;
            }

            // 攻擊
            _movementController.SendKeyInput(VK_CONTROL, false);
            await Task.Delay(20);
            _movementController.SendKeyInput(VK_CONTROL, true);

            _lastAttackTime = now;
            OnStatusMessage?.Invoke($"⚔️ 自動攻擊: 鎖定 {target.Name}");
        }

        /// <summary>
        /// 執行攻擊序列（停止移動→攻擊→等冷卻）
        /// 從 MainForm.PerformAutoAttackAsync L1202-1232 提取
        /// </summary>
        public async Task PerformAutoAttackSequenceAsync()
        {
            if (_isAttacking) return;
            _isAttacking = true;

            try
            {
                Logger.Info("[GamePipeline] 發現怪物在範圍內，暫停移動並攻擊");
                _movementController?.StopMovement();
                if (_movementController != null)
                    await _movementController.PerformAttackAsync(1000);
                Logger.Info("[GamePipeline] 攻擊完成，恢復移動");
            }
            catch (Exception ex)
            {
                Logger.Error($"[GamePipeline] 攻擊序列失敗: {ex.Message}");
            }
            finally
            {
                _isAttacking = false;
            }
        }

        // ============================================================
        // Step 3: 路徑規劃更新觸發
        // 架構考量：路徑規劃結果透過事件回傳，MainForm 訂閱後驅動 OnPathTrackingUpdated
        // ============================================================

        private void ProcessPathPlanningUpdate(MinimapTrackingResult result)
        {
            if (_pathPlanningManager == null || !_pathPlanningManager.IsRunning) return;

            try
            {
                if (_isAttacking) return;

                if (result != null)
                {
                    _pathPlanningManager.ProcessTrackingResult(result);
                    _visionDataReady = true;
                    OnPathTrackingResult?.Invoke(result);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[GamePipeline] ProcessPathPlanning 錯誤: {ex.Message}");
            }
        }

        // ============================================================
        // Step 4: 血條偵測排程
        // ============================================================

        private void ProcessBloodBarDetection(Mat frameMat, AppConfig config, DateTime now)
        {
            var elapsed = (now - _lastBloodBarDetection).TotalMilliseconds;
            int bloodBarCount;
            lock (_bloodBarLock) bloodBarCount = _currentBloodBars.Count;

            if (elapsed < config.Vision.BloodBarDetectIntervalMs && bloodBarCount > 0)
                return;

            try
            {
                var bloodBarResult = _gameVision.ProcessBloodBarDetection(frameMat, null);
                SdRect? bloodBar = bloodBarResult.Item1;
                var detectionBoxes = bloodBarResult.Item2 ?? new List<SdRect>();
                var attackRangeBoxes = bloodBarResult.Item3 ?? new List<SdRect>();

                if (bloodBar.HasValue)
                {
                    lock (_bloodBarLock)
                    {
                        _currentBloodBars.Clear();
                        _currentBloodBars.Add(bloodBar.Value);
                    }
                    _currentDetectionBoxes = detectionBoxes;
                    _currentAttackRangeBoxes = attackRangeBoxes;
                    _lastBloodBarDetection = now;
                }
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"血條偵測錯誤: {ex.Message}");
            }
        }

        // ============================================================
        // Step 5: 怪物偵測排程
        // 從 MainForm.ProcessMonsters L1022-1132 提取
        // ============================================================

        private void ProcessMonsterDetection(Mat frameMat, AppConfig config, DateTime now)
        {
            var elapsed = (now - _lastMonsterDetection).TotalMilliseconds;
            int monsterCount;
            lock (_monsterLock) monsterCount = _currentMonsters.Count;

            if (elapsed < config.Vision.MonsterDetectIntervalMs && monsterCount > 0) return;

            if (!_currentDetectionBoxes.Any()) return;
            if (string.IsNullOrEmpty(SelectedMonsterName) || !MonsterTemplates.Any()) return;

            try
            {
                var allResults = new List<DetectionResult>();
                var frameBounds = new Rect(0, 0, frameMat.Width, frameMat.Height);

                var detectionModeString = config.Vision.DetectionMode ?? "Color";
                if (!Enum.TryParse<MonsterDetectionMode>(detectionModeString, out var detectionMode))
                    detectionMode = MonsterDetectionMode.Color;

                foreach (var detectionBox in _currentDetectionBoxes)
                {
                    var cropRect = new Rect(detectionBox.X, detectionBox.Y, detectionBox.Width, detectionBox.Height);
                    var validCropRect = frameBounds.Intersect(cropRect);

                    if (validCropRect.Width < 10 || validCropRect.Height < 10) continue;

                    using var croppedMat = frameMat[validCropRect].Clone();

                    double detectionThreshold = config.Vision.DefaultThreshold;

                    var results = _gameVision.FindMonsters(
                        croppedMat, MonsterTemplates, detectionMode,
                        detectionThreshold, SelectedMonsterName)
                        ?? new List<DetectionResult>();

                    foreach (var r in results)
                    {
                        allResults.Add(new DetectionResult(
                            r.Name,
                            new SdPoint(r.Position.X + validCropRect.X, r.Position.Y + validCropRect.Y),
                            r.Size, r.Confidence,
                            new SdRect(r.Position.X + validCropRect.X, r.Position.Y + validCropRect.Y,
                                r.Size.Width, r.Size.Height)));
                    }
                }

                // NMS 去重
                if (allResults.Count > 1)
                {
                    var dedupedResults = GameVisionCore.ApplyNMS(allResults, iouThreshold: 0.3, higherIsBetter: true)
                        .OrderByDescending(r => r.Confidence).ToList();

                    if (config.Vision.MaxDetectionResults > 0 && dedupedResults.Count > config.Vision.MaxDetectionResults)
                        dedupedResults = dedupedResults.Take(config.Vision.MaxDetectionResults).ToList();

                    lock (_monsterLock) { _currentMonsters = dedupedResults; }
                    OnStatusMessage?.Invoke($"檢測到 {dedupedResults.Count} 個怪物 (原始: {allResults.Count})");
                }
                else
                {
                    lock (_monsterLock) { _currentMonsters = allResults; }
                }

                _lastMonsterDetection = DateTime.UtcNow;

                // 自動攻擊檢查
                CheckAutoAttackCondition();
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"怪物檢測錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 自動攻擊條件檢查
        /// 從 MainForm.CheckAutoAttackCondition L1161-1196 提取
        /// </summary>
        private void CheckAutoAttackCondition()
        {
            if (!AutoAttackEnabled || _isAttacking) return;

            List<DetectionResult> monsters;
            List<SdRect> attackRanges;

            lock (_monsterLock) monsters = _currentMonsters.ToList();
            attackRanges = _currentAttackRangeBoxes.ToList();

            if (!monsters.Any() || !attackRanges.Any()) return;

            foreach (var monster in monsters)
            {
                var monsterRect = new SdRect(
                    monster.Position.X, monster.Position.Y,
                    monster.Size.Width, monster.Size.Height);
                foreach (var range in attackRanges)
                {
                    if (range.IntersectsWith(monsterRect))
                    {
                        _ = PerformAutoAttackSequenceAsync();
                        return;
                    }
                }
            }
        }
    }
}
