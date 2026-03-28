using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace ArtaleAI.Core.Vision
{
    // ============================================================
    // 架構決策：介面隔離原則 (ISP) 的應用
    //
    // 為什麼不用一個大介面？
    // GameVisionCore 最大的問題就是把三種完全不同的職責（血條、小地圖、怪物）
    // 塞進同一個類別。一個 2000 行的上帝類別，只是在等著爆炸。
    //
    // 設計層次：
    //   IVisionDetector（基礎契約）
    //     ├── IBloodBarDetector（血條偵測）
    //     ├── IMinimapDetector（小地圖追蹤）
    //     └── IMonsterDetector（怪物偵測 + NMS 工具）
    //
    // 每個偵測器的相依物件：
    //   - ITemplateManager：唯一能存取 Mat 的物件（建構子注入）
    //   - AppConfig：設定參數（建構子注入）
    //
    // 這樣的好處：
    //   1. GamePipeline 只需依賴它真正使用的介面
    //   2. 測試時可以 Mock 任一個偵測器
    //   3. 未來替換演算法（例如換成 YOLO）只需換一個實作
    // ============================================================

    // ─── 基礎介面 ────────────────────────────────────────────────

    /// <summary>
    /// 所有視覺偵測器的基礎契約
    /// </summary>
    public interface IVisionDetector : IDisposable
    {
        /// <summary>偵測器名稱（用於 Log 識別）</summary>
        string Name { get; }
    }

    // ─── 血條偵測介面 ─────────────────────────────────────────────

    /// <summary>
    /// 血條偵測器介面 — 負責找出角色血條，並計算相關的偵測框
    /// </summary>
    public interface IBloodBarDetector : IVisionDetector
    {
        /// <summary>
        /// 偵測畫面中的角色血條（紅色 HSV 色彩空間）
        /// </summary>
        /// <param name="frameMat">來源畫面</param>
        /// <param name="uiExcludeRect">要排除的 UI 區域（可選）</param>
        /// <param name="cameraOffsetY">輸出：相機垂直偏移量</param>
        /// <returns>血條矩形區域，未偵測到時返回 null</returns>
        System.Drawing.Rectangle? DetectBloodBar(
            Mat frameMat,
            System.Drawing.Rectangle? uiExcludeRect,
            out float cameraOffsetY);

        /// <summary>
        /// 完整血條偵測：同時計算血條、怪物偵測框、攻擊範圍框
        /// 對應 GameVisionCore.ProcessBloodBarDetection
        /// </summary>
        /// <returns>(bloodBarRect?, detectionBoxes, attackRangeBoxes)</returns>
        (System.Drawing.Rectangle? BloodBar,
         List<System.Drawing.Rectangle> DetectionBoxes,
         List<System.Drawing.Rectangle> AttackRangeBoxes)
        ProcessBloodBarDetection(Mat frameMat, System.Drawing.Rectangle? uiExcludeRect);
    }

    // ─── 小地圖偵測介面 ───────────────────────────────────────────

    /// <summary>
    /// 小地圖偵測器介面 — 負責在畫面中尋找小地圖及追蹤角色位置
    /// </summary>
    public interface IMinimapDetector : IVisionDetector
    {
        /// <summary>
        /// 在畫面中尋找小地圖的矩形位置
        /// 對應 GameVisionCore.FindMinimapOnScreen
        /// </summary>
        System.Drawing.Rectangle? FindMinimapOnScreen(Mat fullFrameMat);

        /// <summary>
        /// 完整小地圖追蹤（包含玩家位置與其他玩家位置）
        /// 對應 GameVisionCore.GetMinimapTracking(Mat, DateTime)
        /// </summary>
        /// <param name="fullFrameMat">完整畫面</param>
        /// <param name="captureTime">畫面擷取時間戳（用於精確時間同步）</param>
        MinimapTrackingResult? GetMinimapTracking(Mat fullFrameMat, DateTime captureTime);

        /// <summary>取得最後一幀小地圖的 Mat 複製（呼叫者需自行 Dispose）</summary>
        Mat? GetLastMinimapMatClone();
    }

    // ─── 怪物偵測介面 ─────────────────────────────────────────────

    /// <summary>
    /// 怪物偵測器介面 — 負責模板匹配、怪物尋找及後處理（NMS）
    /// </summary>
    public interface IMonsterDetector : IVisionDetector
    {
        /// <summary>
        /// 在畫面區域中使用模板匹配尋找怪物
        /// 對應 GameVisionCore.FindMonsters
        /// </summary>
        /// <param name="sourceMat">裁切後的偵測區域</param>
        /// <param name="templateMats">怪物模板列表</param>
        /// <param name="detectionMode">偵測模式（Color / Grayscale 等）</param>
        /// <param name="threshold">匹配閾值（0.0-1.0）</param>
        /// <param name="monsterName">怪物名稱（用於結果標記）</param>
        /// <returns>偵測結果列表（未去重）</returns>
        List<DetectionResult> FindMonsters(
            Mat sourceMat,
            List<Mat> templateMats,
            MonsterDetectionMode detectionMode,
            double threshold,
            string monsterName);

        /// <summary>
        /// 非最大值抑制（NMS）— 移除重疊的偵測框
        /// 對應 GameVisionCore.ApplyNMS（靜態方法提升為介面方法）
        /// </summary>
        /// <param name="results">原始偵測結果</param>
        /// <param name="iouThreshold">IoU 重疊容錯閾值</param>
        /// <param name="higherIsBetter">信心度越高越好為 true</param>
        List<DetectionResult> ApplyNMS(
            List<DetectionResult> results,
            double iouThreshold,
            bool higherIsBetter);

        /// <summary>
        /// 非同步載入怪物模板（從資料夾），並計算推薦閾值
        /// 對應 GameVisionCore.LoadMonsterTemplatesAsync
        /// </summary>
        /// <param name="folderPath">怪物圖檔資料夾</param>
        /// <param name="monsterName">怪物名稱（用於快取識別）</param>
        /// <returns>載入的模板 Mat 列表（由呼叫者管理生命週期）</returns>
        Task<List<Mat>> LoadMonsterTemplatesAsync(string folderPath, string monsterName);
    }
}
