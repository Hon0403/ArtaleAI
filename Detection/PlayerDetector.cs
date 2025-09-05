using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ArtaleAI.Config;
using ArtaleAI.Utils;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 玩家位置偵測器 - 通過隊友紅色血條定位玩家位置
    /// </summary>
    public class PlayerDetector
    {
        private readonly AppConfig _config;
        private readonly PartyRedBarSettings _settings;
        private readonly PlayerDetectionSettings? _playerSettings;
        private bool _isProcessing = false;
        private readonly object _processingLock = new();

        public event Action<List<Rectangle>>? BloodBarDetected;
        public event Action<string>? StatusMessage;

        public PlayerDetector(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _settings = config.PartyRedBar ?? new PartyRedBarSettings();
            _playerSettings = config.PlayerDetection;

        }

        ///
        /// 通過隊友紅色血條獲取玩家位置 - 修復版本
        ///
        public (System.Drawing.Point? playerLocation, System.Drawing.Point? redBarLocation, Rectangle? redBarRect)
        GetPlayerLocationByPartyRedBar(Bitmap frameBitmap, Rectangle? minimapRect = null, Rectangle? uiExcludeRect = null)
        {
            if (frameBitmap == null) return (null, null, null);

            try
            {
                using var frameMat = ImageUtils.BitmapToThreeChannelMat(frameBitmap);

                // 1. 清零小地圖區域避免干擾
                if (minimapRect.HasValue)
                {
                    var minimapRegion = new Rect(minimapRect.Value.X, minimapRect.Value.Y,
                        minimapRect.Value.Width, minimapRect.Value.Height);
                    frameMat[minimapRegion].SetTo(new Scalar(0, 0, 0));
                }

                // 2. 提取相機區域（排除UI）
                using var cameraArea = ExtractCameraArea(frameMat, uiExcludeRect);
                if (cameraArea.Empty()) return (null, null, null);

                using var hsvImage = ImageUtils.ConvertToHSV(cameraArea);
                var lowerRed = ToOpenCvHsv((_settings.LowerRedHsv[0], _settings.LowerRedHsv[1], _settings.LowerRedHsv[2]));
                var upperRed = ToOpenCvHsv((_settings.UpperRedHsv[0], _settings.UpperRedHsv[1], _settings.UpperRedHsv[2]));

                using var redMask = new Mat();
                Cv2.InRange(hsvImage, lowerRed, upperRed, redMask);

                var redBarResult = FindPartyRedBarWithSize(redMask);
                if (!redBarResult.HasValue) return (null, null, null);

                var (redBarLocation, redBarRect) = redBarResult.Value;
                var playerLocation = new System.Drawing.Point(
                    redBarLocation.X + _settings.PlayerOffsetX,
                    redBarLocation.Y + _settings.PlayerOffsetY);

                return (playerLocation, redBarLocation, redBarRect);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 血條定位失敗: {ex.Message}");
                return (null, null, null);
            }
        }

        /// <summary>
        /// 使用設定檔的動態填充率參數
        /// </summary>
        private (System.Drawing.Point location, Rectangle rect)? FindPartyRedBarWithSize(Mat redMask)
        {
            var contours = new Mat[0];
            var hierarchy = new Mat();
            Cv2.FindContours(redMask, out contours, hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var candidates = new List<(System.Drawing.Point location, Rectangle rect, int area)>();

            try
            {
                foreach (var contour in contours)
                {
                    var boundingRect = Cv2.BoundingRect(contour);
                    var area = (int)Cv2.ContourArea(contour);
                    var fillRate = (double)area / (boundingRect.Width * boundingRect.Height);

                    int smallWidthLimit = _playerSettings.SmallBarWidthLimit;
                    int mediumWidthLimit = _playerSettings.MediumBarWidthLimit;

                    double minFillRateThreshold;
                    if (boundingRect.Width <= smallWidthLimit)
                        minFillRateThreshold = _settings.DynamicFillRateSmall;
                    else if (boundingRect.Width <= mediumWidthLimit)
                        minFillRateThreshold = _settings.DynamicFillRateMedium;
                    else
                        minFillRateThreshold = _settings.MinFillRate;

                    if (boundingRect.Height >= _settings.MinBarHeight &&
                        boundingRect.Height <= _settings.MaxBarHeight &&
                        boundingRect.Width >= _settings.MinBarWidth &&
                        boundingRect.Width <= _settings.MaxBarWidth &&
                        area >= _settings.MinBarArea &&
                        fillRate >= minFillRateThreshold)
                    {
                        var realRect = new Rectangle(
                            boundingRect.X, boundingRect.Y,
                            boundingRect.Width, boundingRect.Height);

                        candidates.Add((
                            new System.Drawing.Point(boundingRect.X, boundingRect.Y),
                            realRect,
                            area));
                    }
                }

                if (candidates.Any())
                {
                    var bestCandidate = candidates.OrderByDescending(c => c.area).First();
                    return (bestCandidate.location, bestCandidate.rect);
                }
            }
            finally
            {
                ImageUtils.SafeDispose(contours);
                hierarchy?.Dispose();
            }

            return null;
        }

        /// <summary>
        /// 提取相機區域（排除UI）
        /// </summary>
        private Mat ExtractCameraArea(Mat frameMat, Rectangle? uiExcludeRect)
        {
            if (uiExcludeRect.HasValue)
            {
                var cameraHeight = uiExcludeRect.Value.Y;
                return frameMat[new Rect(0, 0, frameMat.Width, cameraHeight)].Clone(); // 明確 Clone
            }
            else
            {
                var cameraHeight = Math.Max(frameMat.Height - _settings.UiHeightFromBottom, frameMat.Height / 2);
                return frameMat[new Rect(0, 0, frameMat.Width, cameraHeight)].Clone(); // 明確 Clone
            }
        }

        /// <summary>
        /// 非同步處理幀 - 核心方法
        /// </summary>
        public async Task ProcessFrameAsync(Bitmap frame, Rectangle? minimapRect = null)
        {
            lock (_processingLock)
            {
                if (_isProcessing) return;
                _isProcessing = true;
            }

            try
            {
                var result = await Task.Run(() =>
                    GetPlayerLocationByPartyRedBar(frame, minimapRect));

                if (result.redBarRect.HasValue)
                {
                    var redBarRects = new List<Rectangle> { result.redBarRect.Value };
                    BloodBarDetected?.Invoke(redBarRects);
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"❌ 血條識別失敗: {ex.Message}");
            }
            finally
            {
                lock (_processingLock)
                {
                    _isProcessing = false;
                }
            }
        }

        /// <summary>
        /// 轉換HSV值為OpenCV格式
        /// </summary>
        private Scalar ToOpenCvHsv((int h, int s, int v) hsv)
        {
            return new Scalar(hsv.h, hsv.s, hsv.v);
        }
    }
}
