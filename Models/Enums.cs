using System;

namespace ArtaleAI.Models
{
    #region 檢測相關枚舉

    /// <summary>
    /// 怪物檢測模式
    /// </summary>
    public enum MonsterDetectionMode
    {
        /// <summary>
        /// 基礎檢測模式
        /// </summary>
        Basic,

        /// <summary>
        /// 僅輪廓檢測
        /// </summary>
        ContourOnly,

        /// <summary>
        /// 灰階檢測
        /// </summary>
        Grayscale,

        /// <summary>
        /// 色彩檢測
        /// </summary>
        Color,

        /// <summary>
        /// 無模板檢測
        /// </summary>
        TemplateFree
    }

    /// <summary>
    /// 遮擋處理模式
    /// </summary>
    public enum OcclusionHandling
    {
        /// <summary>
        /// 不處理遮擋
        /// </summary>
        None,

        /// <summary>
        /// 形態學修復
        /// </summary>
        MorphologyRepair,

        /// <summary>
        /// 動態閾值
        /// </summary>
        DynamicThreshold,

        /// <summary>
        /// 多尺度處理
        /// </summary>
        MultiScale
    }

    #endregion

    #region 地圖編輯相關枚舉

    /// <summary>
    /// 小地圖的使用情境模式
    /// </summary>
    public enum MinimapUsage
    {
        /// <summary>路徑編輯模式 - 靜態小地圖用於編輯</summary>
        PathEditing,
        /// <summary>即時顯示模式 - 動態疊加層用於即時偵測</summary>
        LiveViewOverlay
    }

    /// <summary>
    /// 定義了所有編輯模式的種類
    /// </summary>
    public enum EditMode
    {
        /// <summary>
        /// 無編輯模式
        /// </summary>
        None,

        /// <summary>
        /// ● 路線標記
        /// </summary>
        Waypoint,

        /// <summary>
        /// 🟩 安全區域
        /// </summary>
        SafeZone,

        /// <summary>
        /// 🟥 禁止區域
        /// </summary>
        RestrictedZone,

        /// <summary>
        /// 🧗 繩索路徑
        /// </summary>
        Rope,

        /// <summary>
        /// ❌ 刪除標記
        /// </summary>
        Delete
    }

    #endregion
}