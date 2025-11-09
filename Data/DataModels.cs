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
    /// 統一檢測結果
    /// </summary>
    public record DetectionResult(
        string Name,
        SdPoint Position,
        SdSize Size,
        double Confidence,
        Rectangle BoundingBox
    );

    /// <summary>
    /// 統一小地圖結果
    /// </summary>
    public record MinimapResult(
        Bitmap MinimapImage,
        SdPoint? PlayerPosition,
        GraphicsCaptureItem CaptureItem,
        Rectangle? MinimapScreenRect
    );

    /// <summary>
    /// 路徑規劃狀態
    /// </summary>
    public class PathPlanningState
    {
        public List<SdPoint> PlannedPath { get; set; } = new();
        public int CurrentWaypointIndex { get; set; }
        public bool IsPathCompleted { get; set; }
        public double DistanceToNextWaypoint { get; set; }
        public SdPoint? CurrentPlayerPosition { get; set; }
        public DateTime LastUpdateTime { get; set; }

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
    /// 小地圖追蹤結果
    /// </summary>
    public record MinimapTrackingResult(
        SdPoint? PlayerPosition,
        List<SdPoint> OtherPlayers,
        DateTime Timestamp,
        double Confidence
    )
    {
        public Rectangle? MinimapBounds { get; init; }
    }

    /// <summary>
    /// 地圖資料
    /// </summary>
    public class MapData
    {
        public List<int[]> WaypointPaths { get; set; }
        public List<int[]> SafeZones { get; set; }
        public List<int[]> Ropes { get; set; }
        public List<int[]> RestrictedZones { get; set; }
    }

    /// <summary>
    /// 檢測框資料
    /// </summary>
    public class DetectionBox
    {
        public Rectangle Rectangle { get; set; }
        public string Label { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public Color Color { get; set; }
    }

    /// <summary>
    /// 怪物樣式設定
    /// </summary>
    public class MonsterStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; } 
        public int TextThickness { get; set; } 
        public bool ShowConfidence { get; set; } 
        public string TextFormat { get; set; } 
    }

    /// <summary>
    /// 血條樣式設定
    /// </summary>
    public class PartyRedBarStyle
    {
        public string FrameColor { get; set; } 
        public string TextColor { get; set; }
        public int FrameThickness { get; set; } 
        public double TextScale { get; set; }
        public int TextThickness { get; set; } 
        public string RedBarDisplayName { get; set; }
    }

    /// <summary>
    /// 檢測框樣式設定
    /// </summary>
    public class DetectionBoxStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string BoxDisplayName { get; set; }
    }

    /// <summary>
    /// 攻擊範圍樣式設定
    /// </summary>
    public class AttackRangeStyle
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public string FrameColor { get; set; }
        public string TextColor { get; set; } 
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string RangeDisplayName { get; set; }
    }

    /// <summary>
    /// 小地圖樣式設定
    /// </summary>
    public class MinimapStyle
    {
        public string FrameColor { get; set; } 
        public string TextColor { get; set; }
        public int FrameThickness { get; set; } 
        public double TextScale { get; set; } 
        public int TextThickness { get; set; }
        public string MinimapDisplayName { get; set; } 
    }

    /// <summary>
    /// 小地圖玩家標記樣式設定
    /// </summary>
    public class MinimapPlayerStyle
    {
        public string FrameColor { get; set; } 
        public string TextColor { get; set; } 
        public int FrameThickness { get; set; } 
        public double TextScale { get; set; }
        public int TextThickness { get; set; } 
        public string PlayerDisplayName { get; set; }
    }
    #endregion

    #region JSON 序列化輔助方法

    /// <summary>
    /// 提供資料模型的JSON序列化輔助方法
    /// </summary>
    public static class DataModelHelper
    {
        public static string ToJson<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public static T? FromJson<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json);
        }
    }

    #endregion
}