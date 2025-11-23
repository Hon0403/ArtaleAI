using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using Windows.Graphics.Capture;
using OpenCvSharp;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;
using SdRectangle = System.Drawing.Rectangle;
using SdBitmap = System.Drawing.Bitmap;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;

namespace ArtaleAI.Config
{
    #region 核心資料模型

    /// <summary>
    /// 統一檢測結果記錄
    /// 用於儲存怪物或其他物件的檢測資訊
    /// </summary>
    /// <param name="Name">檢測目標名稱</param>
    /// <param name="Position">檢測位置</param>
    /// <param name="Size">檢測大小</param>
    /// <param name="Confidence">檢測信心度（0.0-1.0）</param>
    /// <param name="BoundingBox">邊界框</param>
    public record DetectionResult(
        string Name,
        SdPoint Position,
        SdSize Size,
        double Confidence,
        Rectangle BoundingBox
    );

    /// <summary>
    /// 小地圖檢測結果記錄
    /// 包含小地圖圖像、玩家位置和螢幕位置資訊
    /// </summary>
    /// <param name="MinimapImage">小地圖圖像</param>
    /// <param name="PlayerPosition">玩家在小地圖上的位置（相對座標）</param>
    /// <param name="CaptureItem">擷取項目</param>
    /// <param name="MinimapScreenRect">小地圖在螢幕上的矩形區域</param>
    public record MinimapResult(
        Bitmap MinimapImage,
        SdPoint? PlayerPosition,
        GraphicsCaptureItem CaptureItem,
        Rectangle? MinimapScreenRect
    );

    /// <summary>
    /// 路徑規劃狀態
    /// 追蹤角色在規劃路徑上的當前進度和狀態
    /// </summary>
    public class PathPlanningState
    {
        /// <summary>規劃的路徑點列表</summary>
        public List<SdPoint> PlannedPath { get; set; } = new();
        
        /// <summary>當前路徑點索引（從0開始）</summary>
        public int CurrentWaypointIndex { get; set; }
        
        /// <summary>路徑是否已完成</summary>
        public bool IsPathCompleted { get; set; }
        
        /// <summary>到下一個路徑點的距離（像素）</summary>
        public double DistanceToNextWaypoint { get; set; }
        
        /// <summary>玩家當前位置</summary>
        public SdPoint? CurrentPlayerPosition { get; set; }
        
        /// <summary>最後更新時間</summary>
        public DateTime LastUpdateTime { get; set; }

        /// <summary>
        /// 取得下一個要前往的路徑點
        /// </summary>
        public SdPoint? NextWaypoint
        {
            get
            {
                if (PlannedPath == null || CurrentWaypointIndex >= PlannedPath.Count)
                    return null;
                return PlannedPath[CurrentWaypointIndex];
            }
        }
    }

    /// <summary>
    /// 小地圖追蹤結果記錄
    /// 包含玩家位置、其他玩家位置和小地圖邊界資訊
    /// </summary>
    /// <param name="PlayerPosition">玩家在小地圖上的位置（相對座標）</param>
    /// <param name="OtherPlayers">其他玩家的位置列表</param>
    /// <param name="Timestamp">追蹤時間戳記</param>
    /// <param name="Confidence">追蹤信心度</param>
    public record MinimapTrackingResult(
        SdPoint? PlayerPosition,
        List<SdPoint> OtherPlayers,
        DateTime Timestamp,
        double Confidence
    )
    {
        /// <summary>小地圖在螢幕上的邊界區域</summary>
        public Rectangle? MinimapBounds { get; init; }
    }

    /// <summary>
    /// 地圖資料
    /// 儲存地圖上的所有路徑點、區域標記等資訊
    /// </summary>
    public class MapData
    {
        /// <summary>路徑點列表（用於路徑規劃）</summary>
        public List<float[]> WaypointPaths { get; set; } = new();
        
        /// <summary>安全區域列表</summary>
        public List<float[]> SafeZones { get; set; } = new();
        
        /// <summary>繩索位置列表</summary>
        public List<float[]> Ropes { get; set; } = new();
        
        /// <summary>限制區域列表（禁止進入的區域）</summary>
        public List<float[]> RestrictedZones { get; set; } = new();
    }

    /// <summary>
    /// 檢測框資料
    /// 儲存檢測結果的視覺化資訊
    /// </summary>
    public class DetectionBox
    {
        /// <summary>檢測框矩形區域</summary>
        public Rectangle Rectangle { get; set; }
        
        /// <summary>檢測標籤文字</summary>
        public string Label { get; set; } = string.Empty;
        
        /// <summary>檢測信心度</summary>
        public double Confidence { get; set; }
        
        /// <summary>檢測框顏色</summary>
        public Color Color { get; set; }
    }

    /// <summary>
    /// 怪物檢測框樣式設定
    /// 定義怪物檢測結果的視覺化顯示樣式
    /// </summary>
    public class MonsterStyle
    {
        /// <summary>邊框顏色（格式：R,G,B）</summary>
        public string FrameColor { get; set; }
        
        /// <summary>文字顏色（格式：R,G,B）</summary>
        public string TextColor { get; set; }
        
        /// <summary>邊框粗細（像素）</summary>
        public int FrameThickness { get; set; }
        
        /// <summary>文字縮放比例</summary>
        public double TextScale { get; set; } 
        
        /// <summary>文字粗細</summary>
        public int TextThickness { get; set; } 
        
        /// <summary>是否顯示信心度</summary>
        public bool ShowConfidence { get; set; } 
        
        /// <summary>文字格式化字串</summary>
        public string TextFormat { get; set; } 
    }

    /// <summary>
    /// 血條樣式設定
    /// 定義隊友血條檢測的視覺化顯示樣式
    /// </summary>
    public class PartyRedBarStyle
    {
        /// <summary>邊框顏色（格式：R,G,B）</summary>
        public string FrameColor { get; set; } 
        
        /// <summary>文字顏色（格式：R,G,B）</summary>
        public string TextColor { get; set; }
        
        /// <summary>邊框粗細（像素）</summary>
        public int FrameThickness { get; set; } 
        
        /// <summary>文字縮放比例</summary>
        public double TextScale { get; set; }
        
        /// <summary>文字粗細</summary>
        public int TextThickness { get; set; } 
        
        /// <summary>血條顯示名稱</summary>
        public string RedBarDisplayName { get; set; }
    }

    /// <summary>
    /// 檢測框樣式設定
    /// 定義通用檢測框的視覺化顯示樣式
    /// </summary>
    public class DetectionBoxStyle
    {
        /// <summary>邊框顏色（格式：R,G,B）</summary>
        public string FrameColor { get; set; }
        
        /// <summary>文字顏色（格式：R,G,B）</summary>
        public string TextColor { get; set; }
        
        /// <summary>邊框粗細（像素）</summary>
        public int FrameThickness { get; set; }
        
        /// <summary>文字縮放比例</summary>
        public double TextScale { get; set; }
        
        /// <summary>文字粗細</summary>
        public int TextThickness { get; set; }
        
        /// <summary>檢測框顯示名稱</summary>
        public string BoxDisplayName { get; set; }
    }

    /// <summary>
    /// 攻擊範圍樣式設定
    /// 定義角色攻擊範圍框的大小、位置和顯示樣式
    /// </summary>
    public class AttackRangeStyle
    {
        /// <summary>攻擊範圍寬度（像素）</summary>
        public int Width { get; set; }
        
        /// <summary>攻擊範圍高度（像素）</summary>
        public int Height { get; set; }
        
        /// <summary>水平偏移量（像素）</summary>
        public int OffsetX { get; set; }
        
        /// <summary>垂直偏移量（像素）</summary>
        public int OffsetY { get; set; }
        
        /// <summary>邊框顏色（格式：R,G,B）</summary>
        public string FrameColor { get; set; }
        
        /// <summary>文字顏色（格式：R,G,B）</summary>
        public string TextColor { get; set; } 
        
        /// <summary>邊框粗細（像素）</summary>
        public int FrameThickness { get; set; }
        
        /// <summary>文字縮放比例</summary>
        public double TextScale { get; set; }
        
        /// <summary>文字粗細</summary>
        public int TextThickness { get; set; }
        
        /// <summary>攻擊範圍顯示名稱</summary>
        public string RangeDisplayName { get; set; }
    }

    /// <summary>
    /// 小地圖樣式設定
    /// 定義小地圖邊界框的顯示樣式
    /// </summary>
    public class MinimapStyle
    {
        /// <summary>邊框顏色（格式：R,G,B）</summary>
        public string FrameColor { get; set; } 
        
        /// <summary>文字顏色（格式：R,G,B）</summary>
        public string TextColor { get; set; }
        
        /// <summary>邊框粗細（像素）</summary>
        public int FrameThickness { get; set; } 
        
        /// <summary>文字縮放比例</summary>
        public double TextScale { get; set; } 
        
        /// <summary>文字粗細</summary>
        public int TextThickness { get; set; }
        
        /// <summary>小地圖顯示名稱</summary>
        public string MinimapDisplayName { get; set; } 
    }

    /// <summary>
    /// 小地圖玩家標記樣式設定
    /// 定義小地圖上玩家標記的顯示樣式
    /// </summary>
    public class MinimapPlayerStyle
    {
        /// <summary>標記顏色（格式：R,G,B）</summary>
        public string FrameColor { get; set; } 
        
        /// <summary>文字顏色（格式：R,G,B）</summary>
        public string TextColor { get; set; } 
        
        /// <summary>標記粗細（像素）</summary>
        public int FrameThickness { get; set; } 
        
        /// <summary>文字縮放比例</summary>
        public double TextScale { get; set; }
        
        /// <summary>文字粗細</summary>
        public int TextThickness { get; set; } 
        
        /// <summary>玩家標記顯示名稱</summary>
        public string PlayerDisplayName { get; set; }
    }
    #endregion

    #region JSON 序列化輔助方法

    /// <summary>
    /// 提供資料模型的 JSON 序列化輔助方法
    /// 統一管理 JSON 序列化選項
    /// </summary>
    public static class DataModelHelper
    {
        /// <summary>
        /// 將物件序列化為 JSON 字串
        /// 使用縮排格式，方便閱讀
        /// </summary>
        /// <typeparam name="T">物件類型</typeparam>
        /// <param name="obj">要序列化的物件</param>
        /// <returns>JSON 字串</returns>
        public static string ToJson<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        /// <summary>
        /// 將 JSON 字串反序列化為物件
        /// </summary>
        /// <typeparam name="T">目標物件類型</typeparam>
        /// <param name="json">JSON 字串</param>
        /// <returns>反序列化後的物件（失敗時返回 null）</returns>
        public static T? FromJson<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json);
        }
    }

    #endregion
}