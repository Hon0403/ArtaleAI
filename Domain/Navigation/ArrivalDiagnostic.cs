using System.Drawing;

namespace ArtaleAI.Domain.Navigation
{
    /// <summary>驗收診斷結果，用於 log 區分程式對齊失效與人工標記偏差。</summary>
    public sealed class ArrivalDiagnostic
    {
        public bool Passed { get; init; }
        public ArrivalPolicy Policy { get; init; }
        public string? NodeId { get; init; }
        public string? PlatformId { get; init; }
        public PointF PlayerPos { get; init; }
        public float TargetX { get; init; }
        public float AnchorY { get; init; }
        public float? ExpectedY { get; init; }
        public float? RopeX { get; init; }
        public float XErr { get; init; }
        public float YErrVsExpected { get; init; }
        public float YErrVsAnchor { get; init; }
        public float XTol { get; init; }
        public float YTol { get; init; }
        public bool HasProjection { get; init; }
        public bool Extrapolated { get; init; }
        public string FailReason { get; init; } = "";
        public string Attribution { get; init; } = "";

        public string FormatLine() =>
            $"[驗收診斷] {(Passed ? "通過" : "未通過")} attribution={Attribution} reason={FailReason} " +
            $"policy={Policy} node={NodeId ?? "-"} platform={PlatformId ?? "-"} " +
            $"player=({PlayerPos.X:F1},{PlayerPos.Y:F1}) targetX={TargetX:F1} anchorY={AnchorY:F1} " +
            $"expectedY={(ExpectedY.HasValue ? ExpectedY.Value.ToString("F1") : "-")} ropeX={(RopeX.HasValue ? RopeX.Value.ToString("F1") : "-")} " +
            $"xErr={XErr:F2} yErrExp={YErrVsExpected:F2} yErrAnchor={YErrVsAnchor:F2} " +
            $"xTol={XTol:F2} yTol={YTol:F2} projected={HasProjection} extrapolated={Extrapolated}";
    }
}
