using System;
using System.Drawing;

namespace ArtaleAI.Models
{
    #region 地圖物件介面

    /// <summary>
    /// 所有地圖標記物件的基礎介面
    /// </summary>
    public interface IMapObject
    {
        /// <summary>
        /// 物件的唯一識別碼
        /// </summary>
        public Guid Id { get; }
    }

    #endregion

    #region 渲染介面

    /// <summary>
    /// 渲染項目的基礎介面
    /// </summary>
    public interface IRenderItem
    {
        /// <summary>
        /// 邊界框
        /// </summary>
        Rectangle BoundingBox { get; set; }

        /// <summary>
        /// 顯示文字
        /// </summary>
        string DisplayText { get; set; }

        /// <summary>
        /// 框架顏色
        /// </summary>
        Color FrameColor { get; set; }

        /// <summary>
        /// 文字顏色
        /// </summary>
        Color TextColor { get; set; }

        /// <summary>
        /// 框架粗細
        /// </summary>
        int FrameThickness { get; set; }

        /// <summary>
        /// 文字縮放
        /// </summary>
        double TextScale { get; set; }

        /// <summary>
        /// 文字粗細
        /// </summary>
        int TextThickness { get; set; }
    }

    #endregion
}