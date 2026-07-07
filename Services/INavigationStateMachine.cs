using System;
using System.Threading.Tasks;
using ArtaleAI.Core.Domain.Navigation;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
{
    /// <summary>導航 FSM 契約：啟動邊、取消、外部到達通知。</summary>
    public interface INavigationStateMachine
    {
        /// <summary>目前狀態。</summary>
        NavigationState CurrentState { get; }

        /// <summary>狀態變更時觸發；(舊狀態, 新狀態)。</summary>
        event Action<NavigationState, NavigationState>? OnStateChanged;

        /// <summary>在 Idle 或 Reached_Waypoint 時啟動非同步邊執行；忙碌時回傳 false。</summary>
        bool TryStartNavigation(NavigationEdge edge, SdPointF currentPos, SdPointF targetPos);

        /// <summary>取消目前導航並回 Idle（reason 寫入日誌）。</summary>
        void CancelNavigation(string reason = "使用者強制中斷");

        /// <summary>Idle 補推進：僅在無 in-flight 且 SSOT 成立時推進 waypoint（單次去重）。</summary>
        void NotifyTargetReached();
    }
}
