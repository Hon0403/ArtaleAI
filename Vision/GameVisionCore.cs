using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Shared;
using ArtaleAI.Infrastructure.Capture;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace ArtaleAI.Vision
{
    /// <summary>整合的遊戲視覺核心。</summary>
    public partial class GameVisionCore : IDisposable
    {
        #region 私有字段
        private readonly Dictionary<string, Mat> _mapTemplates = new();
        private readonly Dictionary<string, MonsterTemplateBundle> _monsterBundleCache = new();
        private bool _disposed = false;
        private int _lastPixelCount = 0; 
        private readonly object _minimapMatLock = new object();

        private Rectangle? _cachedMinimapRect = null;
        private DateTime _lastFullScanTime = DateTime.MinValue;
        private const int FullScanIntervalMs = 2000;
        #endregion

        #region 公開屬性

        /// <summary>
        /// 最後一次追蹤的小地圖 Mat（供 MinimapViewer 使用）
        /// 線程安全，每次 GetMinimapTracking 時更新
        /// 注意：直接存取此屬性可能導致線程問題，請使用 GetLastMinimapMatClone()
        /// </summary>
        public Mat? LastMinimapMat { get; private set; }

        /// <summary>
        /// 取得 LastMinimapMat 的安全複製（線程安全）
        /// 呼叫者負責 Dispose 返回的 Mat
        /// </summary>
        /// <returns>小地圖 Mat 的複製，如果不存在則返回 null</returns>
        public Mat? GetLastMinimapMatClone()
        {
            lock (_minimapMatLock)
            {
                if (LastMinimapMat == null || LastMinimapMat.IsDisposed || LastMinimapMat.Empty())
                    return null;

                try
                {
                    return LastMinimapMat.Clone();
                }
                catch
                {
                    return null;
                }
            }
        }

        #endregion

        #region 建構函式

        /// <summary>
        /// 初始化遊戲視覺核心
        /// 自動載入小地圖角點模板
        /// </summary>
        public GameVisionCore()
        {
            LoadMapTemplates();
        }
        #endregion
        /// <summary>
        /// 將 RGB 圖像轉換為灰階
        /// </summary>
        /// <param name="rgbMat">RGB 格式的 Mat</param>
        /// <returns>灰階 Mat</returns>
        /// <exception cref="ArgumentException">輸入 Mat 為 null 或空時拋出</exception>
        public static Mat ConvertToGrayscale(Mat rgbMat)
        {
            if (rgbMat == null || rgbMat.Empty())
                throw new ArgumentException("Mat不能為null或空", nameof(rgbMat));

            var grayMat = new Mat();
            Cv2.CvtColor(rgbMat, grayMat, ColorConversionCodes.RGB2GRAY);
            return grayMat;
        }

        /// <summary>
        /// 將 HSV 值轉換為 OpenCV Scalar 格式
        /// </summary>
        /// <param name="h">色相 (Hue) 0-179</param>
        /// <param name="s">飽和度 (Saturation) 0-255</param>
        /// <param name="v">明度 (Value) 0-255</param>
        /// <returns>OpenCV Scalar 物件</returns>
        public static Scalar ToOpenCvHsv(int h, int s, int v)
        {
            return new Scalar(h, s, v);
        }

        /// <summary>
        /// 執行模板匹配
        /// 支援彩色和灰階兩種模式
        /// </summary>
        /// <param name="inputMat">輸入影像</param>
        /// <param name="templateMat">模板影像</param>
        /// <param name="threshold">匹配閾值（0.0-1.0）</param>
        /// <param name="useGrayscale">是否使用灰階模式</param>
        /// <returns>匹配位置和最大值，未達閾值時返回 null</returns>
        public static (System.Drawing.Point Location, double MaxValue)? MatchTemplate(Mat inputMat, Mat templateMat, double threshold, bool useGrayscale = false)
        {
            if (inputMat?.Empty() != false || templateMat?.Empty() != false)
                return null;

            try
            {
                using var result = new Mat();
                if (useGrayscale)
                {
                    using var inputGray = ConvertToGrayscale(inputMat);
                    using var templateGray = ConvertToGrayscale(templateMat);
                    OpenCvSharp.Cv2.MatchTemplate(inputGray, templateGray, result, OpenCvSharp.TemplateMatchModes.CCoeffNormed);
                }
                else
                {
                    OpenCvSharp.Cv2.MatchTemplate(inputMat, templateMat, result, OpenCvSharp.TemplateMatchModes.CCoeffNormed);
                }
                OpenCvSharp.Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLoc);
                if (maxValue >= threshold) return (new System.Drawing.Point(maxLoc.X, maxLoc.Y), maxValue);
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析顏色字串為 Color 物件
        /// 支援 "R,G,B" 格式（例如："255,0,0"）
        /// </summary>
        /// <param name="colorString">顏色字串</param>
        /// <returns>Color 物件，解析失敗時返回白色</returns>
        public static Color ParseColor(string? colorString)
        {
            if (string.IsNullOrWhiteSpace(colorString))
                return Color.White;

            try
            {
                var parts = colorString.Split(',');
                if (parts.Length == 3)
                {
                    var r = int.Parse(parts[0].Trim());
                    var g = int.Parse(parts[1].Trim());
                    var b = int.Parse(parts[2].Trim());

                    if (r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
                        return Color.FromArgb(r, g, b);
                }
            }
            catch
            {
            }

            return Color.White;
        }

        /// <summary>
        /// 將小地圖相對座標轉換為螢幕絕對座標
        /// </summary>
        /// <param name="minimapPoint">小地圖上的相對座標</param>
        /// <param name="minimapBounds">小地圖在螢幕上的邊界區域</param>
        /// <returns>螢幕絕對座標</returns>
        public static PointF MinimapToScreenF(PointF minimapPoint, Rectangle minimapBounds)
        {
            return new PointF(
                minimapBounds.X + minimapPoint.X,
                minimapBounds.Y + minimapPoint.Y
            );
        }

        /// <summary>
        /// 將螢幕絕對座標轉換為小地圖相對座標
        /// </summary>
        /// <param name="screenPoint">螢幕絕對座標</param>
        /// <param name="minimapBounds">小地圖在螢幕上的邊界區域</param>
        /// <returns>小地圖相對座標</returns>
        public static PointF ScreenToMinimapF(PointF screenPoint, Rectangle minimapBounds)
        {
            return new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y
            );
        }

        /// <summary>
        /// 應用非最大值抑制（NMS）去除重疊的檢測結果
        /// 使用 IoU（Intersection over Union）計算重疊度
        /// </summary>
        /// <param name="results">檢測結果列表</param>
        /// <param name="iouThreshold">IoU 閾值，超過此值的重疊框會被抑制</param>
        /// <param name="higherIsBetter">信心度越高越好（true）或越低越好（false）</param>
        /// <returns>經過 NMS 處理後的檢測結果列表</returns>
        public static List<DetectionResult> ApplyNMS(List<DetectionResult> results, double iouThreshold = 0.3, bool higherIsBetter = true)
        {
            if (results == null || results.Count <= 1)
                return results?.ToList() ?? new List<DetectionResult>();

            var sorted = higherIsBetter
                ? results.OrderByDescending(r => r.Confidence).ToList()
                : results.OrderBy(r => r.Confidence).ToList();

            var kept = new List<DetectionResult>();
            var suppressed = new bool[sorted.Count];

            for (int i = 0; i < sorted.Count; i++)
            {
                if (suppressed[i]) continue;

                kept.Add(sorted[i]);

                var rectA = new Rectangle(sorted[i].Position, sorted[i].Size);

                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (suppressed[j]) continue;

                    var rectB = new Rectangle(sorted[j].Position, sorted[j].Size);
                    var iou = CalculateIoU(rectA, rectB);

                    if (iou > iouThreshold)
                        suppressed[j] = true;
                }
            }

            return kept;
        }

        /// <summary>
        /// 計算兩個矩形的 IoU（Intersection over Union）
        /// IoU = 交集面積 / 聯集面積，用於判斷兩個檢測框的重疊程度
        /// </summary>
        /// <param name="rectA">矩形 A</param>
        /// <param name="rectB">矩形 B</param>
        /// <returns>IoU 值（0.0-1.0）</returns>
        private static double CalculateIoU(Rectangle rectA, Rectangle rectB)
        {
            var intersection = Rectangle.Intersect(rectA, rectB);
            if (intersection.IsEmpty)
                return 0.0;

            var intersectionArea = intersection.Width * intersection.Height;
            var unionArea = (rectA.Width * rectA.Height) + (rectB.Width * rectB.Height) - intersectionArea;

            return unionArea > 0 ? (double)intersectionArea / unionArea : 0.0;
        }
        #region IDisposable

        /// <summary>
        /// 釋放遊戲視覺核心使用的所有資源
        /// 包括所有模板 Mat 物件和快取
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var template in _mapTemplates.Values)
                    template?.Dispose();
                _mapTemplates.Clear();

                foreach (var bundle in _monsterBundleCache.Values)
                    bundle.Dispose();
                _monsterBundleCache.Clear();

                lock (_minimapMatLock)
                {
                    LastMinimapMat?.Dispose();
                    LastMinimapMat = null;
                }
                ;

                _disposed = true;
            }
        }
        #endregion
    }
}
