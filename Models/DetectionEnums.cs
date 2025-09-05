namespace ArtaleAI.Models
{
    /// <summary>
    /// 模板匹配模式枚舉
    /// </summary>
    public enum MonsterDetectionMode
    {
        Basic,       // 基本模板匹配（保持相容性）
        ContourOnly, // 僅輪廓匹配
        Grayscale,   // 灰階匹配
        Color,       // 彩色匹配
        TemplateFree // 無模板自由偵測
    }

    /// <summary>
    /// 遮擋感知處理模式
    /// </summary>
    public enum OcclusionHandling
    {
        None,              // 不處理遮擋
        MorphologyRepair,  // 形態學修復
        DynamicThreshold,  // 動態閾值
        MultiScale         // 多尺度匹配
    }
}
