namespace ArtaleAI.Vision
{
    /// <summary>
    /// 怪物檢測模式。
    /// ContourOnly／Grayscale 有獨立分支；Color 為預設彩色模板路徑。
    /// </summary>
    public enum MonsterDetectionMode
    {
        ContourOnly,
        Grayscale,
        Color
    }
}
