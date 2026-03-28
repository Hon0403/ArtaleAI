using System;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>
    /// 導航系統狀態機的有限狀態枚舉 (FSM States)
    /// 嚴格定義角色在自動導航過程中所處的每一種狀態，確保單一時間只有一種行為發生。
    /// </summary>
    public enum NavigationState
    {
        /// <summary>
        /// 待機中：系統尚未啟動導航，或已抵達最終目的地，目前沒有任何移動指令。
        /// 這是唯一允許接受「全新路徑/全新節點」指令的安全狀態。
        /// </summary>
        Idle,

        /// <summary>
        /// 水平移動中：角色正在執行地圖上的左右行走 (Walk)。
        /// 物理對應：長按 ← 或 →。
        /// </summary>
        Moving_Horizontal,

        /// <summary>
        /// 垂直移動中：角色正在執行爬繩 (ClimbUp / ClimbDown) 動作。
        /// 物理對應：長按 ↑ 或 ↓，並且可能處於 Alt+↑ 的吸附狀態。
        /// </summary>
        Moving_Vertical,

        /// <summary>
        /// 滯空/跳躍中：角色正在執行跳躍等不可中斷的空中動作。
        /// 物理對應：Alt + 組合鍵。此狀態極度脆弱，通常必須等待落地 (Y 座標穩定) 才能離開此狀態。
        /// </summary>
        Jumping,

        /// <summary>
        /// 動作過渡/冷卻中：角色正在執行需要時間完成的短暫動作（例如進入傳送門）。
        /// 物理對應：短按 ↑ 後的畫面黑屏讀取時間，或攻擊動作。
        /// </summary>
        Transitioning,

        /// <summary>
        /// 抵達節點：角色剛完成一個大腦指派的 Edge，成功到達目標座標。
        /// 這是一個極短暫的過渡狀態，用於觸發大腦去更新下一條 Edge，並將狀態流轉回 Idle 以迎接新命令。
        /// </summary>
        Reached_Waypoint,

        /// <summary>
        /// 錯誤/卡死：導航過程中發生不可恢復的異常（例如落海、卡點超時、被傳送到未知道具房）。
        /// 需要大腦介入重新定位或報警。
        /// </summary>
        Error
    }
}
