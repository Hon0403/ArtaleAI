using System;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>導航 FSM 狀態。</summary>
    public enum NavigationState
    {
        /// <summary>可接受新路徑；無進行中邊。</summary>
        Idle,

        /// <summary>Walk（水平長按）。</summary>
        Moving_Horizontal,

        /// <summary>爬繩上下。</summary>
        Moving_Vertical,

        /// <summary>跳躍／滯空。</summary>
        Jumping,

        /// <summary>傳送等短暫過渡。</summary>
        Transitioning,

        /// <summary>單邊完成，即將回 Idle 接下一邊。</summary>
        Reached_Waypoint,

        /// <summary>執行失敗，待救援或中止。</summary>
        Error
    }
}
