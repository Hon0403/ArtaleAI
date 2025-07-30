using System;
using System.Collections.Generic;
using System.Drawing;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 怪物模板管理事件處理介面
    /// </summary>
    public interface IMonsterTemplateEventHandler
    {
        /// <summary>
        /// 獲取怪物模板資料夾路徑
        /// </summary>
        string GetMonstersDirectory();

        /// <summary>
        /// 當狀態訊息需要顯示時觸發
        /// </summary>
        void OnStatusMessage(string message);

        /// <summary>
        /// 當發生錯誤時觸發
        /// </summary>
        void OnError(string errorMessage);

        /// <summary>
        /// 當怪物模板載入完成時觸發
        /// </summary>
        void OnTemplatesLoaded(string monsterName, int templateCount);
    }
}
