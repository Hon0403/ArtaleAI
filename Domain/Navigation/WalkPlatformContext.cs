using System.Drawing;

namespace ArtaleAI.Domain.Navigation
{
    /// <summary>Walk 執行時的平台幾何上下文，供移動層 Y 漂移診斷。</summary>
    public sealed class WalkPlatformContext
    {
        public string? PlatformId { get; init; }
        public PlatformGeometryIndex? Geometry { get; init; }
        public string? NodeId { get; init; }
    }
}
