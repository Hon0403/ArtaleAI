using System;

namespace ArtaleAI.Domain.Navigation
{
    /// <summary>
    /// 導航節點類型
    /// </summary>
    public enum NavigationNodeType
    {
        Platform,
        Rope
    }

    /// <summary>
    /// 導航節點 (Navigation Node)
    /// 代表地圖上一個可站立或可到達的關鍵位置
    /// </summary>
    public class NavigationNode
    {
        /// <summary>
        /// 節點唯一識別碼
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 空間座標 (使用浮點數以支援更精確的定位)
        /// </summary>
        public System.Drawing.PointF Position { get; set; }

        /// <summary>
        /// 節點類型
        /// </summary>
        public NavigationNodeType Type { get; set; } = NavigationNodeType.Platform;

        /// <summary>
        /// 所在的平台 ID
        /// </summary>
        public string? PlatformId { get; set; }

        /// <summary>
        /// 策略旗標：來自平台折點 IsSafeZone，不影響連通性。
        /// </summary>
        public bool IsSafeZone { get; set; }

        /// <summary>
        /// 導航觸發區域 (Hitbox)
        /// </summary>
        public BoundingBox? Hitbox { get; set; }

        public NavigationNode(float x, float y)
        {
            Position = new System.Drawing.PointF(x, y);
        }

        public override string ToString()
        {
            return $"Node({Id.Substring(0, 4)}): {Position.X:F0},{Position.Y:F0} [{Type}]";
        }
    }
}
