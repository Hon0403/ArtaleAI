namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>Runtime 執行目標；由節點錨點轉譯，供移動與驗收消費。</summary>
    public sealed class ExecutionTarget
    {
        public float TargetX { get; init; }
        public float AnchorY { get; init; }
        public string? NodeId { get; init; }
        public string? PlatformId { get; init; }
        public float? RopeX { get; init; }
        public ArrivalPolicy Policy { get; init; }
        public BoundingBox? PointHitbox { get; init; }

        public string Describe() =>
            $"node={NodeId} x={TargetX:F1} policy={Policy} platform={PlatformId ?? "-"} ropeX={RopeX?.ToString("F1") ?? "-"}";
    }
}
