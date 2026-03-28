using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ArtaleAI.Core.Vision
{
    /// <summary>
    /// 模板管理器介面 — 負責 OpenCV Mat 模板的載入、快取與生命週期管理
    /// </summary>
    /// <remarks>
    /// 架構考量：提供唯一的 Mat 物件存取入口，隔離對實體型別的依賴。
    /// 讓所有偵測器「只取用模板」，不負責 Mat 的生成與釋放。
    /// </remarks>
    public interface ITemplateManager : IDisposable
    {
        /// <summary>
        /// 取得已載入的模板 Mat（唯讀，呼叫者不得 Dispose）
        /// </summary>
        /// <param name="templateName">模板名稱（對應檔案名稱，不含副檔名）</param>
        /// <returns>模板 Mat，若未載入則返回 null</returns>
        Mat? GetTemplate(string templateName);

        /// <summary>
        /// 非同步載入單一模板圖檔到快取
        /// </summary>
        /// <param name="templateName">模板名稱</param>
        /// <param name="filePath">圖檔的絕對路徑</param>
        /// <returns>載入是否成功</returns>
        Task<bool> LoadTemplateAsync(string templateName, string filePath);

        /// <summary>
        /// 非同步批量載入多個模板（從指定資料夾）
        /// </summary>
        /// <param name="folderPath">模板資料夾路徑</param>
        /// <param name="extensions">允許的副檔名（例如: ".png", ".jpg"）</param>
        /// <returns>成功載入的模板名稱列表</returns>
        Task<IReadOnlyList<string>> LoadTemplatesFromFolderAsync(
            string folderPath,
            IEnumerable<string>? extensions = null);

        /// <summary>
        /// 移除特定模板並釋放其 Mat 資源
        /// </summary>
        /// <param name="templateName">要移除的模板名稱</param>
        void UnloadTemplate(string templateName);

        /// <summary>
        /// 移除所有模板並釋放所有 Mat 資源
        /// </summary>
        void UnloadAll();

        /// <summary>
        /// 判斷特定模板是否已載入
        /// </summary>
        bool IsLoaded(string templateName);

        /// <summary>
        /// 取得所有已載入的模板名稱
        /// </summary>
        IReadOnlyList<string> GetLoadedTemplateNames();
    }
}
