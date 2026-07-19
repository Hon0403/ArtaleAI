using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArtaleAI.Models.Config;
using ArtaleAI.Vision;
using ArtaleAI.Application.Navigation;
using ArtaleAI.Application.Movement;
using ArtaleAI.Domain.Navigation;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Shared;
using ArtaleAI.Vision.Detectors;
using OpenCvSharp;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>每幀協調追蹤、路徑、攻擊與偵測；不依賴 WinForms。</summary>
    public class GamePipeline
    {
        private readonly GameVisionCore _gameVision;
        private readonly PathPlanningManager? _pathPlanningManager;
        private readonly CharacterMovementController? _movementController;
        private readonly IPlayerVitalsDetector _playerVitalsDetector;

        private readonly object _bloodBarLock = new();
        private readonly object _vitalsLock = new();
        private readonly object _monsterLock = new();
        private readonly object _minimapBoxLock = new();
        private readonly object _minimapMarkerLock = new();

        private List<SdRect> _currentBloodBars = new();
        private List<SdRect> _currentDetectionBoxes = new();
        private List<SdRect> _currentAttackRangeBoxes = new();
        private List<DetectionResult> _currentMonsters = new();

        /// <summary>怪物結果的「來源幀擷取時間」（非偵測完成時間），供過期修剪判斷。</summary>
        private DateTime _monsterResultCaptureUtc = DateTime.MinValue;
        private List<SdRect> _currentMinimapBoxes = new();
        private List<SdRect> _currentMinimapMarkers = new();
        private List<SdRect> _currentOtherPlayerMarkers = new();
        private PlayerVitalsSnapshot? _currentPlayerVitals;

        private readonly AutoHealCoordinator _autoHeal = new();
        private readonly BuffSkillCoordinator _buffSkills = new();
        private readonly AttackRotationCoordinator _attackRotation = new();
        private readonly OtherPlayerAvoidanceCoordinator _otherPlayerAvoidance = new();
        private readonly ChangeChannelSequence _changeChannelSequence = new();
        private readonly FarmUiInterruptCoordinator _farmUiInterrupt = new();
        private readonly PartyHpBarRecoveryCoordinator _partyRecovery = new();
        private readonly object _uiAutomationFrameLock = new();
        private Mat? _uiAutomationFrameCache;
        private int _changeChannelInFlight;
        private DateTime _lastBloodBarDetection = DateTime.MinValue;
        private DateTime _lastPlayerVitalsDetection = DateTime.MinValue;
        private DateTime _lastMonsterDetection = DateTime.MinValue;

        private volatile bool _isAttacking = false;
        private bool _lastAutoAttackEnabled;

        /// <summary>
        /// 休息階段：None=正常巡邏；Seeking=前往休息點（導航開放、攻擊關閉）；
        /// Resting=倒數中（導航與攻擊皆關閉）。volatile int 因 C# 不允許 volatile enum。
        /// </summary>
        private const int RestPhaseNone = 0;
        private const int RestPhaseSeeking = 1;
        private const int RestPhaseResting = 2;
        private volatile int _restPhase = RestPhaseNone;

        /// <summary>選點或強制目標規劃失敗時的重試節流，避免每幀重跑 A*。</summary>
        private const int RestSeekRetrySeconds = 3;
        private DateTime _restEndsAtUtc = DateTime.MinValue;
        private DateTime _nextRestDueUtc = DateTime.MinValue;
        private DateTime _nextRestSeekRetryUtc = DateTime.MinValue;
        private int _attackInputLease;
        private int _monsterDetectionInFlight;
        private int _monsterJobReplacementCount;
        private readonly object _monsterJobLock = new();
        private MonsterDetectionWorkItem? _pendingMonsterJob;
        private const int MonsterJobReplacementLogInterval = 30;
        private DateTime _lastAttackTime = DateTime.MinValue;
        private DateTime _lastDirectionChangeTime = DateTime.MinValue;
        private const int AttackCooldownMs = 500;
        private const int DirectionChangeCooldownMs = 200;
        private const ushort VK_LEFT = 0x25;
        private const ushort VK_RIGHT = 0x27;
        private const ushort VK_CONTROL = 0x11;

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

        /// <summary>
        /// 自動喝水工作階段：勾選「自動打怪」並開著擷取即可。
        /// 不要求路徑／怪物就緒（那些只閘攻擊與導航）。
        /// </summary>
        public volatile bool AutoHealEnabled;

        /// <summary>補助技能循環：與喝水相同，勾選自動打怪即可週期施放。</summary>
        public volatile bool AutoBuffEnabled;

        /// <summary>遇人換頻／退避：勾選自動打怪且設定開啟時生效。</summary>
        public volatile bool OtherPlayerAvoidanceEnabled;

        /// <summary>
        /// 隊伍血條守門／重建：主控台勾選「開始」即持續監測。
        /// 與 AutoAttackEnabled 解耦——血條是攻擊前置，不應被路徑／怪物選擇綁住。
        /// </summary>
        public volatile bool PartyRecoveryEnabled;

        /// <summary>執行期啟用的怪物模板 catalog（與 <see cref="MonsterTemplateStore.Catalog"/> 共用參考）。</summary>
        public MonsterDetectionCatalog MonsterCatalog { get; set; } = new();

        /// <summary>幀驅動同步：視覺處理完成旗標</summary>
        public bool VisionDataReady
        {
            get => _visionDataReady;
            set => _visionDataReady = value;
        }

        /// <summary>防偵測休息流程中（前往休息點或倒數中，攻擊皆暫停）。</summary>
        public bool IsResting => _restPhase != RestPhaseNone;

        /// <summary>正在前往安全折點／繩索休息點（導航仍開放）。</summary>
        public bool IsSeekingRestSpot => _restPhase == RestPhaseSeeking;

        /// <summary>小地圖遇人退避中。</summary>
        public bool IsAvoidingOtherPlayers => _otherPlayerAvoidance.IsAvoiding;

        /// <summary>隊伍血條重建序列進行中。</summary>
        public bool IsRecoveringParty => _partyRecovery.IsRecovering;

        /// <summary>
        /// 攻擊／小休倒數／遇人退避／換頻／打怪清窗期間，導航 Walk 應讓出鍵盤。
        /// SeekingRest 不在此列：前往休息點必須靠導航輸入。
        /// </summary>
        public bool BlocksNavigationInput =>
            _restPhase == RestPhaseResting
            || _isAttacking
            || _otherPlayerAvoidance.IsAvoiding
            || _farmUiInterrupt.IsDismissing
            || _partyRecovery.IsRecovering
            || Volatile.Read(ref _attackInputLease) > 0
            || Volatile.Read(ref _changeChannelInFlight) != 0;

        /// <summary>每幀處理完成後觸發，攜帶所有偵測結果的快照</summary>
        public event Action<FrameProcessingResult>? OnFrameProcessed;

        /// <summary>狀態訊息事件（取代直接呼叫 MsgLog.ShowStatus）</summary>
        public event Action<string>? OnStatusMessage;

        /// <summary>路徑規劃追蹤結果事件（傳遞給 OnPathTrackingUpdated）</summary>
        public event Action<MinimapTrackingResult>? OnPathTrackingResult;

        public GamePipeline(
            GameVisionCore gameVision,
            PathPlanningManager? pathPlanningManager = null,
            CharacterMovementController? movementController = null,
            IPlayerVitalsDetector? playerVitalsDetector = null)
        {
            _gameVision = gameVision ?? throw new ArgumentNullException(nameof(gameVision));
            _pathPlanningManager = pathPlanningManager;
            _movementController = movementController;
            _playerVitalsDetector = playerVitalsDetector ?? new PlayerVitalsDetector();
        }

        /// <summary>更新小地圖邊界範圍（由 MainForm 傳入）</summary>
        public void SetMinimapBoxes(List<SdRect> boxes)
        {
            lock (_minimapBoxLock) { _currentMinimapBoxes = boxes.ToList(); }
        }

        /// <summary>取得當前偵測結果的線程安全快照</summary>
        public FrameProcessingResult GetCurrentSnapshot()
        {
            List<SdRect> bloodBars, detectionBoxes, attackRangeBoxes, minimapBoxes, minimapMarkers, otherPlayerMarkers;
            List<DetectionResult> monsters;

            lock (_bloodBarLock) bloodBars = _currentBloodBars.ToList();
            lock (_monsterLock) monsters = _currentMonsters.ToList();
            lock (_minimapBoxLock) minimapBoxes = _currentMinimapBoxes.ToList();
            lock (_minimapMarkerLock)
            {
                minimapMarkers = _currentMinimapMarkers.ToList();
                otherPlayerMarkers = _currentOtherPlayerMarkers.ToList();
            }
            detectionBoxes = _currentDetectionBoxes.ToList();
            attackRangeBoxes = _currentAttackRangeBoxes.ToList();
            PlayerVitalsSnapshot? playerVitals;
            lock (_vitalsLock) playerVitals = _currentPlayerVitals;

            return new FrameProcessingResult
            {
                BloodBars = bloodBars,
                DetectionBoxes = detectionBoxes,
                AttackRangeBoxes = attackRangeBoxes,
                Monsters = monsters,
                MinimapBoxes = minimapBoxes,
                MinimapMarkers = minimapMarkers,
                OtherPlayerMarkers = otherPlayerMarkers,
                PlayerVitals = playerVitals
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

                // 時間線對齊：怪物結果來自非同步背景偵測，先修剪過期資料，
                // 確保本幀所有下游（攻擊決策、快照）只消費新鮮結果。
                PruneStaleMonsterResults(now, config);

                // 監測開啟即持續快取幀：換頻／隊伍重建／Esc 探針驗證都靠最新畫面。
                if (AutoAttackEnabled
                    || AutoHealEnabled
                    || AutoBuffEnabled
                    || PartyRecoveryEnabled
                    || OtherPlayerAvoidanceEnabled
                    || Volatile.Read(ref _changeChannelInFlight) != 0
                    || _partyRecovery.IsRecovering
                    || _farmUiInterrupt.IsDismissing)
                    CacheFrameForUiAutomation(frameMat);

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

                if (trackingResult != null)
                    FeedPlayerTracking(trackingResult);

                if (OtherPlayerAvoidanceEnabled)
                    ProcessOtherPlayerAvoidance(trackingResult, config, now);

                TryProcessFarmUiInterrupt(frameMat, trackingResult, config);

                if (AutoAttackEnabled)
                    ProcessAntiDetectRest(config, now);

                // 攻擊只允許從本入口發起；怪物背景 worker 僅更新結果，不得自行送鍵。
                if (CanStartAttack())
                    ProcessAutoAttackDecision();

                if (trackingResult != null && !BlocksNavigationInput)
                    SignalNavigationOrchestration(trackingResult);

                double msAfterPathAttack = sw.Elapsed.TotalMilliseconds;

                ProcessBloodBarDetection(frameMat, config, now, trackingResult);
                ProcessPlayerVitalsDetection(frameMat, config, now);

                // 隊伍血條是攻擊框前置：主控台開始即持續監測，不等路徑／怪物就緒。
                if (PartyRecoveryEnabled)
                    TryRecoverPartyHpBar(trackingResult, config, now);

                if (AutoHealEnabled
                    && !_otherPlayerAvoidance.IsAvoiding
                    && !_partyRecovery.IsRecovering
                    && !_farmUiInterrupt.IsDismissing
                    && Volatile.Read(ref _changeChannelInFlight) == 0)
                    ProcessAutoHeal(config, now);

                if (AutoBuffEnabled
                    && !_otherPlayerAvoidance.IsAvoiding
                    && !_partyRecovery.IsRecovering
                    && !_farmUiInterrupt.IsDismissing
                    && Volatile.Read(ref _changeChannelInFlight) == 0)
                    ProcessBuffSkills(config, now);

                double msAfterBlood = sw.Elapsed.TotalMilliseconds;

                ScheduleMonsterDetection(frameMat, captureTime, config, now);

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
                        _currentOtherPlayerMarkers.Clear();
                        if (trackingResult?.PlayerPosition.HasValue == true)
                        {
                            var playerPos = trackingResult.PlayerPosition.Value;
                            var screenPlayerPos = new SdPoint(
                                minimapRect.Value.X + (int)playerPos.X,
                                minimapRect.Value.Y + (int)playerPos.Y);
                            _currentMinimapMarkers.Add(new SdRect(
                                screenPlayerPos.X - 5, screenPlayerPos.Y - 5, 10, 10));
                        }

                        if (trackingResult?.OtherPlayers != null)
                        {
                            foreach (var other in trackingResult.OtherPlayers)
                            {
                                var screenPos = new SdPoint(
                                    minimapRect.Value.X + (int)other.X,
                                    minimapRect.Value.Y + (int)other.Y);
                                _currentOtherPlayerMarkers.Add(new SdRect(
                                    screenPos.X - 6, screenPos.Y - 6, 12, 12));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[GamePipeline] 小地圖標記處理錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 攻擊決策的唯一閘門：換頻／清窗／退避／隊伍重建／進行中攻擊皆不可再開新攻擊。
        /// </summary>
        private bool CanStartAttack()
        {
            return AutoAttackEnabled
                && _restPhase == RestPhaseNone
                && !_otherPlayerAvoidance.IsAvoiding
                && !_partyRecovery.IsRecovering
                && !_farmUiInterrupt.IsDismissing
                && Volatile.Read(ref _changeChannelInFlight) == 0
                && Volatile.Read(ref _attackInputLease) == 0;
        }

        /// <summary>已持有攻擊租約時，換頻／退避發生則應立刻中止後續按鍵。</summary>
        private bool CanContinueAttack()
        {
            return AutoAttackEnabled
                && !_otherPlayerAvoidance.IsAvoiding
                && !_partyRecovery.IsRecovering
                && !_farmUiInterrupt.IsDismissing
                && Volatile.Read(ref _changeChannelInFlight) == 0;
        }

        private bool ProcessAutoAttackDecision()
        {
            if (!TryCollectAttackTargets(out SdRect playerAttackBox, out List<DetectionResult> targets))
                return false;

            // CAS 租約：同一時刻只允許一條攻擊序列持有鍵盤。
            if (Interlocked.CompareExchange(ref _attackInputLease, 1, 0) != 0)
                return false;

            _isAttacking = true;
            _ = PerformAutoAttackAsync(playerAttackBox, targets);
            return true;
        }

        /// <summary>釋放方向鍵與主攻鍵，避免換頻／熄火時殘留 key-down。</summary>
        private void AbortCombatInputs()
        {
            _movementController?.StopMovement();
            if (_movementController == null)
                return;

            _movementController.SendKeyInput(VK_LEFT, keyUp: true);
            _movementController.SendKeyInput(VK_RIGHT, keyUp: true);
            _movementController.SendKeyInput(VK_CONTROL, keyUp: true);

            string? primary = AppConfig.Instance?.AutoFarm?.AttackPrimaryHotkey;
            if (!string.IsNullOrWhiteSpace(primary)
                && VirtualKeyParser.TryParse(primary, out ushort primaryVk)
                && primaryVk != VK_CONTROL)
            {
                _movementController.SendKeyInput(primaryVk, keyUp: true);
            }
        }

        /// <summary>
        /// 導航優先於攻擊：飛行中／路徑未完成時不打斷走位。
        /// 並過濾垂直差過大（不同層高）的怪物，避免螢幕框相交但打不到。
        /// </summary>
        private bool TryCollectAttackTargets(out SdRect attackBox, out List<DetectionResult> targets)
        {
            attackBox = default;
            targets = new List<DetectionResult>();

            if (ShouldDeferAttackForNavigation())
                return false;

            List<SdRect> attackRanges = _currentAttackRangeBoxes.ToList();
            List<DetectionResult> monsters;
            lock (_monsterLock) monsters = _currentMonsters.ToList();

            if (!attackRanges.Any() || !monsters.Any())
                return false;

            attackBox = attackRanges.FirstOrDefault();
            if (attackBox.IsEmpty)
                return false;

            int maxVerticalDelta = ResolveAttackMaxVerticalDeltaPx();
            float boxCenterY = attackBox.Y + attackBox.Height / 2f;

            foreach (var m in monsters)
            {
                if (!attackBox.IntersectsWith(m.BoundingBox))
                    continue;

                float monsterCenterY = m.BoundingBox.Y + m.BoundingBox.Height / 2f;
                if (Math.Abs(monsterCenterY - boxCenterY) > maxVerticalDelta)
                    continue;

                targets.Add(m);
            }

            return targets.Count > 0;
        }

        /// <summary>
        /// 僅在垂直移動／跳躍飛行中暫緩攻擊；Walk 巡邏允許同層攻擊。
        /// （先前「路徑未走完就不打」會導致巡邏永遠不攻擊。）
        /// </summary>
        private bool ShouldDeferAttackForNavigation()
        {
            var farm = AppConfig.Instance?.AutoFarm;
            if (farm != null && !farm.PreferNavigationOverAttack)
                return false;

            if (_pathPlanningManager == null || !_pathPlanningManager.IsRunning)
                return false;

            if (!_pathPlanningManager.HasActiveNavigationFlight)
                return false;

            return _pathPlanningManager.ActiveFlightActionType is
                NavigationActionType.ClimbUp or
                NavigationActionType.ClimbDown or
                NavigationActionType.Jump or
                NavigationActionType.SideJump or
                NavigationActionType.JumpDown;
        }

        private static int ResolveAttackMaxVerticalDeltaPx()
        {
            int configured = AppConfig.Instance?.AutoFarm?.AttackMaxVerticalDeltaPx ?? 80;
            return configured > 0 ? configured : 80;
        }

        private void ProcessAntiDetectRest(AppConfig config, DateTime now)
        {
            if (!AutoAttackEnabled)
            {
                CancelRestFlow();
                _nextRestDueUtc = DateTime.MinValue;
                _lastAutoAttackEnabled = false;
                return;
            }

            if (!_lastAutoAttackEnabled)
            {
                _lastAutoAttackEnabled = true;
                ScheduleNextRest(config, now);
            }

            int intervalMinutes = config.AutoFarm.RestIntervalMinutes;
            if (intervalMinutes <= 0)
            {
                CancelRestFlow();
                return;
            }

            if (_restPhase == RestPhaseResting)
            {
                if (now >= _restEndsAtUtc)
                {
                    _restPhase = RestPhaseNone;
                    RestNavigationTracker?.ClearForcedGoal();
                    _movementController?.StopMovement();
                    OnStatusMessage?.Invoke("小休結束，繼續自動打怪");
                    ScheduleNextRest(config, now);
                }

                return;
            }

            if (_restPhase == RestPhaseSeeking)
            {
                ProcessRestSeeking(config, now);
                return;
            }

            if (_nextRestDueUtc == DateTime.MinValue)
                ScheduleNextRest(config, now);

            if (now < _nextRestDueUtc)
                return;

            TryBeginRestSeek(config, now);
        }

        /// <summary>休息導航僅在路徑規劃運行中才有意義；否則退化為原地休息。</summary>
        private PathPlanningTracker? RestNavigationTracker =>
            _pathPlanningManager?.IsRunning == true ? _pathPlanningManager.Tracker : null;

        private void CancelRestFlow()
        {
            if (_restPhase == RestPhaseNone) return;
            _restPhase = RestPhaseNone;
            RestNavigationTracker?.ClearForcedGoal();
        }

        /// <summary>
        /// UI 關閉自動打怪的單一熄火入口：一次熄掉所有工作階段旗標、清休息狀態與 ForcedGoal，
        /// 並鬆開方向鍵。避免鬆勾後角色仍靠殘留狀態續走／續打。
        /// 導航飛行 (Walk/Jump/Climb) 的中斷由呼叫端的 FSM.CancelNavigation 負責。
        /// </summary>
        public void StopAutoFarmImmediately()
        {
            AutoAttackEnabled = false;
            AutoHealEnabled = false;
            AutoBuffEnabled = false;
            OtherPlayerAvoidanceEnabled = false;
            PartyRecoveryEnabled = false;

            _lastAutoAttackEnabled = false;
            _nextRestDueUtc = DateTime.MinValue;
            _nextRestSeekRetryUtc = DateTime.MinValue;
            CancelRestFlow();

            AbortCombatInputs();
        }

        /// <summary>
        /// 休息到期：選最近可達的安全折點或繩索並注入強制目標。
        /// 選不到可達點時不進入 Resting，節流後重試（持續尋路直到到達）。
        /// </summary>
        private void TryBeginRestSeek(AppConfig config, DateTime now)
        {
            if (now < _nextRestSeekRetryUtc)
                return;

            var tracker = RestNavigationTracker;
            var graph = tracker?.NavGraph;
            var playerPos = tracker?.CurrentPathState?.CurrentPlayerPosition;

            if (tracker == null || graph == null || playerPos == null)
            {
                BeginRestCountdown(config, now, "無導航資料，原地小休");
                return;
            }

            var selection = RestSpotSelector.Select(graph, playerPos.Value);
            switch (selection.Outcome)
            {
                case RestSpotOutcome.NoCandidates:
                    BeginRestCountdown(config, now, "地圖無安全區與繩索，原地小休");
                    return;

                case RestSpotOutcome.Unreachable:
                    _nextRestSeekRetryUtc = now.AddSeconds(RestSeekRetrySeconds);
                    OnStatusMessage?.Invoke("休息點暫時不可達，稍後重試…");
                    return;
            }

            tracker.SetForcedGoal(selection.Node!.Id);
            _restPhase = RestPhaseSeeking;
            string spotKind = selection.Node.Type == NavigationNodeType.Rope ? "繩索" : "安全區";
            OnStatusMessage?.Invoke($"休息時間到，前往{spotKind}休息點…");
        }

        /// <summary>SeekingRest：等待強制目標到達；規劃停擺時節流重試，直到到達才開始倒數。</summary>
        private void ProcessRestSeeking(AppConfig config, DateTime now)
        {
            var tracker = RestNavigationTracker;
            if (tracker == null || !tracker.HasForcedGoal)
            {
                BeginRestCountdown(config, now, "導航中斷，原地小休");
                return;
            }

            if (tracker.IsForcedGoalArrived)
            {
                // ForcedGoal 保留至休息結束，避免倒數期間巡邏重啟。
                BeginRestCountdown(config, now, reason: null);
                return;
            }

            if (tracker.IsForcedGoalPlanningParked && now >= _nextRestSeekRetryUtc)
            {
                _nextRestSeekRetryUtc = now.AddSeconds(RestSeekRetrySeconds);
                tracker.RetryForcedGoalPlanning();
            }
        }

        private void BeginRestCountdown(AppConfig config, DateTime now, string? reason)
        {
            int durationSeconds = ResolveRestDurationSeconds(config.AutoFarm);
            _restPhase = RestPhaseResting;
            _restEndsAtUtc = now.AddSeconds(durationSeconds);
            _movementController?.StopMovement();
            OnStatusMessage?.Invoke(reason == null
                ? $"已到休息點，小休約 {durationSeconds} 秒…"
                : $"{reason}，約 {durationSeconds} 秒…");
        }

        private void ProcessOtherPlayerAvoidance(
            MinimapTrackingResult? trackingResult,
            AppConfig config,
            DateTime now)
        {
            // 隊伍重建整段佔用鍵盤／畫面；換頻 Esc 會把建隊流程打掉。
            if (_partyRecovery.IsRecovering)
                return;

            int otherCount = trackingResult?.OtherPlayers?.Count ?? 0;
            bool justTriggered = _otherPlayerAvoidance.TryUpdate(
                config.AutoFarm,
                otherCount,
                now);

            if (!justTriggered)
                return;

            // 先搶佔戰鬥輸入，避免背景攻擊 Task 與換頻 Esc／點擊互搶鍵盤。
            AbortCombatInputs();
            OnStatusMessage?.Invoke("偵測到其他玩家：暫停並執行 Esc→點頻道");
            TryStartChangeChannel(config.AutoFarm);
        }

        /// <summary>自動打怪開啟即清突發窗；換頻中／隊伍建隊危險階段讓出。</summary>
        private void TryProcessFarmUiInterrupt(
            Mat frameMat,
            MinimapTrackingResult? trackingResult,
            AppConfig config)
        {
            // AutoAttack／Heal／Buff 皆由「自動打怪」勾選驅動。
            bool autoFarmActive = AutoAttackEnabled || AutoHealEnabled || AutoBuffEnabled;
            bool uiSequenceBusy =
                Volatile.Read(ref _changeChannelInFlight) != 0
                || _partyRecovery.BlocksInterruptDismiss;
            bool hasMinimap = trackingResult?.MinimapBounds != null;

            if (!hasMinimap
                && autoFarmActive
                && !uiSequenceBusy
                && config.AutoFarm.InterruptDismissEnabled
                && config.AutoFarm.InterruptDismissDuringAutoFarm)
            {
                hasMinimap = _gameVision.FindMinimapOnScreen(frameMat).HasValue;
            }

            _farmUiInterrupt.ObserveFrame(
                frameMat,
                hasMinimap,
                autoFarmActive,
                uiSequenceBusy,
                config.AutoFarm,
                config.Vision,
                _movementController,
                CloneCachedUiAutomationFrame,
                msg => OnStatusMessage?.Invoke(msg));
        }

        private void TryStartChangeChannel(AutoFarmSettings settings)
        {
            if (_movementController == null)
            {
                _otherPlayerAvoidance.SetPulse("換頻失敗：無輸入控制器");
                return;
            }

            if (Interlocked.CompareExchange(ref _changeChannelInFlight, 1, 0) != 0)
                return;

            CharacterMovementController movement = _movementController;
            _ = Task.Run(async () =>
            {
                try
                {
                    bool ok = await _changeChannelSequence.ExecuteAsync(
                        settings,
                        CloneCachedUiAutomationFrame,
                        movement.FocusGameWindow,
                        async (vk, token) =>
                        {
                            movement.FocusGameWindow();
                            await movement.TapKeyAsync(vk, pressDurationMs: 100, intervalMs: 40, token)
                                .ConfigureAwait(false);
                        },
                        async (x, y, frameW, frameH, token) =>
                            await movement.ClickCapturePointAsync(x, y, frameW, frameH, token)
                                .ConfigureAwait(false),
                        msg =>
                        {
                            _otherPlayerAvoidance.SetPulse(msg);
                            OnStatusMessage?.Invoke(msg);
                        },
                        frame => _gameVision.FindMinimapOnScreen(frame).HasValue).ConfigureAwait(false);

                    _otherPlayerAvoidance.SetPulse(ok ? "換頻指令已送出" : "換頻失敗");
                }
                catch (OperationCanceledException)
                {
                    _otherPlayerAvoidance.SetPulse("換頻已取消");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[換頻] 序列例外: {ex.Message}");
                    _otherPlayerAvoidance.SetPulse("換頻例外");
                    OnStatusMessage?.Invoke($"換頻失敗：{ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _changeChannelInFlight, 0);
                }
            });
        }

        private void CacheFrameForUiAutomation(Mat frameMat)
        {
            Mat clone = frameMat.Clone();
            lock (_uiAutomationFrameLock)
            {
                _uiAutomationFrameCache?.Dispose();
                _uiAutomationFrameCache = clone;
            }
        }

        /// <summary>回傳快取幀的 Clone；呼叫端（換頻／隊伍重建序列）負責 Dispose。</summary>
        private Mat? CloneCachedUiAutomationFrame()
        {
            lock (_uiAutomationFrameLock)
            {
                if (_uiAutomationFrameCache == null || _uiAutomationFrameCache.Empty())
                    return null;
                return _uiAutomationFrameCache.Clone();
            }
        }

        /// <summary>
        /// 隊伍血條守門：自動打怪期間每幀觀測；缺失時啟動狀態機（開窗→新建→關窗→等血條）。
        /// 僅在遊戲畫面中（有小地圖）且無換頻／清窗／退避／小休時執行。
        /// </summary>
        private void TryRecoverPartyHpBar(MinimapTrackingResult? trackingResult, AppConfig config, DateTime now)
        {
            if (_movementController == null)
                return;

            if (Volatile.Read(ref _changeChannelInFlight) != 0
                || _farmUiInterrupt.IsDismissing
                || _otherPlayerAvoidance.IsAvoiding
                || IsResting)
                return;

            if (trackingResult?.MinimapBounds == null)
                return;

            _partyRecovery.Observe(
                config.AutoFarm,
                _lastBloodBarDetection,
                now,
                CloneCachedUiAutomationFrame,
                () => _lastBloodBarDetection,
                frame => _gameVision.FindMinimapOnScreen(frame).HasValue,
                _movementController,
                msg => OnStatusMessage?.Invoke(msg));
        }

        /// <summary>自動打怪勾選、擷取運行中即可依血魔％按藥水快捷鍵；不依賴路徑／怪物就緒。</summary>
        private void ProcessAutoHeal(AppConfig config, DateTime now)
        {
            if (_movementController == null)
                return;

            PlayerVitalsSnapshot? vitals;
            lock (_vitalsLock)
                vitals = _currentPlayerVitals;

            _autoHeal.TryHeal(
                config.AutoFarm,
                vitals,
                now,
                TapSkillHotkey);
        }

        private void ProcessBuffSkills(AppConfig config, DateTime now)
        {
            if (_movementController == null)
                return;

            config.AutoFarm.EnsureBuffSkillSlots();
            _buffSkills.TryCast(config.AutoFarm, now, TapSkillHotkey);
        }

        /// <summary>供主控台 StatusBar 顯示剛補／冷卻短提示。</summary>
        public string? GetAutoHealStatusHint(DateTime? nowUtc = null)
        {
            PlayerVitalsSnapshot? vitals;
            lock (_vitalsLock)
                vitals = _currentPlayerVitals;

            return _autoHeal.GetStatusHint(
                AppConfig.Instance.AutoFarm,
                vitals,
                nowUtc ?? DateTime.UtcNow);
        }

        public string? GetBuffStatusHint(DateTime? nowUtc = null)
            => _buffSkills.GetStatusHint(nowUtc ?? DateTime.UtcNow);

        public string? GetAttackStatusHint(DateTime? nowUtc = null)
            => _attackRotation.GetStatusHint(nowUtc ?? DateTime.UtcNow);

        public string? GetOtherPlayerAvoidanceStatusHint(DateTime? nowUtc = null)
            => _otherPlayerAvoidance.GetStatusHint(nowUtc ?? DateTime.UtcNow);

        public string? GetPartyRecoveryStatusHint(DateTime? nowUtc = null)
            => _partyRecovery.GetStatusHint(nowUtc ?? DateTime.UtcNow);

        public void ResetBuffSchedule() => _buffSkills.ResetSchedule();

        public void ResetAttackCooldowns() => _attackRotation.ResetCooldowns();

        private void TapSkillHotkey(ushort virtualKey)
        {
            var movement = _movementController;
            if (movement == null)
                return;

            // fire-and-forget：短按不阻塞幀迴圈；間隔由各 Coordinator 節流。
            _ = Task.Run(async () =>
            {
                try
                {
                    movement.SendKeyInput(virtualKey, false);
                    await Task.Delay(60).ConfigureAwait(false);
                    movement.SendKeyInput(virtualKey, true);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[自動按鍵] 失敗: {ex.Message}");
                }
            });
        }

        private void ScheduleNextRest(AppConfig config, DateTime now)
        {
            int intervalMinutes = config.AutoFarm.RestIntervalMinutes;
            if (intervalMinutes <= 0)
            {
                _nextRestDueUtc = DateTime.MinValue;
                return;
            }

            double jitteredMinutes = ApplyPercentJitter(
                intervalMinutes,
                config.AutoFarm.RestJitterPercent);

            // 抖動後仍至少 1 分鐘，避免緊貼連續休息。
            jitteredMinutes = Math.Max(1.0, jitteredMinutes);
            _nextRestDueUtc = now.AddMinutes(jitteredMinutes);
            OnStatusMessage?.Invoke($"下次約 {jitteredMinutes:F0} 分鐘後會再休息");
        }

        private static int ResolveRestDurationSeconds(AutoFarmSettings settings)
        {
            double jittered = ApplyPercentJitter(
                Math.Max(5, settings.RestDurationSeconds),
                settings.RestJitterPercent);
            return Math.Max(5, (int)Math.Round(jittered));
        }

        /// <summary>對基準值套用 ±jitterPercent 均勻隨機倍率，打破固定週期指紋。</summary>
        private static double ApplyPercentJitter(double baseValue, int jitterPercent)
        {
            int clamped = Math.Clamp(jitterPercent, 0, 50);
            if (clamped <= 0 || baseValue <= 0)
                return baseValue;

            double factor = 1.0 + (Random.Shared.NextDouble() * 2.0 - 1.0) * (clamped / 100.0);
            return baseValue * factor;
        }

        /// <summary>轉向最近目標並送出攻擊鍵；租約已由呼叫端 CAS 取得，結束時釋放。</summary>
        private async Task PerformAutoAttackAsync(SdRect playerBox, List<DetectionResult> targets)
        {
            try
            {
                if (targets == null || targets.Count == 0) return;
                if (_movementController == null) return;
                if (!CanContinueAttack()) return;

                _movementController.StopMovement();

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
                    if (!CanContinueAttack()) return;

                    int monsterCenterX = target.BoundingBox.X + target.BoundingBox.Width / 2;
                    ushort directionKey = monsterCenterX < playerCenter.X ? VK_LEFT : VK_RIGHT;

                    _movementController.SendKeyInput(directionKey, false);
                    await Task.Delay(20).ConfigureAwait(false);
                    _movementController.SendKeyInput(directionKey, true);
                    _lastDirectionChangeTime = now;
                }

                if (!CanContinueAttack()) return;

                if (!_attackRotation.TrySelectAttackKey(
                        AppConfig.Instance.AutoFarm,
                        now,
                        out ushort attackKey,
                        out string attackLabel))
                {
                    Logger.Warning("[自動攻擊] 主攻快捷鍵無法解析，略過此次攻擊");
                    return;
                }

                _movementController.SendKeyInput(attackKey, false);
                await Task.Delay(20).ConfigureAwait(false);

                // 換頻可能在 Delay 期間觸發：只送 key-up，不送第二次按下。
                if (!CanContinueAttack())
                {
                    _movementController.SendKeyInput(attackKey, true);
                    return;
                }

                _movementController.SendKeyInput(attackKey, true);

                _lastAttackTime = now;
                OnStatusMessage?.Invoke($"自動攻擊: 鎖定 {target.Name}（{attackLabel}）");
            }
            finally
            {
                _isAttacking = false;
                Interlocked.Exchange(ref _attackInputLease, 0);
            }
        }

        /// <summary>每幀更新玩家座標 SSOT（攻擊期間仍執行）。</summary>
        private void FeedPlayerTracking(MinimapTrackingResult result)
        {
            if (_pathPlanningManager == null || !_pathPlanningManager.IsRunning) return;

            try
            {
                _pathPlanningManager.ProcessTrackingResult(result);
            }
            catch (Exception ex)
            {
                Logger.Error($"[GamePipeline] 追蹤座標更新錯誤: {ex.Message}");
            }
        }

        /// <summary>攻擊未佔用輸入時，才推進導航 FSM tick。</summary>
        private void SignalNavigationOrchestration(MinimapTrackingResult result)
        {
            if (_pathPlanningManager == null || !_pathPlanningManager.IsRunning) return;

            try
            {
                _visionDataReady = true;
                OnPathTrackingResult?.Invoke(result);
            }
            catch (Exception ex)
            {
                Logger.Error($"[GamePipeline] 導航編排信號錯誤: {ex.Message}");
            }
        }

        private void ProcessBloodBarDetection(
            Mat frameMat,
            AppConfig config,
            DateTime now,
            MinimapTrackingResult? trackingResult)
        {
            var elapsed = (now - _lastBloodBarDetection).TotalMilliseconds;
            int bloodBarCount;
            lock (_bloodBarLock) bloodBarCount = _currentBloodBars.Count;

            if (elapsed < config.Vision.BloodBarDetectIntervalMs && bloodBarCount > 0)
                return;

            try
            {
                bool hasMinimapSelf = trackingResult?.PlayerPosition.HasValue == true;
                SdRect? minimapBox;
                lock (_minimapBoxLock)
                    minimapBox = _currentMinimapBoxes.Count > 0 ? _currentMinimapBoxes[0] : null;

                var bloodBarResult = _gameVision.ProcessBloodBarDetection(
                    frameMat, null, hasMinimapSelf, minimapBox);
                SdRect? bloodBar = bloodBarResult.Item1;
                var detectionBoxes = bloodBarResult.Item2 ?? new List<SdRect>();
                var attackRangeBoxes = bloodBarResult.Item3 ?? new List<SdRect>();

                lock (_bloodBarLock)
                {
                    _currentBloodBars.Clear();
                    if (bloodBar.HasValue)
                        _currentBloodBars.Add(bloodBar.Value);
                }

                _currentDetectionBoxes = detectionBoxes;
                _currentAttackRangeBoxes = attackRangeBoxes;

                if (bloodBar.HasValue)
                    _lastBloodBarDetection = now;
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"血條偵測錯誤: {ex.Message}");
            }
        }

        private void ProcessPlayerVitalsDetection(Mat frameMat, AppConfig config, DateTime now)
        {
            config.ReloadPlayerVitalsIfFileChanged();
            var vitalsSettings = config.PlayerVitals;
            if (!vitalsSettings.Enabled)
                return;

            var layout = PlayerVitalsDetector.ResolveLayout(
                frameMat.Width, frameMat.Height, vitalsSettings, frameMat);

            if (!layout.IsLayoutValid)
            {
                lock (_vitalsLock) { _currentPlayerVitals = layout; }
                return;
            }

            var elapsed = (now - _lastPlayerVitalsDetection).TotalMilliseconds;
            bool shouldMeasureFill = elapsed >= vitalsSettings.DetectIntervalMs;

            lock (_vitalsLock)
            {
                if (!shouldMeasureFill && _currentPlayerVitals?.HasFillReading == true)
                {
                    _currentPlayerVitals = _currentPlayerVitals with
                    {
                        HpBarRect = layout.HpBarRect,
                        MpBarRect = layout.MpBarRect,
                        UiBandRect = layout.UiBandRect,
                        FrameWidth = layout.FrameWidth,
                        FrameHeight = layout.FrameHeight,
                        IsLayoutValid = true
                    };
                    return;
                }
            }

            try
            {
                var measured = _playerVitalsDetector.Detect(frameMat, vitalsSettings);
                PlayerVitalsSnapshot snapshot = measured.IsLayoutValid
                    ? measured
                    : layout;

                if (measured.HasFillReading)
                {
                    lock (_vitalsLock)
                    {
                        snapshot = vitalsSettings.SmoothReadings
                            ? SmoothVitals(measured, _currentPlayerVitals, vitalsSettings.EmaAlpha)
                            : measured;
                        _currentPlayerVitals = snapshot;
                    }
                    _lastPlayerVitalsDetection = now;
                }
                else
                {
                    lock (_vitalsLock) { _currentPlayerVitals = layout; }
                }
            }
            catch (Exception ex)
            {
                lock (_vitalsLock) { _currentPlayerVitals = layout; }
                OnStatusMessage?.Invoke($"玩家血魔條偵測錯誤: {ex.Message}");
            }
        }

        private static PlayerVitalsSnapshot SmoothVitals(
            PlayerVitalsSnapshot raw,
            PlayerVitalsSnapshot? previous,
            double emaAlpha)
        {
            if (previous is not { HasFillReading: true })
                return raw;

            double alpha = Math.Clamp(emaAlpha, 0.05, 1);
            const double snapDelta = 0.12;
            return raw with
            {
                HpRatio = SmoothRatio(raw.HpRatio, previous.HpRatio, alpha, snapDelta),
                MpRatio = SmoothRatio(raw.MpRatio, previous.MpRatio, alpha, snapDelta)
            };
        }

        private static double SmoothRatio(double raw, double previous, double alpha, double snapDelta)
        {
            if (Math.Abs(raw - previous) >= snapDelta)
                return raw;

            return alpha * raw + (1 - alpha) * previous;
        }

        /// <summary>
        /// 怪物結果最大存活時間：來源幀年齡超過即整批清空釋放。
        /// 取 3 個偵測週期（下限 250ms）容忍背景偵測的正常延遲，同時擋住卡頓後的殭屍資料。
        /// </summary>
        private static int ResolveMonsterResultMaxAgeMs(AppConfig config) =>
            Math.Max(250, config.Vision.MonsterDetectIntervalMs * 3);

        /// <summary>來源幀過期的怪物結果整批清空，讓下游 fail-closed（無資料＝不攻擊）。</summary>
        private void PruneStaleMonsterResults(DateTime now, AppConfig config)
        {
            int maxAgeMs = ResolveMonsterResultMaxAgeMs(config);
            lock (_monsterLock)
            {
                if (_currentMonsters.Count == 0)
                    return;
                if ((now - _monsterResultCaptureUtc).TotalMilliseconds <= maxAgeMs)
                    return;

                _currentMonsters = new List<DetectionResult>();
            }

            Logger.Debug($"[GamePipeline] 怪物結果過期（>{maxAgeMs}ms）已清空，等待新偵測");
        }

        /// <summary>節流並非同步排程怪物辨識；進行中則以 Latest-Frame-Wins 覆蓋待處理 ROI。</summary>
        private void ScheduleMonsterDetection(Mat frameMat, DateTime captureTime, AppConfig config, DateTime now)
        {
            // 無論目前有無命中，一律節流；否則「搜尋中」會每幀全模板比對，config 形同虛設。
            var elapsed = (now - _lastMonsterDetection).TotalMilliseconds;
            if (elapsed < config.Vision.MonsterDetectIntervalMs) return;

            if (!_currentDetectionBoxes.Any()) return;
            if (MonsterCatalog.IsEmpty) return;

            var workItem = TryBuildMonsterWorkItem(frameMat, captureTime, config);
            if (workItem == null) return;

            // 排程當下打點：避免 in-flight 期間仍每幀 Clone ROI／覆蓋 pending。
            _lastMonsterDetection = now;

            if (Interlocked.CompareExchange(ref _monsterDetectionInFlight, 1, 0) != 0)
            {
                lock (_monsterJobLock)
                {
                    _pendingMonsterJob?.Dispose();
                    _pendingMonsterJob = workItem;
                    var replaced = Interlocked.Increment(ref _monsterJobReplacementCount);
                    if (replaced == 1 || replaced % MonsterJobReplacementLogInterval == 0)
                        Logger.Debug($"[GamePipeline] Latest-Frame-Wins 覆蓋待處理 ROI，累計 {replaced} 次");
                }
                return;
            }

            Task.Run(() => RunMonsterDetectionWorker(workItem));
        }

        private MonsterDetectionWorkItem? TryBuildMonsterWorkItem(Mat frameMat, DateTime captureTime, AppConfig config)
        {
            var detectionBoxes = _currentDetectionBoxes.ToList();
            var frameBounds = new Rect(0, 0, frameMat.Width, frameMat.Height);
            var crops = new List<(Rect CropRect, Mat Image)>();

            foreach (var detectionBox in detectionBoxes)
            {
                var cropRect = new Rect(detectionBox.X, detectionBox.Y, detectionBox.Width, detectionBox.Height);
                var validCropRect = frameBounds.Intersect(cropRect);
                if (validCropRect.Width < 10 || validCropRect.Height < 10) continue;

                try
                {
                    crops.Add((validCropRect, frameMat[validCropRect].Clone()));
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[GamePipeline] 怪物辨識 ROI 複製失敗: {ex.Message}");
                }
            }

            if (crops.Count == 0) return null;

            var detectionModeString = config.Vision.DetectionMode ?? "Color";
            if (!Enum.TryParse<MonsterDetectionMode>(detectionModeString, out var detectionMode))
                detectionMode = MonsterDetectionMode.Color;

            // ContourOnly：threshold 語意為 KenYu maxAllowedDiff；其餘模式為最低信心分數
            double detectionThreshold = detectionMode == MonsterDetectionMode.ContourOnly
                ? Math.Clamp(config.Vision.ContourDiffThreshold, 0.05, 1.0)
                : config.Vision.DefaultThreshold;

            return new MonsterDetectionWorkItem(
                crops,
                captureTime,
                MonsterCatalog,
                detectionMode,
                detectionThreshold,
                config.Vision.MaxDetectionResults);
        }

        private void RunMonsterDetectionWorker(MonsterDetectionWorkItem initialJob)
        {
            var job = initialJob;
            try
            {
                while (job != null)
                {
                    try
                    {
                        RunMonsterDetection(
                            job.Crops, job.CaptureTime, job.Catalog, job.DetectionMode,
                            job.DetectionThreshold, job.MaxResults);
                    }
                    catch (Exception ex)
                    {
                        OnStatusMessage?.Invoke($"怪物檢測錯誤: {ex.Message}");
                    }
                    finally
                    {
                        job.Dispose();
                    }

                    lock (_monsterJobLock)
                    {
                        job = _pendingMonsterJob;
                        _pendingMonsterJob = null;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _monsterDetectionInFlight, 0);
                Interlocked.Exchange(ref _monsterJobReplacementCount, 0);
            }

            MonsterDetectionWorkItem? lateJob;
            lock (_monsterJobLock)
            {
                lateJob = _pendingMonsterJob;
                _pendingMonsterJob = null;
            }

            if (lateJob == null) return;

            if (Interlocked.CompareExchange(ref _monsterDetectionInFlight, 1, 0) == 0)
                Task.Run(() => RunMonsterDetectionWorker(lateJob));
            else
                lateJob.Dispose();
        }

        private void RunMonsterDetection(
            List<(Rect CropRect, Mat Image)> crops,
            DateTime captureTime,
            MonsterDetectionCatalog catalog,
            MonsterDetectionMode detectionMode,
            double detectionThreshold,
            int maxResults)
        {
            var allResults = new List<DetectionResult>();
            MonsterTemplateMatchStats? aggregateStats = null;
            string? timedMonsterName = null;

            foreach (var (validCropRect, croppedMat) in crops)
            {
                foreach (var bundle in catalog.Bundles)
                {
                    var (results, stats) = _gameVision.FindMonstersWithStats(
                        croppedMat, bundle, detectionMode, detectionThreshold);

                    aggregateStats = aggregateStats.HasValue
                        ? MonsterTemplateMatchStats.Merge(aggregateStats.Value, stats)
                        : stats;
                    timedMonsterName ??= bundle.MonsterName;

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
            }

            if (aggregateStats.HasValue)
                LogMonsterMatchTiming(timedMonsterName ?? "unknown", aggregateStats.Value);

            // 怪血條關聯：預設關閉。血條多半攻擊後才出現，不可當發現過濾。
            var vision = AppConfig.Instance?.Vision;
            if (vision?.MonsterHpBarFilterEnabled == true && allResults.Count > 0)
            {
                var enemyBars = new List<SdRect>();
                foreach (var (validCropRect, croppedMat) in crops)
                {
                    var bars = GameVisionCore.FindEnemyHpBars(croppedMat, AppConfig.Instance!);
                    foreach (var bar in bars)
                    {
                        enemyBars.Add(new SdRect(
                            bar.X + validCropRect.X,
                            bar.Y + validCropRect.Y,
                            bar.Width,
                            bar.Height));
                    }
                }

                SdRect? playerBar = null;
                lock (_bloodBarLock)
                {
                    if (_currentBloodBars.Count > 0)
                        playerBar = _currentBloodBars[0];
                }

                int before = allResults.Count;
                allResults = GameVisionCore.FilterMonstersByEnemyHpBars(
                    allResults,
                    enemyBars,
                    vision.MonsterHpBarMaxGapPx > 0
                        ? vision.MonsterHpBarMaxGapPx
                        : 36,
                    playerBar);

                if (before != allResults.Count)
                {
                    OnStatusMessage?.Invoke(
                        $"怪血條過濾：{before} → {allResults.Count}（血條 {enemyBars.Count}）");
                }
            }

            List<DetectionResult> finalResults;
            if (allResults.Count > 1)
            {
                finalResults = GameVisionCore.ApplyNMS(allResults, iouThreshold: 0.3, higherIsBetter: true)
                    .OrderByDescending(r => r.Confidence).ToList();

                if (maxResults > 0 && finalResults.Count > maxResults)
                    finalResults = finalResults.Take(maxResults).ToList();
            }
            else
            {
                finalResults = allResults;
            }

            if (finalResults.Count > 0)
                OnStatusMessage?.Invoke($"檢測到 {finalResults.Count} 個怪物 (原始: {allResults.Count})");

            lock (_monsterLock)
            {
                // 只允許時間軸前進：晚到的舊幀結果不得覆蓋較新結果。
                if (captureTime >= _monsterResultCaptureUtc)
                {
                    _currentMonsters = finalResults;
                    _monsterResultCaptureUtc = captureTime;
                }
            }
        }

        private static void LogMonsterMatchTiming(string monsterName, MonsterTemplateMatchStats stats)
        {
            if (stats.TotalTemplates <= 0) return;

            string coarsePart = stats.UsedFullFallback
                ? $"粗篩 {stats.TotalTemplates}@{MonsterTemplateEntry.CoarseScale:0.##}× → fallback 精配 {stats.FineTemplates}"
                : $"粗篩 {stats.TotalTemplates}@{MonsterTemplateEntry.CoarseScale:0.##}× → Top {stats.FineTemplates} 精配";

            Logger.Debug(
                $"[怪物偵測] {monsterName} {coarsePart} 模板，" +
                $"down={stats.DownscaleMs:F1}ms score={stats.CoarseScoreMs:F1}ms " +
                $"fine={stats.FineMs:F1}ms total={stats.TotalMs:F1}ms");
        }

        private sealed class MonsterDetectionWorkItem : IDisposable
        {
            public MonsterDetectionWorkItem(
                List<(Rect CropRect, Mat Image)> crops,
                DateTime captureTime,
                MonsterDetectionCatalog catalog,
                MonsterDetectionMode detectionMode,
                double detectionThreshold,
                int maxResults)
            {
                Crops = crops;
                CaptureTime = captureTime;
                Catalog = catalog;
                DetectionMode = detectionMode;
                DetectionThreshold = detectionThreshold;
                MaxResults = maxResults;
            }

            public List<(Rect CropRect, Mat Image)> Crops { get; }

            /// <summary>來源幀擷取時間：結果的時間戳基準，供過期判斷。</summary>
            public DateTime CaptureTime { get; }

            public MonsterDetectionCatalog Catalog { get; }
            public MonsterDetectionMode DetectionMode { get; }
            public double DetectionThreshold { get; }
            public int MaxResults { get; }

            public void Dispose()
            {
                foreach (var (_, image) in Crops)
                    image.Dispose();
            }
        }
    }
}
