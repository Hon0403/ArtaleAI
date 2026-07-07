namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>執行層到達驗收策略；與 MapData 拓撲標記解耦。</summary>
    public enum ArrivalPolicy
    {
        /// <summary>繩索等：維持節點中心 3×3 / 繩索 Hitbox。</summary>
        PointHitbox,

        /// <summary>一般 Walk：X 對齊 + 平台折線投影 Y 帶。</summary>
        PlatformStand,

        /// <summary>Jump / SideJump 起跳：X 嚴格對齊 + 平台投影 Y，不比較標記 Y。</summary>
        JumpTakeoff,

        /// <summary>ClimbUp / ClimbDown 落地：嚴格 Y + 繩 X，不可用斜坡寬容帶。</summary>
        RopeLanding
    }
}
