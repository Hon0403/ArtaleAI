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
    // ============================================================
    // 架構決策：為什麼 BloodBarDetector 不持有任何 Mat？
    //
    // 記憶體安全原則（三條鐵律）：
    //   1. BloodBarDetector 只透過 ITemplateManager.GetTemplate() 「借用」Mat，
    //      永遠不呼叫 .Dispose()（那是 TemplateManager 的工作）
    //   2. 所有在方法內部建立的中間態 Mat（hsvImage, redMask 等）
    //      一律用 using 語法確保離開 scope 時立即釋放
    //   3. 傳入的 frameMat 由「呼叫者（GamePipeline）持有 using」，
    //      BloodBarDetector 只讀取，絕不 Dispose
    //
    // 依賴注入設計：
    //   AppConfig 透過建構子注入，不再呼叫 AppConfig.Instance（Singleton 耦合）
    //   這讓未來單元測試可以 Mock config，驗證不同設定的偵測行為。
    // ============================================================

    /// <summary>
    /// 血條偵測器 — 負責找出角色血條並計算相關的偵測框和攻擊範圍框
    /// 從 GameVisionCore (God Class) 中提取，職責單一化
    /// </summary>
    public sealed class BloodBarDetector : IBloodBarDetector
    {
        // BloodBarDetector 不需要 ITemplateManager（血條偵測是 HSV 色彩空間，不用模板匹配）
        // 血條是純色偵測，ITemplateManager 留給後續的 MinimapDetector/MonsterDetector 使用
        private readonly AppConfig _config;
        private bool _disposed = false;

        #region IVisionDetector

        /// <inheritdoc/>
        public string Name => "BloodBarDetector";

        #endregion

        /// <summary>
        /// 初始化血條偵測器
        /// </summary>
        /// <param name="config">
        /// 應用程式設定（包含血條尺寸限制、HSV 範圍等）
        /// 透過建構子注入，避免 Singleton 耦合
        /// </param>
        public BloodBarDetector(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        #region IBloodBarDetector 實作

        /// <inheritdoc/>
        /// <remarks>
        /// 記憶體說明：
        ///   - frameMat：呼叫者負責 Dispose（BloodBarDetector 只讀取）
        ///   - 內部中間態 Mat（cameraArea, hsvImage, redMask）：全部 using，方法返回前即釋放
        /// </remarks>
        public Rectangle? DetectBloodBar(
            Mat frameMat,
            Rectangle? uiExcludeRect,
            out float cameraOffsetY)
        {
            cameraOffsetY = 0f;

            if (frameMat == null || frameMat.Empty()) return null;

            // 1. 裁切相機區域（排除 UI）
            // ── Mat 生命週期：using 確保離開 scope 時釋放 ──
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
                // 2. 轉換 BGR → HSV，建立紅色遮罩
                using var hsvImage = new Mat();
                Cv2.CvtColor(cameraArea, hsvImage, ColorConversionCodes.BGR2HSV);

                using var redMask = new Mat();
                var lowerRed = new Scalar(_config.Vision.LowerRedHsv[0], _config.Vision.LowerRedHsv[1], _config.Vision.LowerRedHsv[2]);
                var upperRed = new Scalar(_config.Vision.UpperRedHsv[0], _config.Vision.UpperRedHsv[1], _config.Vision.UpperRedHsv[2]);
                Cv2.InRange(hsvImage, lowerRed, upperRed, redMask);

                // 3. 找最佳血條輪廓
                var bestBar = FindBestRedBar(redMask);
                return bestBar.HasValue
                    ? new Rectangle(bestBar.Value.X, bestBar.Value.Y + (int)cameraOffsetY,
                                   bestBar.Value.Width, bestBar.Value.Height)
                    : null;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 記憶體說明：
        ///   - frameMat：呼叫者負責 Dispose
        ///   - DetectBloodBar 內部已透過 using 管理所有中間態 Mat
        /// </remarks>
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

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 計算血條相關的偵測框與攻擊範圍框
        /// 對應 GameVisionCore.CalculateBloodBarRelatedBoxes
        /// </summary>
        private (List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            CalculateBloodBarRelatedBoxes(Rectangle bloodBarRect)
        {
            // 計算怪物偵測框（以血條下緣中心為基準）
            var dotCenterX = bloodBarRect.X + bloodBarRect.Width / 2;
            var dotCenterY = bloodBarRect.Y + bloodBarRect.Height + _config.Vision.DotOffsetY;

            var detectionBox = new Rectangle(
                dotCenterX - _config.Vision.DetectionBoxWidth / 2,
                dotCenterY - _config.Vision.DetectionBoxHeight / 2,
                _config.Vision.DetectionBoxWidth,
                _config.Vision.DetectionBoxHeight);

            // 計算攻擊範圍框（以玩家中心為基準）
            var playerCenterX = bloodBarRect.X + bloodBarRect.Width / 2 + _config.Appearance.AttackRange.OffsetX;
            var playerCenterY = bloodBarRect.Y + bloodBarRect.Height + _config.Appearance.AttackRange.OffsetY;

            var attackRangeBox = new Rectangle(
                playerCenterX - _config.Appearance.AttackRange.Width / 2,
                playerCenterY - _config.Appearance.AttackRange.Height / 2,
                _config.Appearance.AttackRange.Width,
                _config.Appearance.AttackRange.Height);

            return (new List<Rectangle> { detectionBox }, new List<Rectangle> { attackRangeBox });
        }

        /// <summary>
        /// 從紅色遮罩中以輪廓分析找出最佳血條候選矩形
        /// 對應 GameVisionCore.FindBestRedBar（私有方法提升到本類別）
        /// </summary>
        /// <remarks>
        /// 記憶體說明：
        ///   - hierarchy, contours[] 全部在 finally 中釋放
        ///   - redMask 由呼叫方（DetectBloodBar）透過 using 管理，本方法只讀取
        /// </remarks>
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

                        // 多重條件篩選（寬高 + 面積）
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
                        contour?.Dispose(); // 每個輪廓即用即釋放
                    }
                }

                if (candidates.Count == 0) return null;

                // 選面積最大的候選
                return candidates.OrderByDescending(c => c.area).First().rect;
            }
            finally
            {
                hierarchy?.Dispose();
                // 注意：個別 contour 已在迴圈 finally 中釋放，這裡僅清理陣列本身
                // contours 陣列元素可能已釋放，僅需清理未被上方釋放的剩餘項
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

        #endregion

        #region IDisposable

        // 架構說明：BloodBarDetector 目前沒有需要管理的非受管資源。
        // 實作 IDisposable 是為了遵守 IVisionDetector 介面契約，
        // 以及確保未來若加入 Mat 欄位時有正確的釋放模式可延伸。

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // 目前無非受管資源需釋放
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
