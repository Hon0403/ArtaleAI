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
        /// 使用顏色特徵偵測小地圖位置（白框連通分量法）
        /// 參考自 MapleStoryAutoLevelUp 專案的 get_minimap_loc_size 函數
        /// </summary>
        /// <param name="fullFrameMat">完整畫面 Mat</param>
        /// <returns>小地圖矩形區域，未找到時返回 null</returns>
        private Rectangle? FindMinimapByColor(Mat fullFrameMat)
        {
            if (fullFrameMat?.Empty() != false) return null;

            try
            {
                var config = AppConfig.Instance;

                var colorParts = config.Vision.MinimapFrameColorBgr.Split(',');
                byte b = byte.Parse(colorParts[0].Trim());
                byte g = byte.Parse(colorParts[1].Trim());
                byte r = byte.Parse(colorParts[2].Trim());
                var frameColor = new Scalar(b, g, r);

                using var maskWhite = new Mat();
                Cv2.InRange(fullFrameMat, frameColor, frameColor, maskWhite);

                using var labels = new Mat();
                using var stats = new Mat();
                using var centroids = new Mat();
                int numLabels = Cv2.ConnectedComponentsWithStats(maskWhite, labels, stats, centroids, PixelConnectivity.Connectivity8);

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
                        var pixel = fullFrameMat.At<Vec3b>(y0, x);
                        if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                            validFrame = false;
                    }

                    for (int x = x0; x <= x1 && validFrame; x++)
                    {
                        var pixel = fullFrameMat.At<Vec3b>(y1, x);
                        if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                            validFrame = false;
                    }

                    for (int y = y0; y <= y1 && validFrame; y++)
                    {
                        var pixel = fullFrameMat.At<Vec3b>(y, x0);
                        if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                            validFrame = false;
                    }

                    for (int y = y0; y <= y1 && validFrame; y++)
                    {
                        var pixel = fullFrameMat.At<Vec3b>(y, x1);
                        if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                            validFrame = false;
                    }

                    if (!validFrame)
                        continue;

                    using var roiMat = fullFrameMat[new OpenCvSharp.Rect(x0, y0, rw, rh)];
                    using var maskNonWhite = new Mat();
                    Cv2.InRange(roiMat, frameColor, frameColor, maskNonWhite);
                    Cv2.BitwiseNot(maskNonWhite, maskNonWhite);

                    using var nonZeroMat = new Mat();
                    Cv2.FindNonZero(maskNonWhite, nonZeroMat);
                    if (nonZeroMat.Empty())
                        continue;

                    var innerRect = Cv2.BoundingRect(nonZeroMat);

                    int minimapX = x0 + innerRect.X;
                    int minimapY = y0 + innerRect.Y;
                    int minimapW = innerRect.Width;
                    int minimapH = innerRect.Height;

                    return new Rectangle(minimapX, minimapY, minimapW, minimapH);
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"[小地圖-顏色偵測] 錯誤: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 完整的小地圖追蹤處理（無時間戳版本，用於 UI 顯示等非關鍵路徑）
        /// </summary>
        /// <param name="fullFrameMat">完整畫面 Mat</param>
        /// <returns>小地圖追蹤結果，失敗時返回 null</returns>
        public MinimapTrackingResult? GetMinimapTracking(Mat fullFrameMat)
        {
            return GetMinimapTracking(fullFrameMat, DateTime.UtcNow);
        }

        /// <summary>
        /// 完整的小地圖追蹤處理
        /// 檢測小地圖位置、玩家位置和其他玩家位置
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

                    var colorParts = playerStyle.PlayerColorBgr.Split(',');
                    int r = int.Parse(colorParts[0].Trim());
                    int g = int.Parse(colorParts[1].Trim());
                    int b = int.Parse(colorParts[2].Trim());
                    int tolerance = playerStyle.ColorTolerance;
                    int minPixels = playerStyle.MinPixelCount;

                    var lowerBound = new Scalar(
                        Math.Max(0, b - tolerance),
                        Math.Max(0, g - tolerance),
                        Math.Max(0, r - tolerance)
                    );
                    var upperBound = new Scalar(
                        Math.Min(255, b + tolerance),
                        Math.Min(255, g + tolerance),
                        Math.Min(255, r + tolerance)
                    );

                    using var mask = new Mat();
                    Cv2.InRange(minimapMat, lowerBound, upperBound, mask);

                    var moments = Cv2.Moments(mask, true);
                    if (moments.M00 >= minPixels)
                    {
                        float avgX = (float)(moments.M10 / moments.M00);
                        float avgY = (float)(moments.M01 / moments.M00);

                        playerPos = new System.Drawing.PointF(avgX, avgY + playerStyle.OffsetY);

                        if (Math.Abs(moments.M00 - _lastPixelCount) > 5)
                        {
                            _lastPixelCount = (int)moments.M00;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[顏色匹配] 像素數不足: {moments.M00:F0} < {minPixels}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[小地圖] 顏色匹配偵測錯誤: {ex.Message}");
                }

                var otherPlayers = new List<System.Drawing.PointF>();
                if (AppConfig.Instance.Navigation.EnableOtherPlayersDetection == true)
                {
                    try
                    {
                        var threshold = AppConfig.Instance.Navigation.PlayerPositionThreshold;
                        var result = MatchTemplateInternal(minimapMat, "OtherPlayers", threshold);
                        if (result.HasValue)
                        {
                            otherPlayers.Add(new System.Drawing.PointF(
                                result.Value.Location.X,
                                result.Value.Location.Y
                            ));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[小地圖] 其他玩家檢測錯誤: {ex.Message}");
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
        /// 內部模板匹配方法
        /// 從快取中取得模板並執行匹配
        /// </summary>
        /// <param name="inputMat">輸入影像</param>
        /// <param name="templateName">模板名稱</param>
        /// <param name="threshold">匹配閾值</param>
        /// <param name="useGrayscale">是否使用灰階模式</param>
        /// <returns>匹配位置和最大值，未找到時返回 null</returns>
        private (System.Drawing.Point Location, double MaxValue)? MatchTemplateInternal(Mat inputMat, string templateName, double threshold, bool useGrayscale = false)
        {
            if (inputMat?.Empty() != false)
                return null;

            if (!_mapTemplates.TryGetValue(templateName, out var template) || template?.Empty() != false)
                return null;

            return MatchTemplate(inputMat, template, threshold, useGrayscale);
        }

        /// <summary>
        /// 載入所有小地圖相關模板
        /// 包括玩家標記、其他玩家標記和四個角點模板
        /// </summary>
        private void LoadMapTemplates()
        {
            var config = AppConfig.Instance;
            var templatePaths = new Dictionary<string, string>
            {
                ["PlayerMarker"] = config.Vision.PlayerMarker,
                ["OtherPlayers"] = config.Vision.OtherPlayers,
                ["TopLeft"] = config.Vision.TopLeft,
                ["TopRight"] = config.Vision.TopRight,
                ["BottomLeft"] = config.Vision.BottomLeft,
                ["BottomRight"] = config.Vision.BottomRight
            };

            foreach (var kvp in templatePaths)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;

                var templatePath = kvp.Value;
                if (!Path.IsPathRooted(templatePath))
                    templatePath = Path.Combine(PathManager.ContentRoot, templatePath);

                if (!File.Exists(templatePath))
                {
                    Debug.WriteLine($"模板檔案不存在: {kvp.Key} -> {templatePath}");
                    continue;
                }

                try
                {
                    using var originalTemplate = Cv2.ImRead(templatePath, ImreadModes.Color);
                    if (originalTemplate.Empty())
                    {
                        Logger.Warning($"[模板] 無法載入模板: {kvp.Key}");
                        continue;
                    }

                    Mat finalTemplate = originalTemplate.Clone();



                    _mapTemplates[kvp.Key] = finalTemplate;
                    Logger.Debug($"[模板] 成功載入模板: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[模板] 載入模板失敗: {kvp.Key} - {ex.Message}");
                }
            }
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
                        AppConfig.Instance.General.LastSelectedProcessId = process.Id;
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
