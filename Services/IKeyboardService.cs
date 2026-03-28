using System.Threading;
using System.Threading.Tasks;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 鍵盤操作的抽象介面。
    /// 架構考量：將 Win32 SendInput 的實作細節隔離在 Infrastructure 層，
    /// 讓 Application 層（NavigationExecutor）可以在不依賴 Windows API 的情況下編排按鍵序列。
    /// 這也使得未來可以替換為其他輸入方式（例如記憶體注入、遊戲內 API）而不影響上層邏輯。
    /// </summary>
    public interface IKeyboardService
    {
        /// <summary>按下或釋放指定按鍵</summary>
        /// <param name="vkCode">虛擬鍵碼</param>
        /// <param name="keyUp">true = 釋放, false = 按下</param>
        void SendKey(ushort vkCode, bool keyUp);

        /// <summary>短按指定按鍵（按下→等待→釋放）</summary>
        Task TapKeyAsync(ushort vkCode, int durationMs, CancellationToken ct = default);

        /// <summary>長按方向鍵持續移動（自動處理方向切換）</summary>
        bool HoldKey(ushort vkCode);

        /// <summary>釋放所有按住的按鍵並停止移動</summary>
        void StopMovement();
    }
}
