using System.Drawing;

namespace ArtaleAI
{
    /// <summary>
    /// 即時顯示事件處理介面
    /// </summary>
    public interface ILiveViewEventHandler
    {
        /// <summary>
        /// 當新的畫面可用時觸發
        /// </summary>
        void OnFrameAvailable(Bitmap frame);

        /// <summary>
        /// 顯示狀態訊息
        /// </summary>
        void OnStatusMessage(string message);

        /// <summary>
        /// 顯示錯誤訊息
        /// </summary>
        void OnError(string errorMessage);
    }
}
