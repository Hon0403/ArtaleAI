using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ArtaleAI.Config;
using ArtaleAI.Utils;

namespace ArtaleAI.Player
{
    /// <summary>
    /// 玩家位置偵測器 - 通過隊友紅色血條定位玩家位置
    /// </summary>
    public class PlayerDetector : IDisposable
    {
        private readonly AppConfig _config;
        private readonly PartyRedBarSettings _settings;

        public PlayerDetector(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _settings = config.PartyRedBar ?? new PartyRedBarSettings();
        }

        /// <summary>
        /// 通過隊友紅色血條獲取玩家位置 - 四通道版本
        /// </summary>
        public (System.Drawing.Point? playerLocation, System.Drawing.Point? redBarLocation, Rectangle? redBarRect) GetPlayerLocationByPartyRedBar(
            Bitmap frameBitmap,
            Rectangle? minimapRect = null,
            Rectangle? uiExcludeRect = null)
        {
            if (frameBitmap == null) return (null, null, null);

            try
            {
                // 🔧 使用 ImageUtils 轉換為四通道
                using var frameMat = ImageUtils.BitmapToFourChannelMat(frameBitmap);

                // 1. 清零小地圖區域避免干擾
                if (minimapRect.HasValue)
                {
                    var minimapRegion = new Rect(minimapRect.Value.X, minimapRect.Value.Y,
                        minimapRect.Value.Width, minimapRect.Value.Height);
                    frameMat[minimapRegion].SetTo(new Scalar(0, 0, 0, 255));
                }

                // 2. 提取相機區域（排除UI）
                using var cameraArea = ExtractCameraArea(frameMat, uiExcludeRect);
                if (cameraArea.Empty()) return (null, null, null);

                // 3. 轉換為HSV並創建紅色掩碼
                using var bgrImage = new Mat();
                using var hsvImage = new Mat();

                // 🔧 修正：分兩步轉換
                // 第一步：BGRA -> BGR（移除Alpha通道）
                Cv2.CvtColor(cameraArea, bgrImage, ColorConversionCodes.BGRA2BGR);
                // 第二步：BGR -> HSV
                Cv2.CvtColor(bgrImage, hsvImage, ColorConversionCodes.BGR2HSV);

                var lowerRed = ToOpenCvHsv((_settings.LowerRedHsv[0], _settings.LowerRedHsv[1], _settings.LowerRedHsv[2]));
                var upperRed = ToOpenCvHsv((_settings.UpperRedHsv[0], _settings.UpperRedHsv[1], _settings.UpperRedHsv[2]));

                using var redMask = new Mat();
                Cv2.InRange(hsvImage, lowerRed, upperRed, redMask);

                // 4. 尋找符合血條特徵的輪廓
                var redBarLocation = FindPartyRedBar(redMask);
                if (!redBarLocation.HasValue) return (null, null, null);

                // 🔧 創建血條矩形 - 使用配置中的實際尺寸
                var redBarRect = new Rectangle(
                    redBarLocation.Value.X,
                    redBarLocation.Value.Y,
                    _settings.MaxBarWidth,
                    _settings.MaxBarHeight
                );

                // 5. 根據偏移量計算玩家位置
                var playerLocation = new System.Drawing.Point(
                    redBarLocation.Value.X + _settings.PlayerOffsetX,
                    redBarLocation.Value.Y + _settings.PlayerOffsetY
                );

                System.Diagnostics.Debug.WriteLine($"🎯 找到隊友血條: ({redBarLocation.Value.X}, {redBarLocation.Value.Y})");
                System.Diagnostics.Debug.WriteLine($"👤 計算玩家位置: ({playerLocation.X}, {playerLocation.Y})");

                return (playerLocation, redBarLocation, redBarRect); // 🔧 返回血條矩形
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 血條定位失敗: {ex.Message}");
                return (null, null, null);
            }
        }

        /// <summary>
        /// 提取相機區域（排除UI）
        /// </summary>
        private Mat ExtractCameraArea(Mat frameMat, Rectangle? uiExcludeRect)
        {
            if (uiExcludeRect.HasValue)
            {
                // 排除指定的UI區域
                var cameraHeight = uiExcludeRect.Value.Y;
                return frameMat[new Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
            else
            {
                // 使用配置中的預設UI高度
                var cameraHeight = Math.Max(frameMat.Height - _settings.UiHeightFromBottom, frameMat.Height / 2);
                return frameMat[new Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
        }

        /// <summary>
        /// 尋找符合隊友血條特徵的區域
        /// </summary>
        private System.Drawing.Point? FindPartyRedBar(Mat redMask)
        {
            // 尋找輪廓
            var contours = new Mat[0];
            var hierarchy = new Mat();
            Cv2.FindContours(redMask, out contours, hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var candidates = new List<(System.Drawing.Point location, int area)>();

            try
            {
                foreach (var contour in contours)
                {
                    var boundingRect = Cv2.BoundingRect(contour);
                    var area = (int)Cv2.ContourArea(contour);
                    var fillRate = (double)area / (boundingRect.Width * boundingRect.Height);

                    // 🔧 動態調整填充率要求
                    double minFillRateThreshold;
                    if (boundingRect.Width <= 10)  // 很短的血條
                    {
                        minFillRateThreshold = 0.2;  // 降低到20%
                    }
                    else if (boundingRect.Width <= 25)  // 中等長度血條
                    {
                        minFillRateThreshold = 0.4;  // 40%
                    }
                    else
                    {
                        minFillRateThreshold = _settings.MinFillRate;  // 使用原始設定
                    }

                    if (boundingRect.Height >= _settings.MinBarHeight &&
                        boundingRect.Height <= _settings.MaxBarHeight &&
                        boundingRect.Width >= _settings.MinBarWidth &&
                        boundingRect.Width <= _settings.MaxBarWidth &&
                        area >= _settings.MinBarArea &&
                        fillRate >= minFillRateThreshold)  // 使用動態閾值
                    {
                        candidates.Add((new System.Drawing.Point(boundingRect.X, boundingRect.Y), area));
                    }
                }

                // 選擇面積最大的候選者
                if (candidates.Any())
                {
                    var bestCandidate = candidates.OrderByDescending(c => c.area).First();
                    return bestCandidate.location;
                }
            }
            finally
            {
                // 釋放輪廓資源
                foreach (var contour in contours)
                {
                    contour?.Dispose();
                }
                hierarchy?.Dispose();
            }

            return null;
        }

        /// <summary>
        /// 轉換HSV值為OpenCV格式
        /// </summary>
        private Scalar ToOpenCvHsv((int h, int s, int v) hsv)
        {
            return new Scalar(hsv.h, hsv.s, hsv.v);
        }

        public void Dispose()
        {
            // 清理資源
        }
    }
}
