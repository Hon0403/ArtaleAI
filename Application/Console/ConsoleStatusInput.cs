using ArtaleAI.Domain.Navigation;

namespace ArtaleAI.Application.Console
{
    /// <summary>主控台一次刷新所需的輸入快照；由 Form 從服務／控件採集，Presenter 只負責轉換。</summary>
    public sealed class ConsoleStatusInput
    {
        public string GameWindowTitle { get; init; } = string.Empty;
        public bool GameFound { get; init; }
        public bool CaptureRunning { get; init; }
        public bool IsResting { get; init; }
        public bool IsAvoidingOtherPlayers { get; init; }
        public NavigationState FsmState { get; init; } = NavigationState.Idle;
        public double? HpRatio { get; init; }
        public double? MpRatio { get; init; }
        public bool HasVitalsReading { get; init; }
        /// <summary>剛補藥／冷卻中等短提示；無則 null。</summary>
        public string? HealStatusHint { get; init; }
        /// <summary>剛施放補助技能等短提示；無則 null。</summary>
        public string? BuffStatusHint { get; init; }
        /// <summary>剛用攻擊輪轉技等短提示；無則 null。</summary>
        public string? AttackStatusHint { get; init; }
        /// <summary>遇人換頻／退避短提示；無則 null。</summary>
        public string? OtherPlayerAvoidanceHint { get; init; }
        public bool PathRunning { get; init; }
        public int WaypointIndex { get; init; }
        public int WaypointTotal { get; init; }
        public double DistanceToNextWaypoint { get; init; }
        public bool AutoStartChecked { get; init; }
        public bool PathFileSelected { get; init; }
        public bool DetectModeSelected { get; init; }
        public bool MonsterSelected { get; init; }
        public int PlatformNodeCount { get; init; }
    }
}
