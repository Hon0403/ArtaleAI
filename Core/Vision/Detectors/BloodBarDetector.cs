using ArtaleAI.Models.Config;
using ArtaleAI.Core.Vision;
using ArtaleAI.Utils;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ArtaleAI.Core.Vision.Detectors
{
    /// <summary>HSV 紅色血條輪廓與衍生偵測／攻擊框；內部中間 Mat 皆 <c>using</c>，不 Dispose 呼叫者傳入的畫面。</summary>
    public sealed class BloodBarDetector : IBloodBarDetector
    {
        private readonly AppConfig _config;
        private bool _disposed = false;

        /// <inheritdoc/>
        public string Name => "BloodBarDetector";

        public BloodBarDetector(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <inheritdoc/>
        public Rectangle? DetectBloodBar(
            Mat frameMat,
            Rectangle? uiExcludeRect,
            out float cameraOffsetY)
        {
            cameraOffsetY = 0f;

            if (frameMat == null || frameMat.Empty()) return null;

            Mat cameraArea;
            if (uiExcludeRect.HasValue)
            {
                var cameraHeight = uiExcludeRect.Value.Y;
                cameraArea = frameMat[new OpenCvSharp.Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
            else
            {
                var totalHeight = frameMat.Height;
                var uiHeight = _config.Vision.UiHeightFromBottom;
                var cameraHeight = Math.Max(totalHeight - uiHeight, totalHeight / 2);
                cameraArea = frameMat[new OpenCvSharp.Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }

            using (cameraArea)
            {
                using var hsvImage = new Mat();
                Cv2.CvtColor(cameraArea, hsvImage, ColorConversionCodes.BGR2HSV);

                using var redMask = new Mat();
                var lowerRed = new Scalar(_config.Vision.LowerRedHsv[0], _config.Vision.LowerRedHsv[1], _config.Vision.LowerRedHsv[2]);
                var upperRed = new Scalar(_config.Vision.UpperRedHsv[0], _config.Vision.UpperRedHsv[1], _config.Vision.UpperRedHsv[2]);
                Cv2.InRange(hsvImage, lowerRed, upperRed, redMask);

                var bestBar = FindBestRedBar(redMask);
                return bestBar.HasValue
                    ? new Rectangle(bestBar.Value.X, bestBar.Value.Y + (int)cameraOffsetY,
                                   bestBar.Value.Width, bestBar.Value.Height)
                    : null;
            }
        }

        /// <inheritdoc/>
        public (Rectangle? BloodBar,
                List<Rectangle> DetectionBoxes,
                List<Rectangle> AttackRangeBoxes)
            ProcessBloodBarDetection(Mat frameMat, Rectangle? uiExcludeRect)
        {
            try
            {
                var bloodBar = DetectBloodBar(frameMat, uiExcludeRect, out _);

                if (bloodBar.HasValue)
                {
                    var (detectionBoxes, attackRangeBoxes) = CalculateBloodBarRelatedBoxes(bloodBar.Value);
                    return (bloodBar, detectionBoxes, attackRangeBoxes);
                }

                return (null, new List<Rectangle>(), new List<Rectangle>());
            }
            catch (Exception ex)
            {
                Logger.Error($"[BloodBarDetector] ProcessBloodBarDetection 錯誤: {ex.Message}");
                return (null, new List<Rectangle>(), new List<Rectangle>());
            }
        }

        private (List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            CalculateBloodBarRelatedBoxes(Rectangle bloodBarRect)
        {
            var dotCenterX = bloodBarRect.X + bloodBarRect.Width / 2;
            var dotCenterY = bloodBarRect.Y + bloodBarRect.Height + _config.Vision.DotOffsetY;

            var detectionBox = new Rectangle(
                dotCenterX - _config.Vision.DetectionBoxWidth / 2,
                dotCenterY - _config.Vision.DetectionBoxHeight / 2,
                _config.Vision.DetectionBoxWidth,
                _config.Vision.DetectionBoxHeight);

            var playerCenterX = bloodBarRect.X + bloodBarRect.Width / 2 + _config.Appearance.AttackRange.OffsetX;
            var playerCenterY = bloodBarRect.Y + bloodBarRect.Height + _config.Appearance.AttackRange.OffsetY;

            var attackRangeBox = new Rectangle(
                playerCenterX - _config.Appearance.AttackRange.Width / 2,
                playerCenterY - _config.Appearance.AttackRange.Height / 2,
                _config.Appearance.AttackRange.Width,
                _config.Appearance.AttackRange.Height);

            return (new List<Rectangle> { detectionBox }, new List<Rectangle> { attackRangeBox });
        }

        private Rectangle? FindBestRedBar(Mat redMask)
        {
            if (redMask == null || redMask.Empty()) return null;

            Mat? hierarchy = null;
            Mat[]? contours = null;

            try
            {
                hierarchy = new Mat();
                Cv2.FindContours(redMask, out contours, hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                var candidates = new List<(Rectangle rect, int area)>();

                for (int i = 0; i < contours.Length; i++)
                {
                    var contour = contours[i];
                    if (contour?.Empty() != false) continue;

                    try
                    {
                        var br = Cv2.BoundingRect(contour);
                        var rect = new Rectangle(br.X, br.Y, br.Width, br.Height);
                        var area = rect.Width * rect.Height;

                        if (rect.Width  >= _config.Vision.MinBarWidth  &&
                            rect.Width  <= _config.Vision.MaxBarWidth  &&
                            rect.Height >= _config.Vision.MinBarHeight &&
                            rect.Height <= _config.Vision.MaxBarHeight &&
                            area        >= _config.Vision.MinBarArea)
                        {
                            candidates.Add((rect, area));
                        }
                    }
                    finally
                    {
                        contour?.Dispose();
                    }
                }

                if (candidates.Count == 0) return null;

                return candidates.OrderByDescending(c => c.area).First().rect;
            }
            finally
            {
                hierarchy?.Dispose();
                if (contours != null)
                {
                    foreach (var c in contours)
                    {
                        if (c != null && !c.IsDisposed)
                            c.Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
