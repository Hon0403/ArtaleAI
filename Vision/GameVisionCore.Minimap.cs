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
    public partial class GameVisionCore
    {
        #region 小地圖檢測功能群組

        /// <summary>
        /// 在螢幕上尋找小地圖位置（絕對像素系統）
        /// 使用顏色基礎偵測法（白框連通分量），不再依賴範本匹配
        /// </summary>
        /// <param name="fullFrameMat">完整畫面 Mat</param>
        /// <returns>小地圖矩形區域，未找到時返回 null</returns>
        public Rectangle? FindMinimapOnScreen(Mat fullFrameMat)
        {
            if (fullFrameMat?.Empty() != false) return null;

            try
            {
                var config = AppConfig.Instance;

                if (config.Vision.UseFixedMinimapPosition)
                {
                    return new Rectangle(0, 0, config.Vision.FixedMinimapWidth, config.Vision.FixedMinimapHeight);
                }

                var now = DateTime.UtcNow;
                if (_cachedMinimapRect.HasValue && (now - _lastFullScanTime).TotalMilliseconds < FullScanIntervalMs)
                {
                    if (VerifyMinimapAt(fullFrameMat, _cachedMinimapRect.Value))
                    {
                        return _cachedMinimapRect;
                    }
                }

                var result = FindMinimapByColor(fullFrameMat);
                if (result.HasValue)
                {
                    _cachedMinimapRect = result;
                    _lastFullScanTime = now;
                }
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"[小地圖] FindMinimapOnScreen 錯誤: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 快速驗證指定區域是否仍具備小地圖邊框特徵（僅檢查 4 個角點與邊緣取樣）
        /// </summary>
        private bool VerifyMinimapAt(Mat fullFrameMat, Rectangle rect)
        {
            if (rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > fullFrameMat.Width || rect.Y + rect.Height > fullFrameMat.Height)
                return false;

            try
            {
                var config = AppConfig.Instance;
                var colorParts = config.Vision.MinimapFrameColorBgr.Split(',');
                byte b = byte.Parse(colorParts[0].Trim());
                byte g = byte.Parse(colorParts[1].Trim());
                byte r = byte.Parse(colorParts[2].Trim());

                int[] xs = { rect.Left, rect.Right - 1 };
                int[] ys = { rect.Top, rect.Bottom - 1 };

                foreach (var x in xs)
                {
                    foreach (var y in ys)
                    {
                        var p = fullFrameMat.At<Vec3b>(y, x);
                        if (p.Item0 != b || p.Item1 != g || p.Item2 != r) return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[小地圖] VerifyMinimapAt 例外 (rect={rect})", ex);
                return false;
            }
        }

        /// <summary>
        /// 使用顏色特徵偵測小地圖位置（白框連通分量法）。
        /// 可選左上角百分比 ROI，縮小搜尋範圍並回傳全畫面座標。
        /// </summary>
        private Rectangle? FindMinimapByColor(Mat fullFrameMat)
        {
            if (fullFrameMat?.Empty() != false) return null;

            try
            {
                var config = AppConfig.Instance;
                var searchRect = ResolveMinimapSearchRect(
                    fullFrameMat.Width, fullFrameMat.Height, config.Vision);

                if (searchRect.Width < config.Vision.MinMinimapWidth
                    || searchRect.Height < config.Vision.MinMinimapHeight)
                {
                    Logger.Warning(
                        $"[小地圖] 搜尋 ROI 過小 {searchRect}（畫面 {fullFrameMat.Width}x{fullFrameMat.Height}），請調整 minimapSearchRoi");
                    return null;
                }

                using var searchMat = new Mat(
                    fullFrameMat,
                    new OpenCvSharp.Rect(searchRect.X, searchRect.Y, searchRect.Width, searchRect.Height));

                var local = FindMinimapByColorInMat(searchMat, config);
                if (!local.HasValue)
                    return null;

                return new Rectangle(
                    searchRect.X + local.Value.X,
                    searchRect.Y + local.Value.Y,
                    local.Value.Width,
                    local.Value.Height);
            }
            catch (Exception ex)
            {
                Logger.Error($"[小地圖-顏色偵測] 錯誤: {ex.Message}");
                return null;
            }
        }

        /// <summary>將百分比 ROI 轉成像素矩形；關閉 ROI 時回傳全畫面。</summary>
        internal static Rectangle ResolveMinimapSearchRect(
            int frameWidth,
            int frameHeight,
            VisionSettings settings)
        {
            if (frameWidth <= 0 || frameHeight <= 0)
                return Rectangle.Empty;

            if (!settings.UseMinimapSearchRoi)
                return new Rectangle(0, 0, frameWidth, frameHeight);

            var roi = settings.MinimapSearchRoi ?? new MinimapSearchRoiPercent();
            int left = PercentToPixels(roi.LeftPercent, frameWidth, minPixels: 0);
            int top = PercentToPixels(roi.TopPercent, frameHeight, minPixels: 0);
            int width = PercentToPixels(roi.WidthPercent, frameWidth);
            int height = PercentToPixels(roi.HeightPercent, frameHeight);

            left = Math.Clamp(left, 0, Math.Max(0, frameWidth - 1));
            top = Math.Clamp(top, 0, Math.Max(0, frameHeight - 1));
            width = Math.Clamp(width, 1, frameWidth - left);
            height = Math.Clamp(height, 1, frameHeight - top);

            return new Rectangle(left, top, width, height);
        }

        private static int PercentToPixels(double percent, int frameSize, int minPixels = 1)
        {
            if (frameSize <= 0)
                return minPixels;

            double clamped = Math.Clamp(percent, 0, 1);
            return Math.Max(minPixels, (int)Math.Round(frameSize * clamped));
        }

        /// <summary>在既定 Mat（可能是搜尋 ROI 裁切）內找白框小地圖，座標相對該 Mat。</summary>
        private static Rectangle? FindMinimapByColorInMat(Mat regionMat, AppConfig config)
        {
            var colorParts = config.Vision.MinimapFrameColorBgr.Split(',');
            byte b = byte.Parse(colorParts[0].Trim());
            byte g = byte.Parse(colorParts[1].Trim());
            byte r = byte.Parse(colorParts[2].Trim());
            var frameColor = new Scalar(b, g, r);

            using var maskWhite = new Mat();
            Cv2.InRange(regionMat, frameColor, frameColor, maskWhite);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int numLabels = Cv2.ConnectedComponentsWithStats(
                maskWhite, labels, stats, centroids, PixelConnectivity.Connectivity8);

            for (int i = 1; i < numLabels; i++)
            {
                int x0 = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                int y0 = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                int rw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                int rh = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

                if (rw < config.Vision.MinMinimapWidth || rh < config.Vision.MinMinimapHeight)
                    continue;

                int x1 = x0 + rw - 1;
                int y1 = y0 + rh - 1;

                bool validFrame = true;

                for (int x = x0; x <= x1 && validFrame; x++)
                {
                    var pixel = regionMat.At<Vec3b>(y0, x);
                    if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                        validFrame = false;
                }

                for (int x = x0; x <= x1 && validFrame; x++)
                {
                    var pixel = regionMat.At<Vec3b>(y1, x);
                    if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                        validFrame = false;
                }

                for (int y = y0; y <= y1 && validFrame; y++)
                {
                    var pixel = regionMat.At<Vec3b>(y, x0);
                    if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                        validFrame = false;
                }

                for (int y = y0; y <= y1 && validFrame; y++)
                {
                    var pixel = regionMat.At<Vec3b>(y, x1);
                    if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                        validFrame = false;
                }

                if (!validFrame)
                    continue;

                using var roiMat = regionMat[new OpenCvSharp.Rect(x0, y0, rw, rh)];
                using var maskNonWhite = new Mat();
                Cv2.InRange(roiMat, frameColor, frameColor, maskNonWhite);
                Cv2.BitwiseNot(maskNonWhite, maskNonWhite);

                using var nonZeroMat = new Mat();
                Cv2.FindNonZero(maskNonWhite, nonZeroMat);
                if (nonZeroMat.Empty())
                    continue;

                var innerRect = Cv2.BoundingRect(nonZeroMat);

                return new Rectangle(
                    x0 + innerRect.X,
                    y0 + innerRect.Y,
                    innerRect.Width,
                    innerRect.Height);
            }

            return null;
        }

        /// <summary>
        /// 完整的小地圖追蹤處理：檢測小地圖位置、玩家與其他玩家位置。
        /// </summary>
        /// <param name="fullFrameMat">完整畫面 Mat</param>
        /// <param name="captureTime">該幀擷取時間（寫入追蹤結果以利同步）。</param>
        /// <returns>小地圖追蹤結果，失敗時返回 null</returns>
        public MinimapTrackingResult? GetMinimapTracking(Mat fullFrameMat, DateTime captureTime)
        {
            if (fullFrameMat?.Empty() != false) return null;

            try
            {
                var minimapRect = FindMinimapOnScreen(fullFrameMat);
                if (!minimapRect.HasValue)
                {
                    Logger.Warning("[小地圖偵測] 找不到小地圖");
                    return null;
                }

                using var minimapMat = new Mat(fullFrameMat, new OpenCvSharp.Rect(
                    minimapRect.Value.X, minimapRect.Value.Y,
                    minimapRect.Value.Width, minimapRect.Value.Height));

                lock (_minimapMatLock)
                {
                    LastMinimapMat?.Dispose();
                    LastMinimapMat = minimapMat.Clone();
                }

                System.Drawing.PointF? playerPos = null;
                try
                {
                    var playerStyle = AppConfig.Instance.Appearance.MinimapPlayer;
                    if (TryFindColorCentroid(
                            minimapMat,
                            playerStyle.PlayerColorBgr,
                            playerStyle.ColorTolerance,
                            playerStyle.MinPixelCount,
                            playerStyle.OffsetY,
                            out var foundPlayer,
                            out int pixelCount))
                    {
                        playerPos = foundPlayer;
                        if (Math.Abs(pixelCount - _lastPixelCount) > 5)
                            _lastPixelCount = pixelCount;
                    }
                    else
                    {
                        Debug.WriteLine($"[顏色匹配] 自己玩家像素數不足（min={playerStyle.MinPixelCount}）");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[小地圖] 自己玩家顏色偵測錯誤: {ex.Message}");
                }

                var otherPlayers = new List<System.Drawing.PointF>();
                if (AppConfig.Instance.Navigation.EnableOtherPlayersDetection)
                {
                    try
                    {
                        var otherStyle = AppConfig.Instance.Appearance.MinimapOtherPlayer;
                        otherPlayers = FindColorBlobCentroids(
                            minimapMat,
                            otherStyle.OtherPlayerColorBgr,
                            otherStyle.ColorTolerance,
                            otherStyle.MinPixelCount,
                            otherStyle.OffsetY,
                            otherStyle.MaxDetectCount,
                            excludeNear: playerPos,
                            excludeRadiusPx: 4f);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[小地圖] 其他玩家顏色偵測錯誤: {ex.Message}");
                    }
                }

                return new MinimapTrackingResult(
                    playerPos,
                    otherPlayers,
                    captureTime,
                    1.0
                )
                {
                    MinimapBounds = minimapRect.Value
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"[小地圖] 小地圖追蹤錯誤: {ex.Message}");
                return null;
            }
        }

        #endregion
        #region 私有輔助方法 - 小地圖相關

        /// <summary>
        /// 解析「R,G,B」字串並轉成 OpenCV BGR 上下界。
        /// 與既有 <c>PlayerColorBgr</c> 慣例相同：字串是 RGB，Scalar 才是 BGR。
        /// </summary>
        private static bool TryBuildBgrRange(
            string colorRgbCsv,
            int tolerance,
            out Scalar lowerBgr,
            out Scalar upperBgr)
        {
            lowerBgr = default;
            upperBgr = default;

            var parts = colorRgbCsv.Split(',');
            if (parts.Length < 3)
                return false;
            if (!int.TryParse(parts[0].Trim(), out int r) ||
                !int.TryParse(parts[1].Trim(), out int g) ||
                !int.TryParse(parts[2].Trim(), out int b))
                return false;

            int tol = Math.Max(0, tolerance);
            lowerBgr = new Scalar(
                Math.Max(0, b - tol),
                Math.Max(0, g - tol),
                Math.Max(0, r - tol));
            upperBgr = new Scalar(
                Math.Min(255, b + tol),
                Math.Min(255, g + tol),
                Math.Min(255, r + tol));
            return true;
        }

        /// <summary>單色塊質心（自己玩家：通常只有一個標記）。</summary>
        private static bool TryFindColorCentroid(
            Mat sourceBgr,
            string colorRgbCsv,
            int tolerance,
            int minPixels,
            float offsetY,
            out System.Drawing.PointF centroid,
            out int pixelCount)
        {
            centroid = default;
            pixelCount = 0;

            if (!TryBuildBgrRange(colorRgbCsv, tolerance, out var lower, out var upper))
                return false;

            using var mask = new Mat();
            Cv2.InRange(sourceBgr, lower, upper, mask);
            var moments = Cv2.Moments(mask, true);
            pixelCount = (int)moments.M00;
            if (pixelCount < Math.Max(1, minPixels))
                return false;

            centroid = new System.Drawing.PointF(
                (float)(moments.M10 / moments.M00),
                (float)(moments.M01 / moments.M00) + offsetY);
            return true;
        }

        /// <summary>
        /// 多色塊質心（其他玩家：可能多人）。
        /// 以連通元件分開各標記；可排除靠近自己玩家的 blob，降低誤判。
        /// </summary>
        private static List<System.Drawing.PointF> FindColorBlobCentroids(
            Mat sourceBgr,
            string colorRgbCsv,
            int tolerance,
            int minPixels,
            float offsetY,
            int maxDetectCount,
            System.Drawing.PointF? excludeNear,
            float excludeRadiusPx)
        {
            var results = new List<System.Drawing.PointF>();
            if (!TryBuildBgrRange(colorRgbCsv, tolerance, out var lower, out var upper))
                return results;

            using var mask = new Mat();
            Cv2.InRange(sourceBgr, lower, upper, mask);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int numLabels = Cv2.ConnectedComponentsWithStats(
                mask, labels, stats, centroids, PixelConnectivity.Connectivity8);

            int minArea = Math.Max(1, minPixels);
            int cap = Math.Max(1, maxDetectCount);
            float excludeR2 = excludeRadiusPx * excludeRadiusPx;

            var candidates = new List<(float Area, System.Drawing.PointF Pos)>(numLabels);
            for (int i = 1; i < numLabels; i++)
            {
                int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                if (area < minArea)
                    continue;

                double cx = centroids.At<double>(i, 0);
                double cy = centroids.At<double>(i, 1);
                var pos = new System.Drawing.PointF((float)cx, (float)cy + offsetY);

                if (excludeNear.HasValue)
                {
                    float dx = pos.X - excludeNear.Value.X;
                    float dy = pos.Y - excludeNear.Value.Y;
                    if (dx * dx + dy * dy <= excludeR2)
                        continue;
                }

                candidates.Add((area, pos));
            }

            // 面積大的優先，較像真實玩家點而非單像素噪點。
            foreach (var c in candidates.OrderByDescending(x => x.Area).Take(cap))
                results.Add(c.Pos);

            return results;
        }

        #endregion
        /// <summary>
        /// 執行一次性的螢幕擷取（記憶體優化版本）
        /// 擷取遊戲視窗畫面並檢測小地圖和玩家位置
        /// </summary>
        /// <param name="windowHandle">視窗句柄（保留參數，未使用）</param>
        /// <param name="config">應用程式設定</param>
        /// <param name="selectedItem">已選擇的擷取項目（可選）</param>
        /// <param name="progressReporter">進度回報回調函數</param>
        /// <returns>包含小地圖圖像和玩家位置的結果</returns>
        public async Task<MinimapResult?> GetSnapshotAsync(nint windowHandle, AppConfig config, GraphicsCaptureItem? selectedItem, Action<string>? progressReporter)
        {
            GraphicsCapturer? capturer = null;
            try
            {
                selectedItem = await GetOrSelectCaptureItem(windowHandle, config, selectedItem, progressReporter);
                if (selectedItem == null)
                {
                    progressReporter?.Invoke("未選擇視窗");
                    return null;
                }

                capturer = new GraphicsCapturer(selectedItem);
                await Task.Delay(100);

                using var fullFrame = capturer.TryGetNextMat();
                if (fullFrame == null)
                {
                    progressReporter?.Invoke("無法擷取畫面");
                    return null;
                }

                var minimapRect = FindMinimapOnScreen(fullFrame);
                if (!minimapRect.HasValue)
                {
                    progressReporter?.Invoke("找不到小地圖");
                    throw new Exception("無法偵測到小地圖區域");
                }

                using var minimapMat = new Mat(fullFrame, new OpenCvSharp.Rect(
                    minimapRect.Value.X, minimapRect.Value.Y,
                    minimapRect.Value.Width, minimapRect.Value.Height));

                var minimapBitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(minimapMat);

                return new MinimapResult(
                    minimapBitmap,
                    null,
                    selectedItem,
                    minimapRect.Value
                );
            }
            catch (Exception ex)
            {
                progressReporter?.Invoke($"小地圖檢測失敗: {ex.Message}");
                Logger.Error($"[系統] GetSnapshotAsync 異常: {ex.Message}");
                return null;
            }
            finally
            {
                capturer?.Dispose();
            }
        }

        /// <summary>
        /// 獲取或選擇擷取項目
        /// 如果沒有提供擷取項目，會自動嘗試尋找遊戲視窗
        /// </summary>
        /// <param name="windowHandle">視窗句柄（保留參數，未使用）</param>
        /// <param name="config">應用程式設定</param>
        /// <param name="selectedItem">已選擇的擷取項目（可選）</param>
        /// <param name="progressReporter">進度回報回調函數</param>
        /// <returns>擷取項目，失敗時返回 null</returns>
        private async Task<GraphicsCaptureItem?> GetOrSelectCaptureItem(nint windowHandle, AppConfig config, GraphicsCaptureItem? selectedItem, Action<string>? progressReporter)
        {
            if (selectedItem == null)
            {
                progressReporter?.Invoke("正在嘗試自動找到遊戲視窗...");
                selectedItem = WindowFinder.TryCreateItemWithFallback(config, progressReporter);

                if (selectedItem == null)
                {
                    progressReporter?.Invoke("自動尋找失敗，請手動選擇視窗。");
                    var picker = new GraphicsCapturePicker();
                    InitializeWithWindow.Initialize(picker, windowHandle);
                    selectedItem = await picker.PickSingleItemAsync();

                    if (selectedItem != null)
                    {
                        await SaveWindowSelection(selectedItem, progressReporter);
                        progressReporter?.Invoke($"已記住選擇: {selectedItem.DisplayName}");
                    }
                }
            }

            return selectedItem;
        }

        /// <summary>
        /// 儲存使用者手動選擇的視窗資訊
        /// 記錄視窗名稱和對應程序資訊，用於下次自動尋找
        /// </summary>
        /// <param name="item">選擇的擷取項目</param>
        /// <param name="config">應用程式設定（用於儲存記錄）</param>
        /// <param name="progressReporter">進度回報回調函數</param>
        private static Task SaveWindowSelection(GraphicsCaptureItem item, Action<string>? progressReporter)
        {
            try
            {
                progressReporter?.Invoke("正在保存視窗選擇到記憶中...");

                if (AppConfig.Instance != null)
                {
                    AppConfig.Instance.General.LastSelectedWindowName = item.DisplayName;
                }

                try
                {
                    var process = Process.GetProcesses()
                        .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle) &&
                                   p.MainWindowTitle == item.DisplayName)
                        .FirstOrDefault();

                    if (process != null && AppConfig.Instance != null)
                    {
                        AppConfig.Instance.General.LastSelectedProcessName = process.ProcessName;
                        progressReporter?.Invoke($"已記錄程序資訊: {process.ProcessName}");
                    }
                }
                catch (Exception ex)
                {
                    progressReporter?.Invoke($"程序資訊獲取失敗: {ex.Message}");
                }

                progressReporter?.Invoke("視窗記憶已保存，下次啟動將自動連接");
            }
            catch (Exception ex)
            {
                progressReporter?.Invoke($"保存視窗選擇時發生錯誤: {ex.Message}");
            }

            return Task.CompletedTask;
        }
    }
}
