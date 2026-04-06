namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>
    /// 定義底層移動控制器執行後的領域結果狀態
    /// 遵守領域驅動設計 (DDD)，將執行結果狀態從 Infrastructure 中抽離。
    /// </summary>
    public enum MovementResult
    {
        Success,
        Failed,
        Overshot
    }
}
