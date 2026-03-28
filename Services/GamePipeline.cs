using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <summary>單幀偵測與小地圖追蹤結果快照。</summary>
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

    /// <summary>每幀協調追蹤、路徑、攻擊與偵測；不依賴 WinForms。</summary>
    public class GamePipeline
    {
        private readonly GameVisionCore _gameVision;
        private readonly PathPlanningManager? _pathPlanningManager;
        private readonly CharacterMovementController? _movementController;

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

        private DateTime _lastBloodBarDetection = DateTime.MinValue;
        private DateTime _lastMonsterDetection = DateTime.MinValue;

        private volatile bool _isAttacking = false;
        private DateTime _lastAttackTime = DateTime.MinValue;
        private DateTime _lastDirectionChangeTime = DateTime.MinValue;
        private const int AttackCooldownMs = 500;
        private const int DirectionChangeCooldownMs = 200;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_LEFT = 0x25;
        private const ushort VK_RIGHT = 0x27;

        /// <summary>單幀處理超過此毫秒數寫入 Warning（Release 亦可見）。</summary>
        private const double PipelineSlowFrameWarnMs = 120;

#if DEBUG
        /// <summary>單幀處理超過此毫秒數寫入 Debug（僅 Debug 組建）。</summary>
        private const double PipelineSlowFrameDebugMs = 40;
#endif

        private readonly SemaphoreSlim _frameSignal = new SemaphoreSlim(0, 1);
        private volatile bool _visionDataReady = false;

        /// <summary>阻塞至本 pipeline 處理完下一幀並發信號。</summary>
        public async Task WaitForNextFrameAsync(CancellationToken ct)
        {
            try { await _frameSignal.WaitAsync(ct); }
            catch (OperationCanceledException) { }
        }

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

        /// <summary>處理單幀：追蹤、路徑、攻擊、血條／怪物偵測排程。</summary>
        public void ProcessFrame(Mat frameMat, DateTime captureTime, AppConfig config)
        {
            if (frameMat == null || frameMat.Empty()) return;

            var sw = Stopwatch.StartNew();
            try
            {
                var now = DateTime.UtcNow;
                double captureLagMs = (now - captureTime).TotalMilliseconds;

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

                double msAfterMinimap = sw.Elapsed.TotalMilliseconds;

                bool attackTriggered = false;
                if (AutoAttackEnabled)
                {
                    attackTriggered = ProcessAutoAttackDecision();
                }

                if (!attackTriggered)
                {
                    _isAttacking = false;
                    if (trackingResult != null)
                    {
                        ProcessPathPlanningUpdate(trackingResult);
                    }
                }

                double msAfterPathAttack = sw.Elapsed.TotalMilliseconds;

                ProcessBloodBarDetection(frameMat, config, now);

                double msAfterBlood = sw.Elapsed.TotalMilliseconds;

                ProcessMonsterDetection(frameMat, config, now);

                double msAfterMonster = sw.Elapsed.TotalMilliseconds;

                var result = GetCurrentSnapshot();
                OnFrameProcessed?.Invoke(result);

                double totalMs = sw.Elapsed.TotalMilliseconds;
                double tailMs = totalMs - msAfterMonster;
                if (totalMs >= PipelineSlowFrameWarnMs)
                {
                    Logger.Warning(
                        $"[GamePipeline] 慢幀 total={totalMs:F1}ms captureLag={captureLagMs:F1}ms " +
                        $"minimap={msAfterMinimap:F1} pathAttack={msAfterPathAttack - msAfterMinimap:F1} " +
                        $"blood={msAfterBlood - msAfterPathAttack:F1} monster={msAfterMonster - msAfterBlood:F1} tail={tailMs:F1}");
                }
#if DEBUG
                else if (totalMs >= PipelineSlowFrameDebugMs)
                {
                    Logger.Debug(
                        $"[GamePipeline] 幀耗時 total={totalMs:F1}ms captureLag={captureLagMs:F1}ms " +
                        $"minimap={msAfterMinimap:F1} pathAttack={msAfterPathAttack - msAfterMinimap:F1} " +
                        $"blood={msAfterBlood - msAfterPathAttack:F1} monster={msAfterMonster - msAfterBlood:F1} tail={tailMs:F1}");
                }
#endif
            }
            catch (Exception ex)
            {
                Logger.Error($"[GamePipeline] 處理畫面錯誤: {ex.Message}");
            }
            finally
            {
                if (_frameSignal.CurrentCount == 0)
                {
                    _frameSignal.Release();
                }
            }
        }

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

        /// <summary>轉向最近目標並送出攻擊鍵。</summary>
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

            if ((now - _lastDirectionChangeTime).TotalMilliseconds > DirectionChangeCooldownMs)
            {
                int monsterCenterX = target.BoundingBox.X + target.BoundingBox.Width / 2;
                ushort directionKey = monsterCenterX < playerCenter.X ? VK_LEFT : VK_RIGHT;

                _movementController.SendKeyInput(directionKey, false);
                await Task.Delay(20);
                _movementController.SendKeyInput(directionKey, true);
                _lastDirectionChangeTime = now;
            }

            _movementController.SendKeyInput(VK_CONTROL, false);
            await Task.Delay(20);
            _movementController.SendKeyInput(VK_CONTROL, true);

            _lastAttackTime = now;
            OnStatusMessage?.Invoke($"自動攻擊: 鎖定 {target.Name}");
        }

        /// <summary>停止移動、執行攻擊、冷卻後恢復。</summary>
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

                CheckAutoAttackCondition();
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"怪物檢測錯誤: {ex.Message}");
            }
        }

        /// <summary>怪物與攻擊範圍相交時觸發攻擊序列。</summary>
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
