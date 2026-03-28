using System;
using System.Collections.Generic;
using ArtaleAI.Utils;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>
    /// 導航節點類型
    /// </summary>
    public enum NavigationNodeType
    {
        Platform,   // 一般平台行走點
        Rope,       // 繩索點
        Portal,     // 傳送門
        MonsterZone // 怪物區
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
        /// 節點名稱 (可選，用於編輯器顯示)
        /// </summary>
        public string? Name { get; set; }

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
        /// 導航觸發區域 (Hitbox)
        /// </summary>
        public BoundingBox? Hitbox { get; set; }

        /// <summary>
        /// 該節點的自定義屬性 (例如: 是否允許傳送、是否需要特殊道具)
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

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
