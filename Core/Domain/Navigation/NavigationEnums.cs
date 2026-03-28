namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>
    /// 定義底層移動控制器執行後的領域結果狀態
    /// 遵守領域驅動設計 (DDD)，將執行結果狀態從 Infrastructure 中抽離。
    /// </summary>
    public enum MovementResult
    {
        Success,          // 成功合法到達終點
        Failed,           // 發生超時、丟失、掉落或無效目標等失敗
        NeedsCorrection   // 觸發穿越防呆，但誤差過大，需要決策層進行微調
    }
}
