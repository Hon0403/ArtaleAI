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
using ArtaleAI.Domain.Input;
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
        private readonly InputLease _inputLease = new();
        private readonly FarmUiInterruptCoordinator _farmUiInterrupt;
        private readonly PartyHpBarRecoveryCoordinator _partyRecovery;
        private readonly object _uiAutomationFrameLock = new();
        private Mat? _uiAutomationFrameCache;
        private DateTime _lastBloodBarDetection = DateTime.MinValue;
        private DateTime _lastPlayerVitalsDetection = DateTime.MinValue;
        private DateTime _lastMonsterDetection = DateTime.MinValue;
        private long _nextVitalsReadingId;

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

        /// <summary>
        /// 單一休息點的尋路預算：超過即判定不可達並故障轉移到下一個候選點。
        /// 沒有這道停損，選點誤判（起點跨平台誤對齊、拓撲斷邊）會讓 Seeking
        /// 無限重試，而 Seeking 期間攻擊全面關閉＝自動打怪永久停擺。
        /// </summary>
        private const int RestSeekTimeoutSeconds = 45;

        private DateTime _restEndsAtUtc = DateTime.MinValue;
        private DateTime _nextRestDueUtc = DateTime.MinValue;
        private DateTime _nextRestSeekRetryUtc = DateTime.MinValue;
        private DateTime _restSeekStartedUtc = DateTime.MinValue;
        private string? _restSeekGoalNodeId;
        private readonly HashSet<string> _restSeekFailedNodeIds = new(StringComparer.Ordinal);

        /// <summary>
        /// 補給失敗撤退：None=正常；Seeking=前往安全區（導航開放、攻擊關閉）；
        /// Waiting=已到安全區等待血魔回升（導航與攻擊皆關閉）。
        /// </summary>
        private const int HealRetreatNone = 0;
        private const int HealRetreatSeeking = 1;
        private const int HealRetreatWaiting = 2;
        private volatile int _healRetreatPhase = HealRetreatNone;

        private const int HealRetreatSeekRetrySeconds = 3;
        private const int HealRetreatSeekTimeoutSeconds = 45;
        private DateTime _healRetreatSeekStartedUtc = DateTime.MinValue;
        private DateTime _nextHealRetreatSeekRetryUtc = DateTime.MinValue;
        private string? _healRetreatGoalNodeId;
        private readonly HashSet<string> _healRetreatFailedNodeIds = new(StringComparer.Ordinal);

        private int _monsterDetectionInFlight;
        private int _monsterJobReplacementCount;
        private readonly object _monsterJobLock = new();
        private MonsterDetectionWorkItem? _pendingMonsterJob;
        private const int MonsterJobReplacementLogInterval = 30;
        private DateTime _lastAttackTime = DateTime.MinValue;
        private DateTime _lastDirectionChangeTime = DateTime.MinValue;
        private int _heldAttackVirtualKey;
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

        /// <summary>補給無效後的安全區撤退／等待回補中。</summary>
        public bool IsHealRetreating => _healRetreatPhase != HealRetreatNone;

        /// <summary>正在前往安全區（補給失敗撤退）。</summary>
        public bool IsSeekingHealSafeZone => _healRetreatPhase == HealRetreatSeeking;

        /// <summary>小地圖遇人退避中。</summary>
        public bool IsAvoidingOtherPlayers => _otherPlayerAvoidance.IsAvoiding;

        /// <summary>隊伍血條重建序列進行中。</summary>
        public bool IsRecoveringParty => _partyRecovery.IsRecovering;

        /// <summary>
        /// 攻擊／小休倒數／補給等待／遇人退避／InputLease 獨佔期間，移動控制器應讓出方向鍵。
        /// 導航 FSM 仍應持續 tick（由 MovementController 在送鍵前讓路），不可連編排一起擋住。
        /// SeekingRest／SeekingHealSafeZone 不在此列：前往目標必須靠導航輸入。
        /// </summary>
        public bool BlocksNavigationInput =>
            _restPhase == RestPhaseResting
            || _healRetreatPhase == HealRetreatWaiting
            || _otherPlayerAvoidance.IsAvoiding
            || _inputLease.BlocksNavigationKeys;

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
            _farmUiInterrupt = new FarmUiInterruptCoordinator(_inputLease, OnExclusiveUiPreemptedCombat);
            _partyRecovery = new PartyHpBarRecoveryCoordinator(_inputLease, OnExclusiveUiPreemptedCombat);
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
                    || !_inputLease.IsIdle)
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

                if (AutoAttackEnabled && !IsHealRetreating)
                    ProcessAntiDetectRest(config, now);

                if (IsHealRetreating)
                    ProcessHealRetreat(config, now);

                // 垂直換層優先於長按攻擊：先搶回鍵盤，再允許導航送 ↓／Alt。
                if (ShouldDeferAttackForNavigation() && IsCombatInputHeld())
                    PreemptCombatForNavigation();

                // 攻擊只允許從本入口發起；怪物背景 worker 僅更新結果，不得自行送鍵。
                if (CanStartAttack())
                    ProcessAutoAttackDecision();

                // 導航編排不因攻擊租約而停 tick；實際方向鍵由 MovementController 讓路。
                if (trackingResult != null)
                    SignalNavigationOrchestration(trackingResult);

                double msAfterPathAttack = sw.Elapsed.TotalMilliseconds;

                ProcessBloodBarDetection(frameMat, config, now, trackingResult);
                ProcessPlayerVitalsDetection(frameMat, config, now);

                // 隊伍血條是攻擊框前置：主控台開始即持續監測，不等路徑／怪物就緒。
                if (PartyRecoveryEnabled)
                    TryRecoverPartyHpBar(trackingResult, config, now);

                if (AutoHealEnabled
                    && !_otherPlayerAvoidance.IsAvoiding
                    && _inputLease.IsIdle)
                    ProcessAutoHeal(config, now);

                if (AutoBuffEnabled
                    && !_otherPlayerAvoidance.IsAvoiding
                    && _inputLease.IsIdle)
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
        /// 攻擊決策的唯一閘門：換頻／清窗／退避／隊伍重建／進行中攻擊／冷卻皆不可再開新攻擊。
        /// 冷卻必須擋在入口，禁止「先 StopMovement 再因冷卻 return」空轉搶鍵。
        /// </summary>
        private bool CanStartAttack()
        {
            return AutoAttackEnabled
                && _restPhase == RestPhaseNone
                && _healRetreatPhase == HealRetreatNone
                && !_otherPlayerAvoidance.IsAvoiding
                && !_inputLease.BlocksCombatStart
                && AttackInputArbiter.IsCooldownReady(_lastAttackTime, DateTime.UtcNow);
        }

        /// <summary>已持有攻擊租約時，換頻／退避／休息／補給撤退／垂直導航發生則應立刻中止。</summary>
        private bool CanContinueAttack()
        {
            return AutoAttackEnabled
                && _restPhase == RestPhaseNone
                && _healRetreatPhase == HealRetreatNone
                && !_otherPlayerAvoidance.IsAvoiding
                && _inputLease.IsHeldBy(InputOwner.Combat)
                && !ShouldDeferAttackForNavigation();
        }

        private bool IsCombatInputHeld() =>
            _isAttacking
            || _inputLease.IsHeldBy(InputOwner.Combat)
            || Volatile.Read(ref _heldAttackVirtualKey) != 0;

        /// <summary>
        /// 垂直換層／休息尋路等更高優先活動接管鍵盤前呼叫：
        /// 立刻放鍵並釋放 Combat 租約，避免 Walk／JumpDown 假啟動。
        /// </summary>
        public void PreemptCombatForNavigation()
        {
            AbortCombatInputs();
            Interlocked.Exchange(ref _heldAttackVirtualKey, 0);
            _isAttacking = false;
            _inputLease.PreemptCombat();
            _lastAttackTime = DateTime.UtcNow;
        }

        /// <summary>UI 序列強佔 Combat 時放鍵；租約已由 InputLease 轉給 UI Owner。</summary>
        private void OnExclusiveUiPreemptedCombat()
        {
            AbortCombatInputs();
            Interlocked.Exchange(ref _heldAttackVirtualKey, 0);
            _isAttacking = false;
            _lastAttackTime = DateTime.UtcNow;
        }

        private bool ProcessAutoAttackDecision()
        {
            if (!TryCollectAttackTargets(out SdRect playerAttackBox, out List<DetectionResult> targets))
                return false;

            if (!_inputLease.TryAcquire(InputOwner.Combat))
                return false;

            _isAttacking = true;
            _ = PerformAutoAttackAsync(playerAttackBox, targets);
            return true;
        }

        /// <summary>釋放方向鍵與攻擊鍵，避免換頻／熄火時殘留 key-down。</summary>
        private void AbortCombatInputs()
        {
            _movementController?.StopMovement();
            if (_movementController == null)
                return;

            _movementController.SendKeyInput(VK_LEFT, keyUp: true);
            _movementController.SendKeyInput(VK_RIGHT, keyUp: true);
            _movementController.SendKeyInput(VK_CONTROL, keyUp: true);

            int heldVk = Interlocked.Exchange(ref _heldAttackVirtualKey, 0);
            if (heldVk != 0)
                _movementController.SendKeyInput((ushort)heldVk, keyUp: true);

            string? primary = AppConfig.Instance?.AutoFarm?.AttackPrimaryHotkey;
            if (!string.IsNullOrWhiteSpace(primary)
                && VirtualKeyParser.TryParse(primary, out ushort primaryVk)
                && primaryVk != VK_CONTROL
                && primaryVk != heldVk)
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

            targets = FilterAttackTargetsInBox(attackBox, monsters);
            return targets.Count > 0;
        }

        /// <summary>以最新怪物清單重驗攻擊框內目標，避免殭屍偵測在怪死後仍出手。</summary>
        private bool TryRefreshLiveAttackTargets(SdRect attackBox, out List<DetectionResult> targets)
        {
            List<DetectionResult> monsters;
            lock (_monsterLock) monsters = _currentMonsters.ToList();
            targets = FilterAttackTargetsInBox(attackBox, monsters);
            return targets.Count > 0;
        }

        private List<DetectionResult> FilterAttackTargetsInBox(SdRect attackBox, IReadOnlyList<DetectionResult> monsters)
        {
            var targets = new List<DetectionResult>();
            if (attackBox.IsEmpty || monsters.Count == 0)
                return targets;

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

            return targets;
        }

        /// <summary>
        /// 垂直移動／跳躍／起跳對位期間暫緩攻擊；Walk 巡邏允許同層攻擊。
        /// 必須在「飛行尚未開始、邊已是 JumpDown」時就讓出，否則長按會擋 ↓／Alt。
        /// </summary>
        private bool ShouldDeferAttackForNavigation()
        {
            var farm = AppConfig.Instance?.AutoFarm;
            if (farm != null && !farm.PreferNavigationOverAttack)
                return false;

            if (_pathPlanningManager == null || !_pathPlanningManager.IsRunning)
                return false;

            var tracker = _pathPlanningManager.Tracker;
            if (tracker.IsSideJumpApproachInProgress)
                return true;

            NavigationActionType? action =
                _pathPlanningManager.ActiveFlightActionType
                ?? tracker.CurrentNavigationEdge?.ActionType;

            return action is
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
            _restSeekGoalNodeId = null;
            _restSeekFailedNodeIds.Clear();
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
            CancelHealRetreatFlow(clearForcedGoal: true);
            _autoHeal.ClearFailureState();

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

            _restSeekFailedNodeIds.Clear();

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

            BeginSeekingToward(tracker, selection.Node!, now, "休息時間到");
        }

        /// <summary>注入強制目標並重置本次尋路的逾時基準點。</summary>
        private void BeginSeekingToward(
            PathPlanningTracker tracker,
            NavigationNode spot,
            DateTime now,
            string prefix)
        {
            // 休息尋路需要導航鍵；進行中長按必須立刻讓出。
            PreemptCombatForNavigation();
            tracker.SetForcedGoal(spot.Id);
            _restSeekGoalNodeId = spot.Id;
            _restSeekStartedUtc = now;
            _restPhase = RestPhaseSeeking;
            string spotKind = spot.Type == NavigationNodeType.Rope ? "繩索" : "安全區";
            OnStatusMessage?.Invoke($"{prefix}，前往{spotKind}休息點…");
        }

        /// <summary>
        /// SeekingRest：等待強制目標到達；規劃停擺時節流重試。
        /// 超過尋路預算即視為不可達，故障轉移到下一個候選休息點；
        /// 候選耗盡才降級原地小休。保證 Seeking 有界，不會凍結自動打怪。
        /// </summary>
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

            if ((now - _restSeekStartedUtc).TotalSeconds > RestSeekTimeoutSeconds)
            {
                FailOverToNextRestSpot(tracker, config, now);
                return;
            }

            if (tracker.IsForcedGoalPlanningParked && now >= _nextRestSeekRetryUtc)
            {
                _nextRestSeekRetryUtc = now.AddSeconds(RestSeekRetrySeconds);
                tracker.RetryForcedGoalPlanning();
            }
        }

        /// <summary>把逾時的休息點列入本輪黑名單並改選下一個；沒得選就原地小休。</summary>
        private void FailOverToNextRestSpot(PathPlanningTracker tracker, AppConfig config, DateTime now)
        {
            if (_restSeekGoalNodeId != null)
                _restSeekFailedNodeIds.Add(_restSeekGoalNodeId);

            tracker.ClearForcedGoal();
            _restSeekGoalNodeId = null;

            var graph = tracker.NavGraph;
            var playerPos = tracker.CurrentPathState?.CurrentPlayerPosition;
            if (graph == null || playerPos == null)
            {
                BeginRestCountdown(config, now, "導航資料失效，原地小休");
                return;
            }

            var selection = RestSpotSelector.Select(graph, playerPos.Value, _restSeekFailedNodeIds);
            if (selection.Outcome != RestSpotOutcome.Found)
            {
                BeginRestCountdown(config, now, "所有休息點皆不可達，原地小休");
                return;
            }

            Logger.Warning(
                $"[休息尋點] 前往休息點逾時（>{RestSeekTimeoutSeconds}s），" +
                $"改選 {selection.Node!.Id}（已排除 {_restSeekFailedNodeIds.Count} 個）");
            BeginSeekingToward(tracker, selection.Node, now, "休息點逾時");
        }

        private void BeginRestCountdown(AppConfig config, DateTime now, string? reason)
        {
            int durationSeconds = ResolveRestDurationSeconds(config.AutoFarm);
            _restPhase = RestPhaseResting;
            _restEndsAtUtc = now.AddSeconds(durationSeconds);
            PreemptCombatForNavigation();
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
                _inputLease.IsHeldBy(InputOwner.ChangeChannel)
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

            if (!_inputLease.TryAcquirePreemptingCombat(
                    InputOwner.ChangeChannel,
                    OnExclusiveUiPreemptedCombat))
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
                    _inputLease.Release(InputOwner.ChangeChannel);
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

            if (_inputLease.IsHeldBy(InputOwner.ChangeChannel)
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

            HealRetreatSignal signal = _autoHeal.EvaluateAndHeal(
                config.AutoFarm,
                vitals,
                now,
                TapSkillHotkey);

            if (signal.ShouldRetreat && _healRetreatPhase == HealRetreatNone)
            {
                BeginHealRetreat(signal.FailedResource, now);
                // 撤退狀態改由 Pipeline 持有；清掉失敗計數以便安全區內繼續喝水回補。
                _autoHeal.ClearFailureState();
            }
        }

        /// <summary>
        /// 遇人換頻／隊伍重建進行中為更高優先；此時不推進撤退尋路逾時，避免誤判安全區不可達。
        /// </summary>
        private bool IsHealRetreatPreemptedByHigherPriority() =>
            _otherPlayerAvoidance.IsAvoiding
            || _inputLease.Current is
                InputOwner.Party or InputOwner.ChangeChannel or InputOwner.FarmDismiss;

        private void BeginHealRetreat(HealResourceKind? failedResource, DateTime now)
        {
            CancelRestFlow();
            AbortCombatInputs();

            string label = failedResource switch
            {
                HealResourceKind.Hp => "HP",
                HealResourceKind.Mp => "MP",
                _ => "血魔"
            };

            _healRetreatFailedNodeIds.Clear();
            _healRetreatGoalNodeId = null;
            _nextHealRetreatSeekRetryUtc = DateTime.MinValue;
            _healRetreatSeekStartedUtc = now;
            _healRetreatPhase = HealRetreatSeeking;
            OnStatusMessage?.Invoke($"{label} 連續補給無效，前往安全區…");

            TryBeginHealRetreatSeek(now, announce: false);
        }

        private void CancelHealRetreatFlow(bool clearForcedGoal)
        {
            if (_healRetreatPhase == HealRetreatNone)
                return;

            _healRetreatPhase = HealRetreatNone;
            _healRetreatGoalNodeId = null;
            _healRetreatFailedNodeIds.Clear();
            _healRetreatSeekStartedUtc = DateTime.MinValue;
            _nextHealRetreatSeekRetryUtc = DateTime.MinValue;

            if (clearForcedGoal)
                RestNavigationTracker?.ClearForcedGoal();
        }

        private void ProcessHealRetreat(AppConfig config, DateTime now)
        {
            if (_healRetreatPhase == HealRetreatWaiting)
            {
                ProcessHealRetreatWaiting(config, now);
                return;
            }

            if (_healRetreatPhase != HealRetreatSeeking)
                return;

            if (IsHealRetreatPreemptedByHigherPriority())
                return;

            var tracker = RestNavigationTracker;
            if (tracker == null)
            {
                EnterHealRetreatWaiting("無導航資料，原地等待回補");
                return;
            }

            if (tracker.HasForcedGoal && tracker.IsForcedGoalArrived)
            {
                EnterHealRetreatWaiting(reason: null);
                return;
            }

            if (!tracker.HasForcedGoal)
            {
                TryBeginHealRetreatSeek(now, announce: true);
                return;
            }

            if ((now - _healRetreatSeekStartedUtc).TotalSeconds > HealRetreatSeekTimeoutSeconds)
            {
                FailOverHealRetreatSpot(tracker, now);
                return;
            }

            if (tracker.IsForcedGoalPlanningParked && now >= _nextHealRetreatSeekRetryUtc)
            {
                _nextHealRetreatSeekRetryUtc = now.AddSeconds(HealRetreatSeekRetrySeconds);
                tracker.RetryForcedGoalPlanning();
            }
        }

        private void ProcessHealRetreatWaiting(AppConfig config, DateTime now)
        {
            PlayerVitalsSnapshot? vitals;
            lock (_vitalsLock)
                vitals = _currentPlayerVitals;

            if (!_autoHeal.AreEnabledVitalsAboveThreshold(config.AutoFarm, vitals))
                return;

            RestNavigationTracker?.ClearForcedGoal();
            CancelHealRetreatFlow(clearForcedGoal: false);
            _autoHeal.ClearFailureState();
            OnStatusMessage?.Invoke("血魔已回補，繼續自動打怪");
        }

        private void EnterHealRetreatWaiting(string? reason)
        {
            _healRetreatPhase = HealRetreatWaiting;
            _movementController?.StopMovement();
            OnStatusMessage?.Invoke(reason ?? "已到安全區，等待血魔回補…");
        }

        private void TryBeginHealRetreatSeek(DateTime now, bool announce)
        {
            if (now < _nextHealRetreatSeekRetryUtc)
                return;

            var tracker = RestNavigationTracker;
            var graph = tracker?.NavGraph;
            var playerPos = tracker?.CurrentPathState?.CurrentPlayerPosition;

            if (tracker == null || graph == null || playerPos == null)
            {
                EnterHealRetreatWaiting("無導航資料，原地等待回補");
                return;
            }

            var selection = RestSpotSelector.Select(
                graph,
                playerPos.Value,
                _healRetreatFailedNodeIds,
                RestSpotCandidateMode.SafeZonesOnly);

            switch (selection.Outcome)
            {
                case RestSpotOutcome.NoCandidates:
                    EnterHealRetreatWaiting("地圖無安全區，原地等待回補");
                    return;

                case RestSpotOutcome.Unreachable:
                    _nextHealRetreatSeekRetryUtc = now.AddSeconds(HealRetreatSeekRetrySeconds);
                    if (announce)
                        OnStatusMessage?.Invoke("安全區暫時不可達，稍後重試…");
                    return;
            }

            BeginHealRetreatSeekingToward(tracker, selection.Node!, now, announce);
        }

        private void BeginHealRetreatSeekingToward(
            PathPlanningTracker tracker,
            NavigationNode spot,
            DateTime now,
            bool announce)
        {
            tracker.SetForcedGoal(spot.Id);
            _healRetreatGoalNodeId = spot.Id;
            _healRetreatSeekStartedUtc = now;
            _healRetreatPhase = HealRetreatSeeking;
            if (announce)
                OnStatusMessage?.Invoke("補給失敗，前往安全區…");
        }

        private void FailOverHealRetreatSpot(PathPlanningTracker tracker, DateTime now)
        {
            if (_healRetreatGoalNodeId != null)
                _healRetreatFailedNodeIds.Add(_healRetreatGoalNodeId);

            tracker.ClearForcedGoal();
            _healRetreatGoalNodeId = null;

            var graph = tracker.NavGraph;
            var playerPos = tracker.CurrentPathState?.CurrentPlayerPosition;
            if (graph == null || playerPos == null)
            {
                EnterHealRetreatWaiting("導航資料失效，原地等待回補");
                return;
            }

            var selection = RestSpotSelector.Select(
                graph,
                playerPos.Value,
                _healRetreatFailedNodeIds,
                RestSpotCandidateMode.SafeZonesOnly);

            if (selection.Outcome != RestSpotOutcome.Found)
            {
                EnterHealRetreatWaiting("所有安全區皆不可達，原地等待回補");
                return;
            }

            Logger.Warning(
                $"[補給撤退] 前往安全區逾時（>{HealRetreatSeekTimeoutSeconds}s），" +
                $"改選 {selection.Node!.Id}（已排除 {_healRetreatFailedNodeIds.Count} 個）");
            BeginHealRetreatSeekingToward(tracker, selection.Node, now, announce: true);
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

        /// <summary>
        /// 轉向最近目標並送出攻擊鍵；租約已由呼叫端 CAS 取得，結束時釋放。
        /// 主攻長按至框內無活怪；輪轉技仍為單次脈衝。
        /// </summary>
        private async Task PerformAutoAttackAsync(SdRect playerAttackBox, List<DetectionResult> targets)
        {
            try
            {
                if (targets == null || targets.Count == 0) return;
                if (_movementController == null) return;
                if (!CanContinueAttack()) return;

                var now = DateTime.UtcNow;
                if (!AttackInputArbiter.IsCooldownReady(_lastAttackTime, now))
                    return;

                if (!TryGetCurrentAttackBox(out SdRect attackBox))
                    attackBox = playerAttackBox;

                if (!TryRefreshLiveAttackTargets(attackBox, out var liveTargets))
                    return;

                if (!_attackRotation.TrySelectAttackKey(
                        AppConfig.Instance.AutoFarm,
                        now,
                        out ushort attackKey,
                        out string attackLabel,
                        out bool useHoldAttack))
                {
                    Logger.Warning("[自動攻擊] 主攻快捷鍵無法解析，略過此次攻擊");
                    return;
                }

                if (!TryRefreshLiveAttackTargets(attackBox, out liveTargets))
                    return;

                _movementController.StopMovement();
                await TryFaceNearestTargetAsync(attackBox, liveTargets).ConfigureAwait(false);

                if (!CanContinueAttack() || ShouldDeferAttackForNavigation())
                    return;

                if (!TryRefreshLiveAttackTargets(attackBox, out liveTargets))
                    return;

                if (useHoldAttack)
                    await HoldPrimaryAttackAsync(attackBox, attackKey, attackLabel, liveTargets).ConfigureAwait(false);
                else
                    await PulseSkillAttackAsync(attackBox, attackKey, attackLabel, liveTargets).ConfigureAwait(false);
            }
            finally
            {
                ReleaseHeldAttackKey();
                _isAttacking = false;
                _inputLease.Release(InputOwner.Combat);
            }
        }

        private bool TryGetCurrentAttackBox(out SdRect attackBox)
        {
            attackBox = _currentAttackRangeBoxes.FirstOrDefault();
            return !attackBox.IsEmpty;
        }

        private static DetectionResult PickNearestTarget(SdRect attackBox, IReadOnlyList<DetectionResult> targets)
        {
            var playerCenter = new SdPoint(
                attackBox.X + attackBox.Width / 2,
                attackBox.Y + attackBox.Height / 2);

            return targets.OrderBy(m =>
                Math.Abs((m.BoundingBox.X + m.BoundingBox.Width / 2) - playerCenter.X)).First();
        }

        private async Task TryFaceNearestTargetAsync(SdRect attackBox, IReadOnlyList<DetectionResult> targets)
        {
            if (_movementController == null || targets.Count == 0)
                return;

            var now = DateTime.UtcNow;
            if (!AttackInputArbiter.ShouldRetapFacing(_lastDirectionChangeTime, now))
                return;

            var target = PickNearestTarget(attackBox, targets);
            var playerCenter = new SdPoint(
                attackBox.X + attackBox.Width / 2,
                attackBox.Y + attackBox.Height / 2);
            int monsterCenterX = target.BoundingBox.X + target.BoundingBox.Width / 2;
            ushort directionKey = monsterCenterX < playerCenter.X ? VK_LEFT : VK_RIGHT;

            _movementController.SendKeyInput(directionKey, false);
            await Task.Delay(AttackInputArbiter.SkillPulseMs).ConfigureAwait(false);
            _movementController.SendKeyInput(directionKey, true);
            _lastDirectionChangeTime = DateTime.UtcNow;
        }

        private void PressAttackKey(ushort attackKey)
        {
            Volatile.Write(ref _heldAttackVirtualKey, attackKey);
            _movementController!.SendKeyInput(attackKey, false);
        }

        private void ReleaseHeldAttackKey()
        {
            int vk = Interlocked.Exchange(ref _heldAttackVirtualKey, 0);
            if (vk != 0)
                _movementController?.SendKeyInput((ushort)vk, true);
        }

        /// <summary>楓之谷主攻：長按至攻擊框內無活怪或導航需接管。</summary>
        private async Task HoldPrimaryAttackAsync(
            SdRect attackBox,
            ushort attackKey,
            string attackLabel,
            List<DetectionResult> liveTargets)
        {
            var target = PickNearestTarget(attackBox, liveTargets);
            PressAttackKey(attackKey);
            OnStatusMessage?.Invoke($"自動攻擊: 按住 {target.Name}（{attackLabel}）");

            var holdStarted = DateTime.UtcNow;
            while (CanContinueAttack() && !ShouldDeferAttackForNavigation())
            {
                if ((DateTime.UtcNow - holdStarted).TotalMilliseconds >= AttackInputArbiter.MaxHoldMs)
                    break;

                if (!TryGetCurrentAttackBox(out attackBox))
                    break;

                if (!TryRefreshLiveAttackTargets(attackBox, out liveTargets))
                    break;

                await TryFaceNearestTargetAsync(attackBox, liveTargets).ConfigureAwait(false);
                await Task.Delay(AttackInputArbiter.HoldPollMs).ConfigureAwait(false);
            }

            ReleaseHeldAttackKey();
            _lastAttackTime = DateTime.UtcNow;
        }

        private async Task PulseSkillAttackAsync(
            SdRect attackBox,
            ushort attackKey,
            string attackLabel,
            List<DetectionResult> liveTargets)
        {
            var target = PickNearestTarget(attackBox, liveTargets);
            _movementController!.SendKeyInput(attackKey, false);
            await Task.Delay(AttackInputArbiter.SkillPulseMs).ConfigureAwait(false);

            if (!CanContinueAttack())
            {
                _movementController.SendKeyInput(attackKey, true);
                return;
            }

            _movementController.SendKeyInput(attackKey, true);
            _lastAttackTime = DateTime.UtcNow;
            OnStatusMessage?.Invoke($"自動攻擊: 鎖定 {target.Name}（{attackLabel}）");
        }

        /// <summary>每幀更新玩家座標 SSOT（攻擊期間仍執行）；並同步卡點豁免旗標。</summary>
        private void FeedPlayerTracking(MinimapTrackingResult result)
        {
            if (_pathPlanningManager == null || !_pathPlanningManager.IsRunning) return;

            try
            {
                // 攻擊／換頻等佔鍵期間靜止是預期行為，不得累積卡點與救援熔斷。
                _pathPlanningManager.Tracker.SuppressStuckDetection = BlocksNavigationInput;
                _pathPlanningManager.ProcessTrackingResult(result);
            }
            catch (Exception ex)
            {
                Logger.Error($"[GamePipeline] 追蹤座標更新錯誤: {ex.Message}");
            }
        }

        /// <summary>推進導航 FSM tick（攻擊期間仍編排；送鍵由 MovementController 讓路）。</summary>
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
                    long readingId = Interlocked.Increment(ref _nextVitalsReadingId);
                    lock (_vitalsLock)
                    {
                        var stamped = measured with
                        {
                            ReadingId = readingId,
                            MeasuredAtUtc = now
                        };
                        snapshot = vitalsSettings.SmoothReadings
                            ? SmoothVitals(stamped, _currentPlayerVitals, vitalsSettings.EmaAlpha)
                            : stamped;
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
                MpRatio = SmoothRatio(raw.MpRatio, previous.MpRatio, alpha, snapDelta),
                ReadingId = raw.ReadingId,
                MeasuredAtUtc = raw.MeasuredAtUtc
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
        /// 戰鬥中縮短殭屍框壽命，讓長按主攻較快在怪死後放開。
        /// </summary>
        private static int ResolveMonsterResultMaxAgeMs(AppConfig config, bool combatActive)
        {
            int interval = Math.Max(50, config.Vision.MonsterDetectIntervalMs);
            int multiplier = combatActive ? 2 : 3;
            int floor = combatActive ? 180 : 250;
            return Math.Max(floor, interval * multiplier);
        }

        private bool IsCombatMonsterRefreshActive() =>
            _isAttacking || Volatile.Read(ref _heldAttackVirtualKey) != 0;

        /// <summary>戰鬥期間加速怪物掃描，避免長按時仍吃 200ms 級殭屍框。</summary>
        private int ResolveMonsterDetectIntervalMs(AppConfig config)
        {
            int interval = Math.Max(50, config.Vision.MonsterDetectIntervalMs);
            if (!IsCombatMonsterRefreshActive())
                return interval;

            return Math.Max(80, interval / 2);
        }

        /// <summary>來源幀過期的怪物結果整批清空，讓下游 fail-closed（無資料＝不攻擊）。</summary>
        private void PruneStaleMonsterResults(DateTime now, AppConfig config)
        {
            int maxAgeMs = ResolveMonsterResultMaxAgeMs(config, IsCombatMonsterRefreshActive());
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
            if (elapsed < ResolveMonsterDetectIntervalMs(config)) return;

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
