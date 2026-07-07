namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>將拓撲節點轉為 Runtime <see cref="ExecutionTarget"/>。</summary>
    public static class ExecutionTargetTranslator
    {
        public static ExecutionTarget ForWaypoint(NavigationNode node)
        {
            var platformId = ResolvePlatformId(node);
            var policy = node.Type == NavigationNodeType.Rope
                ? ArrivalPolicy.PointHitbox
                : ArrivalPolicy.PlatformStand;

            return new ExecutionTarget
            {
                TargetX = node.Position.X,
                AnchorY = node.Position.Y,
                NodeId = node.Id,
                PlatformId = platformId,
                Policy = policy,
                PointHitbox = node.Hitbox
            };
        }

        /// <summary>起跳前水平對位；嚴格 X。</summary>
        public static ExecutionTarget ForJumpTakeoff(NavigationNode node)
        {
            return new ExecutionTarget
            {
                TargetX = node.Position.X,
                AnchorY = node.Position.Y,
                NodeId = node.Id,
                PlatformId = ResolvePlatformId(node),
                Policy = ArrivalPolicy.JumpTakeoff,
                PointHitbox = node.Hitbox
            };
        }

        /// <summary>跳躍落地驗收；與 Walk 相同採 <see cref="ArrivalPolicy.PlatformStand"/>。</summary>
        public static ExecutionTarget ForJumpLanding(NavigationNode node) => ForWaypoint(node);

        public static ExecutionTarget ForRopeLanding(NavigationNode node, float ropeX)
        {
            return new ExecutionTarget
            {
                TargetX = node.Position.X,
                AnchorY = node.Position.Y,
                NodeId = node.Id,
                PlatformId = ResolvePlatformId(node),
                RopeX = ropeX,
                Policy = ArrivalPolicy.RopeLanding,
                PointHitbox = node.Hitbox
            };
        }

        private static string? ResolvePlatformId(NavigationNode node)
        {
            if (!string.IsNullOrEmpty(node.PlatformId))
                return node.PlatformId;

            return NavigationNodeIdParser.TryParsePlatformId(node.Id, out var parsed)
                ? parsed
                : null;
        }
    }
}
