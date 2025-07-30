using System;

namespace ArtaleAI.LiveView
{
    /// <summary>
    /// 即時顯示事件處理介面
    /// </summary>
    public interface ILiveViewEventHandler
    {
        /// <summary>
        /// 當有新畫面可用時觸發
        /// </summary>
        void OnFrameAvailable(System.Drawing.Bitmap frame);

        /// <summary>
        /// 當狀態訊息需要顯示時觸發
        /// </summary>
        void OnStatusMessage(string message);

        /// <summary>
        /// 當發生錯誤時觸發
        /// </summary>
        void OnError(string errorMessage);
    }
}
