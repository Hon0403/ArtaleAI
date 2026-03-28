namespace ArtaleAI.Core.Vision
{
    /// <summary>
    /// 怪物檢測模式
    /// </summary>
    public enum MonsterDetectionMode
    {
        Basic,
        ContourOnly,
        Grayscale,
        Color,
        TemplateFree
    }

    /// <summary>
    /// 遮擋處理模式
    /// </summary>
    public enum OcclusionHandling
    {
        None,
        MorphologyRepair,
        DynamicThreshold,
        MultiScale
    }
}
