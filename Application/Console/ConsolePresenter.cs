using ArtaleAI.Domain.Navigation;

namespace ArtaleAI.Application.Console
{
    /// <summary>
    /// 聚合主控台顯示狀態。不持有 WinForms、不呼叫 WindowFinder；
    /// 輸入由 UI 邊界組裝，輸出為可綁定的 ConsoleViewState。
    /// </summary>
    public sealed class ConsolePresenter
    {
        public ConsoleViewState Build(ConsoleStatusInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            int hpPercent = ToPercent(input.HpRatio);
            int mpPercent = ToPercent(input.MpRatio);
            bool hasVitals = input.HasVitalsReading;
            var prerequisites = BuildPrerequisites(input);

            return new ConsoleViewState
            {
                GameWindowLabel = FormatGameWindowLabel(input),
                GameWindowTone = input.GameFound
                    ? ConsoleStatusTone.Positive
                    : ConsoleStatusTone.Danger,

                StatusGame = input.GameFound ? "遊戲：已連線" : "遊戲：未找到",
                StatusGameTone = input.GameFound
                    ? ConsoleStatusTone.Neutral
                    : ConsoleStatusTone.Danger,

                StatusCapture = FormatCapture(input),
                StatusCaptureTone = input.IsResting
                    ? ConsoleStatusTone.Accent
                    : ConsoleStatusTone.Neutral,

                StatusFsm = FormatFsm(input),
                StatusFsmTone = ResolveFsmTone(input),

                StatusVitals = FormatVitals(input, hpPercent, mpPercent, hasVitals),
                StatusVitalsTone = string.IsNullOrEmpty(input.HealStatusHint)
                    ? ConsoleStatusTone.Neutral
                    : ConsoleStatusTone.Accent,

                StatusPath = FormatPath(input),
                StatusPathTone = ConsoleStatusTone.Neutral,

                PrerequisitesText = prerequisites.Text,
                PrerequisitesTone = prerequisites.Tone,

                HpPercent = hpPercent,
                MpPercent = mpPercent,
                HasVitalsReading = hasVitals
            };
        }

        private static string FormatGameWindowLabel(ConsoleStatusInput input)
        {
            string title = input.GameWindowTitle;
            return input.GameFound
                ? $"遊戲視窗：已找到（{title}）"
                : $"遊戲視窗：未找到（{title}）";
        }

        private static string FormatCapture(ConsoleStatusInput input)
        {
            if (input.IsResting)
                return "擷取：小休中";

            return input.CaptureRunning ? "擷取：運行中" : "擷取：停止";
        }

        private static string FormatVitals(
            ConsoleStatusInput input,
            int hpPercent,
            int mpPercent,
            bool hasVitals)
        {
            string core = hasVitals
                ? $"HP {hpPercent}% · MP {mpPercent}%"
                : "HP — · MP —";

            if (string.IsNullOrEmpty(input.HealStatusHint))
                return core;

            return $"{core} · {input.HealStatusHint}";
        }

        private static string FormatFsm(ConsoleStatusInput input)
        {
            if (input.IsResting)
                return "導航：小休中";

            return $"導航：{FormatFsmState(input.FsmState)}";
        }

        private static ConsoleStatusTone ResolveFsmTone(ConsoleStatusInput input)
        {
            if (input.IsResting)
                return ConsoleStatusTone.Accent;

            return input.FsmState == NavigationState.Error
                ? ConsoleStatusTone.Danger
                : ConsoleStatusTone.Neutral;
        }

        private static string FormatPath(ConsoleStatusInput input)
        {
            if (!input.PathRunning || input.WaypointTotal <= 0)
                return "路徑：—";

            return $"路點 {input.WaypointIndex + 1}/{input.WaypointTotal} · 距離 {input.DistanceToNextWaypoint:F1}";
        }

        private static (string Text, ConsoleStatusTone Tone) BuildPrerequisites(ConsoleStatusInput input)
        {
            if (!input.AutoStartChecked)
                return ("尚未啟動", ConsoleStatusTone.Neutral);

            var warnings = new List<string>();

            if (!input.GameFound)
                warnings.Add($"找不到遊戲視窗：{input.GameWindowTitle}");

            if (!input.PathFileSelected)
                warnings.Add("請選擇路徑檔");

            if (!input.DetectModeSelected)
                warnings.Add("請選擇偵測模式");

            if (!input.MonsterSelected)
                warnings.Add("請勾選至少一種要打的怪");

            if (input.PlatformNodeCount <= 0)
                warnings.Add("路徑檔無平台節點，導航不會啟動");

            if (warnings.Count == 0)
                return ("就緒，自動打怪運行中", ConsoleStatusTone.Positive);

            return (string.Join(Environment.NewLine, warnings), ConsoleStatusTone.Warning);
        }

        private static int ToPercent(double? ratio)
        {
            if (ratio is null)
                return 0;

            return (int)Math.Round(Math.Clamp(ratio.Value, 0, 1) * 100);
        }

        private static string FormatFsmState(NavigationState state) => state switch
        {
            NavigationState.Idle => "閒置",
            NavigationState.Moving_Horizontal => "水平移動",
            NavigationState.Moving_Vertical => "爬繩",
            NavigationState.Jumping => "跳躍",
            NavigationState.Transitioning => "過渡",
            NavigationState.Reached_Waypoint => "抵達路點",
            NavigationState.Error => "錯誤",
            _ => state.ToString()
        };
    }
}
