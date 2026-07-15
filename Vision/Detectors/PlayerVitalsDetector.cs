using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Shared;
using OpenCvSharp;
using System;
using System.Drawing;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Vision.Detectors
{
    /// <summary>底部固定 UI 列：百分比 ROI 佈局 + HSV 多行填充率讀取。</summary>
    public sealed class PlayerVitalsDetector : IPlayerVitalsDetector
    {
        private bool _disposed;

        public string Name => "PlayerVitalsDetector";

        public static PlayerVitalsSnapshot ResolveLayout(
            int frameWidth,
            int frameHeight,
            PlayerVitalsSettings settings,
            Mat? frameMat = null)
        {
            if (settings == null || !settings.Enabled || frameWidth <= 0 || frameHeight <= 0)
                return PlayerVitalsSnapshot.Empty;

            var uiBand = ResolveUiBand(frameWidth, frameHeight, settings.UiBandTopPercent);
            bool usesAuto = false;
            SdRect hpRect;
            SdRect mpRect;

            if (settings.UseAutoLayout
                && frameMat != null
                && !frameMat.Empty()
                && PlayerVitalsAutoLayout.TryDetectAnchors(frameMat, settings, out var hpAnchor, out var mpAnchor))
            {
                hpRect = ResolveBarRect(frameWidth, frameHeight, hpAnchor);
                mpRect = ResolveBarRect(frameWidth, frameHeight, mpAnchor);
                usesAuto = true;
            }
            else
            {
                hpRect = ResolveBarRect(frameWidth, frameHeight, settings.HpBar);
                mpRect = ResolveBarRect(frameWidth, frameHeight, settings.MpBar);
            }

            bool layoutValid = IsRectInsideFrame(frameWidth, frameHeight, hpRect)
                               && IsRectInsideFrame(frameWidth, frameHeight, mpRect);

            if (!layoutValid)
            {
                Logger.Warning(
                    $"[PlayerVitalsDetector] ROI 超出畫面 ({frameWidth}x{frameHeight}) " +
                    $"hp={hpRect} mp={mpRect}，請調整 config.yaml playerVitals 或關閉 useAutoLayout");
            }

            return new PlayerVitalsSnapshot
            {
                HpBarRect = hpRect,
                MpBarRect = mpRect,
                UiBandRect = uiBand,
                FrameWidth = frameWidth,
                FrameHeight = frameHeight,
                IsLayoutValid = layoutValid,
                UsesAutoLayout = usesAuto,
                HasFillReading = false,
                Timestamp = DateTime.UtcNow
            };
        }

        public PlayerVitalsSnapshot Detect(Mat frameMat, PlayerVitalsSettings settings)
        {
            if (frameMat == null || frameMat.Empty() || settings == null || !settings.Enabled)
                return PlayerVitalsSnapshot.Empty;

            try
            {
                var layout = ResolveLayout(frameMat.Width, frameMat.Height, settings, frameMat);
                if (!layout.IsLayoutValid)
                    return layout;

                double hpRatio = MeasureBarFillRatio(
                    frameMat, layout.HpBarRect, settings.LowerRedHsv, settings.UpperRedHsv,
                    isRed: true, settings);
                double mpRatio = MeasureBarFillRatio(
                    frameMat, layout.MpBarRect, settings.LowerBlueHsv, settings.UpperBlueHsv,
                    isRed: false, settings);

                return layout with
                {
                    HpRatio = ClampRatio(hpRatio),
                    MpRatio = ClampRatio(mpRatio),
                    HasFillReading = true,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"[PlayerVitalsDetector] Detect 錯誤: {ex.Message}");
                return PlayerVitalsSnapshot.Empty;
            }
        }

        internal static SdRect ResolveUiBand(int frameWidth, int frameHeight, double topPercent)
        {
            int top = PercentToPixels(topPercent, frameHeight, minPixels: 0);
            top = Math.Clamp(top, 0, Math.Max(0, frameHeight - 1));
            int height = frameHeight - top;
            return new SdRect(0, top, frameWidth, height);
        }

        internal static SdRect ResolveBarRect(int frameWidth, int frameHeight, BarRoiPercentAnchor anchor)
        {
            int width = PercentToPixels(anchor.WidthPercent, frameWidth);
            int barHeight = PercentToPixels(anchor.HeightPercent, frameHeight);
            int left = PercentToPixels(anchor.LeftPercent, frameWidth, minPixels: 0);
            int marginBottom = PercentToPixels(anchor.BottomPercent, frameHeight, minPixels: 0);

            left = Math.Clamp(left, 0, Math.Max(0, frameWidth - width));
            int barBottom = frameHeight - Math.Clamp(marginBottom, 0, frameHeight);
            int top = barBottom - barHeight;
            if (top < 0)
            {
                top = 0;
                barHeight = Math.Min(barHeight, frameHeight);
            }

            return new SdRect(left, top, width, barHeight);
        }

        private static int PercentToPixels(double percent, int frameSize, int minPixels = 1)
        {
            if (frameSize <= 0)
                return minPixels;

            double clamped = Math.Clamp(percent, 0, 1);
            return Math.Max(minPixels, (int)Math.Round(frameSize * clamped));
        }

        private static bool IsRectInsideFrame(int frameWidth, int frameHeight, SdRect rect)
        {
            return rect.X >= 0
                   && rect.Y >= 0
                   && rect.Width > 0
                   && rect.Height > 0
                   && rect.Right <= frameWidth
                   && rect.Bottom <= frameHeight;
        }

        private static SdRect ShrinkRect(SdRect rect, int marginPx)
        {
            if (marginPx <= 0 || rect.Width <= 4 || rect.Height <= 4)
                return rect;

            int margin = Math.Min(marginPx, Math.Min(rect.Width, rect.Height) / 3);
            if (margin <= 0)
                return rect;

            int width = rect.Width - margin * 2;
            int height = rect.Height - margin * 2;
            if (width <= 0 || height <= 0)
                return rect;

            return new SdRect(rect.X + margin, rect.Y + margin, width, height);
        }

        private static double MeasureBarFillRatio(
            Mat frameMat,
            SdRect barRect,
            int[] lowerHsv,
            int[] upperHsv,
            bool isRed,
            PlayerVitalsSettings settings)
        {
            var measureRect = ShrinkRect(barRect, settings.RoiInnerMarginPx);
            if (measureRect.Width <= 0 || measureRect.Height <= 0)
                return 0;

            var cvRect = new Rect(measureRect.X, measureRect.Y, measureRect.Width, measureRect.Height);
            using var barBgr = frameMat[cvRect].Clone();
            using var hsv = new Mat();
            Cv2.CvtColor(barBgr, hsv, ColorConversionCodes.BGR2HSV);

            using var hsvMask = BuildColorMask(hsv, lowerHsv, upperHsv, isRed);
            using var bgrMask = BuildBgrMask(barBgr, isRed);
            ExcludeAnnouncementPink(barBgr, hsvMask);
            ExcludeAnnouncementPink(barBgr, bgrMask);

            using var combinedMask = new Mat();
            Cv2.BitwiseOr(hsvMask, bgrMask, combinedMask);

            double ratio = MeasureHorizontalFillRatio(
                combinedMask,
                settings.ColumnFillThreshold,
                settings.SampleRowCount);

            if (ratio * measureRect.Width < settings.MinFilledColumns)
                return 0;

            return ratio;
        }

        /// <summary>由左而右量測連續填充寬度；遇明顯空洞即停，避免空條底色被算進填充。</summary>
        private static double MeasureHorizontalFillRatio(Mat mask, double columnFillThreshold, int sampleRowCount)
        {
            int width = mask.Width;
            int height = mask.Height;
            if (width <= 0 || height <= 0)
                return 0;

            double threshold = Math.Clamp(columnFillThreshold, 0.1, 1.0);
            // 抗鋸齒淡色可短暫延續，但不得用近乎空白的密度把門檻拖到整條。
            double softThreshold = Math.Max(0.2, threshold * 0.55);
            var sampleRows = BuildSampleRows(height, sampleRowCount);
            double filledWidth = 0;
            bool seenFill = false;
            int softRun = 0;
            const int maxSoftColumns = 3;

            for (int x = 0; x < width; x++)
            {
                double density = GetColumnFillDensity(mask, x, sampleRows);
                if (density >= threshold)
                {
                    filledWidth = x + 1;
                    seenFill = true;
                    softRun = 0;
                    continue;
                }

                if (!seenFill)
                    continue;

                if (density >= softThreshold && softRun < maxSoftColumns)
                {
                    filledWidth = x + density;
                    softRun++;
                    continue;
                }

                break;
            }

            return filledWidth / width;
        }

        private static int[] BuildSampleRows(int height, int sampleRowCount)
        {
            if (height <= 0)
                return [];

            if (sampleRowCount <= 0)
            {
                int topSkip = Math.Max(0, (int)Math.Floor(height * 0.32));
                int usableHeight = height - topSkip;
                if (usableHeight <= 0)
                {
                    topSkip = 0;
                    usableHeight = height;
                }

                var barRows = new int[usableHeight];
                for (int i = 0; i < usableHeight; i++)
                    barRows[i] = topSkip + i;
                return barRows;
            }

            if (sampleRowCount >= height)
            {
                var all = new int[height];
                for (int y = 0; y < height; y++)
                    all[y] = y;
                return all;
            }

            var rows = new int[sampleRowCount];
            for (int i = 0; i < sampleRowCount; i++)
            {
                int y = (int)Math.Round((i + 1.0) * height / (sampleRowCount + 1.0)) - 1;
                rows[i] = Math.Clamp(y, 0, height - 1);
            }

            return rows;
        }

        private static double GetColumnFillDensity(Mat mask, int columnX, int[] sampleRows)
        {
            if (sampleRows.Length == 0)
                return 0;

            int filled = 0;
            foreach (int y in sampleRows)
            {
                if (mask.At<byte>(y, columnX) > 0)
                    filled++;
            }

            return (double)filled / sampleRows.Length;
        }

        /// <summary>剔除頂部公告粉紅底，避免 Hue 接近紅色時誤判為 HP。</summary>
        private static void ExcludeAnnouncementPink(Mat bgr, Mat mask)
        {
            using var pink = new Mat();
            using var notPink = new Mat();
            Cv2.InRange(bgr, new Scalar(130, 100, 170), new Scalar(255, 200, 255), pink);
            Cv2.BitwiseNot(pink, notPink);
            Cv2.BitwiseAnd(mask, notPink, mask);
        }

        private static Mat BuildBgrMask(Mat bgr, bool isRed)
        {
            var mask = new Mat();
            if (isRed)
            {
                Cv2.InRange(bgr, new Scalar(0, 0, 130), new Scalar(115, 115, 255), mask);
            }
            else
            {
                // 實心青藍：B、G 都要夠高；排除空條暗藍灰底（B 高但 G/R 低）。
                using var range = new Mat();
                Cv2.InRange(bgr, new Scalar(140, 90, 0), new Scalar(255, 255, 140), range);
                Mat[] channels = bgr.Split();
                try
                {
                    using var gMinusR = new Mat();
                    Cv2.Subtract(channels[1], channels[2], gMinusR);
                    using var cyanBias = new Mat();
                    Cv2.Threshold(gMinusR, cyanBias, 25, 255, ThresholdTypes.Binary);
                    Cv2.BitwiseAnd(range, cyanBias, mask);
                }
                finally
                {
                    foreach (var ch in channels)
                        ch.Dispose();
                }
            }

            return mask;
        }

        private static Mat BuildColorMask(Mat hsv, int[] lowerHsv, int[] upperHsv, bool isRed)
        {
            var lower = new Scalar(lowerHsv[0], lowerHsv[1], lowerHsv[2]);
            var upper = new Scalar(upperHsv[0], upperHsv[1], upperHsv[2]);
            var mask = new Mat();

            if (isRed)
            {
                using var mask1 = new Mat();
                using var mask2 = new Mat();
                Cv2.InRange(hsv, lower, upper, mask1);
                Cv2.InRange(hsv, new Scalar(160, lowerHsv[1], lowerHsv[2]), new Scalar(180, 255, 255), mask2);
                Cv2.BitwiseOr(mask1, mask2, mask);
            }
            else
            {
                // MP 實心條飽和度與亮度都較高；空槽低飽和暗藍不應入選。
                int sMin = Math.Max(lowerHsv[1], 70);
                int vMin = Math.Max(lowerHsv[2], 90);
                Cv2.InRange(
                    hsv,
                    new Scalar(lowerHsv[0], sMin, vMin),
                    upper,
                    mask);
            }

            return mask;
        }

        private static double ClampRatio(double value) => Math.Clamp(value, 0, 1);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
