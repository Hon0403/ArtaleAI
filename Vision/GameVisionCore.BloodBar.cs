using ArtaleAI.Models.Config;
using ArtaleAI.Shared;
using OpenCvSharp;
using System.Drawing;

namespace ArtaleAI.Vision
{
    /// <summary>
    /// 血條搜尋請求：
    /// <para>HasMinimapSelfMarker：小地圖自己黃點證明「人在場」（避免登入／選角畫面亂搜）。</para>
    /// <para>MinimapExcludeRect：實際偵測到的小地圖框（青色），於遮罩階段挖空避免其他玩家紅點誤判。</para>
    /// </summary>
    internal readonly record struct BloodBarSearchRequest(
        bool HasMinimapSelfMarker,
        Rectangle? MinimapExcludeRect = null,
        Rectangle? UiExcludeRect = null);

    /// <summary>整合的遊戲視覺核心。</summary>
    public partial class GameVisionCore
    {
        #region 血條檢測功能群組

        private static DateTime _lastBloodBarDiagUtc = DateTime.MinValue;

        /// <summary>上一幀血條外框（全畫面座標），供本幀鄰近加分。</summary>
        private Rectangle? _prevBloodBarFrame;

        private DateTime _prevBloodBarUtc = DateTime.MinValue;

        /// <summary>
        /// 隊伍血條外框：可玩區（扣小地圖／下方 UI）內以 contour 幾何過濾定位；
        /// 鎖定後縮小到上一幀鄰域追蹤（省 CPU、天然避開活動 ICON）。
        /// </summary>
        private Rectangle? DetectBloodBar(
            Mat frameMat,
            AppConfig config,
            BloodBarSearchRequest request)
        {
            var vision = config.Vision;
            if (!vision.UseBloodBarFixedFrame)
                return null;

            ExpirePrevBloodBarIfStale(vision);

            // 未鎖定且畫面無自己黃點：多半在登入／選角，直接略過避免誤搜。
            bool locked = _prevBloodBarFrame.HasValue;
            if (!locked && vision.BloodBarRequireMinimapSelf && !request.HasMinimapSelfMarker)
                return null;

            var searchRect = ResolveBloodBarDetectSearchRect(
                frameMat.Width,
                frameMat.Height,
                vision,
                _prevBloodBarFrame,
                request.UiExcludeRect);

            int frameW = Math.Max(1, vision.BloodBarFrameWidth);
            int frameH = Math.Max(1, vision.BloodBarFrameHeight);

            if (searchRect.IsEmpty)
                return null;

            if (searchRect.Width < frameW || searchRect.Height < frameH)
            {
                Logger.Warning(
                    $"[血條] 搜尋區過小 {searchRect}（畫面 {frameMat.Width}x{frameMat.Height}）");
                return null;
            }

            using var searchMat = new Mat(
                frameMat,
                new OpenCvSharp.Rect(searchRect.X, searchRect.Y, searchRect.Width, searchRect.Height));

            var minimapExclude = ResolveMinimapExclusion(
                request.MinimapExcludeRect, frameMat.Width, frameMat.Height, vision);

            using var redMask = BuildStrictHpRedMask(searchMat);
            MaskOutMinimapRegion(redMask, searchRect, minimapExclude);

            using var tipMask = BuildFrameTipMask(searchMat, vision);
            MaskOutMinimapRegion(tipMask, searchRect, minimapExclude);

            var prevLocal = ToSearchLocal(_prevBloodBarFrame, searchRect);
            var (local, bestScore, fillCount) = FindFixedFrameFromRedFill(
                redMask, tipMask, frameW, frameH, vision, prevLocal);
            MaybeLogBloodBarDiag(
                redMask, tipMask, local, bestScore, fillCount,
                searchRect, frameMat.Width, frameMat.Height);

            if (!local.HasValue)
            {
                _prevBloodBarFrame = null;
                return null;
            }

            var full = OffsetToFrame(searchRect, local.Value);
            _prevBloodBarFrame = full;
            _prevBloodBarUtc = DateTime.UtcNow;
            return full;
        }

        private void ExpirePrevBloodBarIfStale(VisionSettings vision)
        {
            if (!_prevBloodBarFrame.HasValue)
                return;

            int holdMs = Math.Max(200, vision.BloodBarTrackHoldMs);
            if ((DateTime.UtcNow - _prevBloodBarUtc).TotalMilliseconds > holdMs)
            {
                _prevBloodBarFrame = null;
            }
        }

        private static Rectangle? ToSearchLocal(Rectangle? fullFrame, Rectangle searchRect)
        {
            if (!fullFrame.HasValue)
                return null;

            var f = fullFrame.Value;
            var local = new Rectangle(
                f.X - searchRect.X,
                f.Y - searchRect.Y,
                f.Width,
                f.Height);
            var clipped = Rectangle.Intersect(
                local,
                new Rectangle(0, 0, searchRect.Width, searchRect.Height));
            return clipped.Width > 0 && clipped.Height > 0 ? clipped : null;
        }

        private static void MaybeLogBloodBarDiag(
            Mat redMask,
            Mat tipMask,
            Rectangle? local,
            double bestScore,
            int fillCandidates,
            Rectangle searchRect,
            int frameWidth,
            int frameHeight)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastBloodBarDiagUtc).TotalSeconds < 2)
                return;

            _lastBloodBarDiagUtc = now;
            string hit = local.HasValue
                ? $"hit={local.Value.Width}x{local.Value.Height}@{local.Value.X},{local.Value.Y}"
                : "hit=none";
            Logger.Info(
                $"[血條外框] 畫面={frameWidth}x{frameHeight} ROI={searchRect.X},{searchRect.Y} " +
                $"{searchRect.Width}x{searchRect.Height} " +
                $"red={Cv2.CountNonZero(redMask)} tip={Cv2.CountNonZero(tipMask)} " +
                $"fills={fillCandidates} score={bestScore:F3} {hit}");
        }

        /// <summary>
        /// 嚴格 HP 紅（高 R、低 G/B）。排除泥土棕紅，大幅降低誤判。
        /// </summary>
        private static Mat BuildStrictHpRedMask(Mat bgrMat)
        {
            var mask = new Mat();
            // BGR：B&G 低、R 高
            Cv2.InRange(bgrMat, new Scalar(0, 0, 170), new Scalar(100, 100, 255), mask);
            return mask;
        }

        /// <summary>端點近白遮罩（驗證外框左右括號用）。</summary>
        private static Mat BuildFrameTipMask(Mat bgrMat, VisionSettings vision)
        {
            using var hsv = new Mat();
            Cv2.CvtColor(bgrMat, hsv, ColorConversionCodes.BGR2HSV);

            int vMin = Math.Clamp(vision.BloodBarFrameTipMinBgr, 0, 255);
            var tip = new Mat();
            Cv2.InRange(hsv, new Scalar(0, 0, vMin), new Scalar(180, 90, 255), tip);
            return tip;
        }

        /// <summary>
        /// 紅填充 blob → 對齊固定外框 → 驗證左右端點；靠近上一幀位置加分。
        /// </summary>
        private static (Rectangle? Rect, double BestScore, int FillCount) FindFixedFrameFromRedFill(
            Mat redMask,
            Mat tipMask,
            int frameW,
            int frameH,
            VisionSettings vision,
            Rectangle? prevLocal)
        {
            Cv2.FindContours(
                redMask,
                out OpenCvSharp.Point[][] contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            using var tipIntegral = new Mat();
            Cv2.Integral(tipMask, tipIntegral, MatType.CV_32SC1);
            using var redIntegral = new Mat();
            Cv2.Integral(redMask, redIntegral, MatType.CV_32SC1);

            int tipStrip = Math.Clamp(frameW / 16, 2, 4);
            double minTipSide = Math.Clamp(vision.BloodBarFrameTipMinSide, 0.05, 0.8);
            double minInteriorRed = Math.Clamp(vision.BloodBarFrameMinInteriorRed, 0.05, 0.95);
            double bandSide = tipStrip * frameH * 255.0;
            double frameArea = frameW * frameH * 255.0;
            double trackRadius = Math.Max(8, vision.BloodBarTrackRadiusPx);
            double trackWeight = Math.Clamp(vision.BloodBarTrackWeight, 0, 1);
            double minAcceptScore = Math.Clamp(vision.BloodBarFrameMinAcceptScore, 0.05, 2.0);
            double maxInteriorTip = Math.Clamp(vision.BloodBarFrameMaxInteriorTip, 0.02, 0.8);
            double minFillAspect = Math.Clamp(vision.BloodBarFrameMinFillAspect, 1.5, 20.0);

            // 高度貼近固定外框，擋稱號／圖示那種偏高紅塊
            int minFillH = Math.Max(2, frameH - 2);
            int maxFillH = frameH + 1;
            int minFillW = Math.Clamp(vision.BloodBarFrameMinFillWidth, 3, frameW);
            int maxFillW = frameW + 2;

            double bestScore = 0;
            Rectangle? best = null;
            int fillCount = 0;

            // 先試上一幀位置（人物微移時低血仍黏住）；同樣走嚴格閘門
            if (prevLocal.HasValue)
            {
                var sticky = ClampRectToBounds(
                    new Rectangle(prevLocal.Value.X, prevLocal.Value.Y, frameW, frameH),
                    redMask.Width,
                    redMask.Height);
                if (TryScoreFrameCandidate(
                        sticky, tipIntegral, redIntegral, tipStrip, bandSide, frameArea,
                        minTipSide, minInteriorRed, maxInteriorTip,
                        prevLocal, trackRadius, trackWeight,
                        out double stickyScore))
                {
                    bestScore = stickyScore + 0.05;
                    best = sticky;
                }
            }

            if (contours != null && contours.Length > 0)
            {
                foreach (var contour in contours)
                {
                    if (contour == null || contour.Length < 3)
                        continue;

                    var br = Cv2.BoundingRect(contour);
                    if (br.Width < minFillW || br.Width > maxFillW ||
                        br.Height < minFillH || br.Height > maxFillH)
                        continue;

                    // 長寬比只擋「夠寬才看得出是橫條」的候選。
                    // 低血短紅條寬度小、比例必然偏低；若硬套用會在約 1/3 血以下冷啟漏檢，
                    // 誤觸發隊伍重建。短紅條改由高度閘門 + 左右端點括號把關。
                    int aspectGateMinWidth = (int)Math.Ceiling(frameH * minFillAspect);
                    if (br.Width >= aspectGateMinWidth)
                    {
                        double aspect = br.Width / (double)Math.Max(1, br.Height);
                        if (aspect < minFillAspect)
                            continue;
                    }

                    fillCount++;

                    int frameX = Math.Max(0, br.X - 1);
                    int frameY = br.Y + (br.Height - frameH) / 2;
                    var frame = ClampRectToBounds(
                        new Rectangle(frameX, frameY, frameW, frameH),
                        redMask.Width,
                        redMask.Height);
                    if (frame.Width < frameW * 0.9 || frame.Height < frameH * 0.9)
                        continue;

                    if (!TryScoreFrameCandidate(
                            frame, tipIntegral, redIntegral, tipStrip, bandSide, frameArea,
                            minTipSide, minInteriorRed, maxInteriorTip,
                            prevLocal, trackRadius, trackWeight,
                            out double score))
                        continue;

                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    best = frame;
                }
            }

            // 不夠像就當沒有，避免垃圾候選仍回傳最高分
            if (!best.HasValue || bestScore < minAcceptScore)
                return (null, bestScore, fillCount);

            return (best, bestScore, fillCount);
        }

        private static bool TryScoreFrameCandidate(
            Rectangle frame,
            Mat tipIntegral,
            Mat redIntegral,
            int tipStrip,
            double bandSide,
            double frameArea,
            double minTipSide,
            double minInteriorRed,
            double maxInteriorTip,
            Rectangle? prevLocal,
            double trackRadius,
            double trackWeight,
            out double score)
        {
            score = 0;
            double redIn = IntegralRectSum(
                redIntegral, frame.X, frame.Y, frame.Width, frame.Height) / frameArea;
            if (redIn < minInteriorRed)
                return false;

            double tipLeft = IntegralRectSum(
                tipIntegral, frame.X, frame.Y, tipStrip, frame.Height) / bandSide;
            double tipRight = IntegralRectSum(
                tipIntegral, frame.X + frame.Width - tipStrip, frame.Y, tipStrip, frame.Height)
                / bandSide;

            // 真實外框左右括號都在；單側近白不足以過關
            if (tipLeft < minTipSide || tipRight < minTipSide)
                return false;

            // 中段不該滿是亮邊（活動 ICON／圓徽常見）；真血條中段是紅或空槽
            int innerW = Math.Max(1, frame.Width - 2 * tipStrip);
            double interiorTip = IntegralRectSum(
                tipIntegral, frame.X + tipStrip, frame.Y, innerW, frame.Height)
                / (innerW * frame.Height * 255.0);
            if (interiorTip > maxInteriorTip)
                return false;

            double tipScore = 0.5 * (tipLeft + tipRight);
            score = 0.50 * Math.Min(1.0, redIn / 0.35) + 0.50 * tipScore;
            score += ComputeTrackBonus(frame, prevLocal, trackRadius, trackWeight);
            return true;
        }

        private static double ComputeTrackBonus(
            Rectangle frame,
            Rectangle? prevLocal,
            double trackRadius,
            double trackWeight)
        {
            if (!prevLocal.HasValue || trackWeight <= 0 || trackRadius <= 0)
                return 0;

            double cx = frame.X + frame.Width * 0.5;
            double cy = frame.Y + frame.Height * 0.5;
            double px = prevLocal.Value.X + prevLocal.Value.Width * 0.5;
            double py = prevLocal.Value.Y + prevLocal.Value.Height * 0.5;
            double dist = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
            if (dist >= trackRadius)
                return 0;

            return trackWeight * (1.0 - dist / trackRadius);
        }

        private static int IntegralRectSum(Mat integral, int x, int y, int width, int height)
        {
            int x2 = x + width;
            int y2 = y + height;
            return integral.Get<int>(y2, x2)
                - integral.Get<int>(y, x2)
                - integral.Get<int>(y2, x)
                + integral.Get<int>(y, x);
        }

        private static Rectangle ClampRectToBounds(Rectangle rect, int boundsW, int boundsH)
        {
            int x = Math.Clamp(rect.X, 0, Math.Max(0, boundsW - 1));
            int y = Math.Clamp(rect.Y, 0, Math.Max(0, boundsH - 1));
            int w = Math.Clamp(rect.Width, 1, boundsW - x);
            int h = Math.Clamp(rect.Height, 1, boundsH - y);
            return new Rectangle(x, y, w, h);
        }

        private static Rectangle OffsetToFrame(Rectangle searchRect, Rectangle local) =>
            new(searchRect.X + local.X, searchRect.Y + local.Y, local.Width, local.Height);

        /// <summary>
        /// 血條搜尋要排除的左上角落：從畫面 (0,0) 延伸到小地圖右緣／下緣。
        /// 用整塊角落而非只挖小地圖本體：洋紅框呈 L 形、且隨小地圖尺寸自動伸縮。
        /// 優先用實際偵測框（青色）；未偵測到時退回百分比搜尋 ROI。停用則回傳空。
        /// </summary>
        internal static Rectangle ResolveMinimapExclusion(
            Rectangle? detectedMinimap,
            int frameWidth,
            int frameHeight,
            VisionSettings vision)
        {
            if (!vision.ExcludeMinimapRoiFromBloodBar)
                return Rectangle.Empty;

            Rectangle basis;
            if (detectedMinimap is { Width: > 0, Height: > 0 } box)
                basis = box;
            else if (vision.UseMinimapSearchRoi)
                basis = ResolveMinimapSearchRect(frameWidth, frameHeight, vision);
            else
                return Rectangle.Empty;

            return Rectangle.FromLTRB(0, 0, basis.Right, basis.Bottom);
        }

        private static void MaskOutMinimapRegion(
            Mat mask,
            Rectangle searchRect,
            Rectangle minimapExclude)
        {
            if (minimapExclude.Width <= 0 || minimapExclude.Height <= 0)
                return;

            var local = Rectangle.Intersect(minimapExclude, searchRect);
            if (local.Width <= 0 || local.Height <= 0)
                return;

            Cv2.Rectangle(
                mask,
                new OpenCvSharp.Rect(
                    local.X - searchRect.X,
                    local.Y - searchRect.Y,
                    local.Width,
                    local.Height),
                Scalar.All(0),
                -1);
        }

        /// <summary>
        /// 可玩區（全畫面扣除底部 UI）。底部排除以畫面高度百分比計算，跨解析度一致。
        /// </summary>
        internal static Rectangle ResolveBloodBarSearchRect(
            int frameWidth,
            int frameHeight,
            VisionSettings settings,
            Rectangle? uiExcludeRect = null)
        {
            if (frameWidth <= 0 || frameHeight <= 0)
                return Rectangle.Empty;

            double pct = Math.Clamp(settings.BloodBarBottomUiPercent, 0.0, 0.9);
            int bottomUiTop = uiExcludeRect?.Y
                ?? (frameHeight - (int)(frameHeight * pct));

            return new Rectangle(0, 0, frameWidth, Math.Clamp(bottomUiTop, 1, frameHeight));
        }

        /// <summary>
        /// 血條搜尋區。鎖定→上一幀鄰域（padding = max(外框寬, 追蹤半徑)）；冷啟→整個可玩區。
        /// 小地圖不在此扣除（於遮罩階段挖空）；下方 UI 已由可玩區高度排除。
        /// </summary>
        internal static Rectangle ResolveBloodBarDetectSearchRect(
            int frameWidth,
            int frameHeight,
            VisionSettings settings,
            Rectangle? prevBloodBar,
            Rectangle? uiExcludeRect = null)
        {
            var playfield = ResolveBloodBarSearchRect(
                frameWidth, frameHeight, settings, uiExcludeRect);
            if (playfield.IsEmpty)
                return playfield;

            if (prevBloodBar is { Width: > 0, Height: > 0 } prev)
            {
                int pad = Math.Max(settings.BloodBarFrameWidth, settings.BloodBarTrackRadiusPx);
                var tracked = Rectangle.Inflate(prev, pad, pad);
                var clipped = Rectangle.Intersect(tracked, playfield);
                if (!clipped.IsEmpty)
                    return clipped;
            }

            return playfield;
        }

        /// <summary>完整的血條檢測：外框 → 偵測框＋攻擊範圍。</summary>
        public (Rectangle? BloodBar, List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            ProcessBloodBarDetection(
                Mat frameMat,
                Rectangle? uiExcludeRect,
                bool hasMinimapSelfMarker = false,
                Rectangle? minimapExcludeRect = null)
        {
            var config = AppConfig.Instance;
            var bloodBar = DetectBloodBar(
                frameMat,
                config,
                new BloodBarSearchRequest(
                    HasMinimapSelfMarker: hasMinimapSelfMarker,
                    MinimapExcludeRect: minimapExcludeRect,
                    UiExcludeRect: uiExcludeRect));

            if (bloodBar.HasValue)
            {
                var (detectionBoxes, attackRangeBoxes) =
                    CalculateBloodBarRelatedBoxes(bloodBar.Value, config);

                return (bloodBar, detectionBoxes, attackRangeBoxes);
            }

            return (null, new List<Rectangle>(), new List<Rectangle>());
        }

        private (List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            CalculateBloodBarRelatedBoxes(Rectangle bloodBarRect, AppConfig config)
        {
            var dotCenterX = bloodBarRect.X + bloodBarRect.Width / 2;
            var dotCenterY = bloodBarRect.Y + bloodBarRect.Height + config.Vision.DotOffsetY;

            var detectionBox = new Rectangle(
                dotCenterX - config.Vision.DetectionBoxWidth / 2,
                dotCenterY - config.Vision.DetectionBoxHeight / 2,
                config.Vision.DetectionBoxWidth,
                config.Vision.DetectionBoxHeight
            );

            var playerCenterX = bloodBarRect.X + bloodBarRect.Width / 2 + config.Appearance.AttackRange.OffsetX;
            var playerCenterY = bloodBarRect.Y + bloodBarRect.Height + config.Appearance.AttackRange.OffsetY;

            var attackRangeBox = new Rectangle(
                playerCenterX - config.Appearance.AttackRange.Width / 2,
                playerCenterY - config.Appearance.AttackRange.Height / 2,
                config.Appearance.AttackRange.Width,
                config.Appearance.AttackRange.Height
            );

            return (
                new List<Rectangle> { detectionBox },
                new List<Rectangle> { attackRangeBox }
            );
        }

        #endregion
    }
}
