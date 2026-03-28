using System;
using System.Threading.Tasks;
using ArtaleAI.Core.Domain.Navigation;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 導航有限狀態機 (FSM) 介面
    /// 負責接收導航指令，並確保狀態的合法轉換與死鎖防護。
    /// </summary>
    public interface INavigationStateMachine
    {
        /// <summary>
        /// 取得狀態機目前的狀態
        /// </summary>
        NavigationState CurrentState { get; }

        /// <summary>
        /// 狀態改變時觸發的事件，供外部 (如 UI 或大腦) 監聽以更新畫面或日誌
        /// 參數：(前一個狀態, 新狀態)
        /// </summary>
        event Action<NavigationState, NavigationState>? OnStateChanged;

        /// <summary>
        /// 嘗試指示狀態機開始執行一段導航邊 (Edge)。
        /// 此為同步方法，內部具備狀態鎖防護，拒絕執行時回傳 false，不拋出例外。
        /// </summary>
        /// <param name="edge">要執行的 Edge (包含動作類型如 Walk, ClimbUp)</param>
        /// <param name="currentPos">角色當前座標</param>
        /// <param name="targetPos">預期的終點座標</param>
        /// <returns>若成功改變狀態並啟動任務則回傳 true，否則回傳 false</returns>
        bool TryStartNavigation(NavigationEdge edge, SdPointF currentPos, SdPointF targetPos);

        /// <summary>
        /// 緊急中斷當前的所有導航動作，並將狀態強制切換回 Idle
        /// </summary>
        /// <param name="reason">中斷的原因（供日誌記錄）</param>
        void CancelNavigation(string reason = "使用者強制中斷");

        /// <summary>
        /// 通知狀態機角色已經到達目標容許範圍內。
        /// 用以提早結束 Moving_Horizontal / Moving_Vertical 狀態，轉入 Reached_Waypoint。
        /// </summary>
        void NotifyTargetReached();
    }
}
