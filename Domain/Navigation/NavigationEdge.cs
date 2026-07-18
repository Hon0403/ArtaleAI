using System;
using System.Collections.Generic;

namespace ArtaleAI.Domain.Navigation
{
    /// <summary>
    /// 導航動作類型
    /// </summary>
    public enum NavigationActionType
    {
        Walk = 1,
        Jump = 2,           // 手標跳躍；執行層依 ΔY 自動選原地/上跳或下跳
        SideJump = 3,       // 側跳 (自動判定左右)
        JumpDown = 4,       // 下跳
        Teleport = 5,       // 傳送
        ClimbUp = 6,        // 繩索：下平台 → 上平台
        ClimbDown = 7       // 繩索：上平台 → 下平台
    }

    /// <summary>
    /// 導航邊 (Navigation Edge)
    /// 代表兩個節點之間的連接關係與移動方式
    /// </summary>
    public class NavigationEdge
    {
        public string FromNodeId { get; set; }
        public string ToNodeId { get; set; }

        /// <summary>
        /// 移動方式
        /// </summary>
        public NavigationActionType ActionType { get; set; }

        /// <summary>
        /// 移動成本 (Cost)
        /// </summary>
        public float Cost { get; set; }

        /// <summary>
        /// 執行此移動所需的按鍵序列
        /// </summary>
        public List<string> InputSequence { get; set; } = new List<string>();

        public NavigationEdge(string fromId, string toId, NavigationActionType action, float cost = 1.0f)
        {
            FromNodeId = fromId;
            ToNodeId = toId;
            ActionType = action;
            Cost = cost;
        }

        public override string ToString()
        {
            return $"Edge: {FromNodeId.Substring(0, 4)} -> {ToNodeId.Substring(0, 4)} via {ActionType} (Cost: {Cost})";
        }
    }
}
