using System.Threading;
using System.Threading.Tasks;

namespace ArtaleAI.Services
{
    /// <summary>鍵盤輸入抽象，與 Win32 實作解耦。</summary>
    public interface IKeyboardService
    {
        /// <param name="keyUp">true 為放開。</param>
        void SendKey(ushort vkCode, bool keyUp);

        Task TapKeyAsync(ushort vkCode, int durationMs, CancellationToken ct = default);

        bool HoldKey(ushort vkCode);

        void StopMovement();
    }
}
