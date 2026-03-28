using ArtaleAI.Models.Config;
using ArtaleAI.Core.Vision;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Services;
using ArtaleAI.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace ArtaleAI.Core
{
    /// <summary>
    /// 整合的遊戲視覺核心 - 合併 GameDetectionEngine、MapDetector、VisionCore 的功能
    /// </summary>
    public partial class GameVisionCore : IDisposable
    {
        #region 私有字段
        private readonly Dictionary<string, Mat> _mapTemplates = new();
        private readonly Dictionary<string, List<Mat>> _monsterTemplateCache = new();
        private bool _disposed = false;
        private int _lastPixelCount = 0; 
        private readonly object _minimapMatLock = new object();

        // 🚀 效能優化：地圖 ROI 快取
        private Rectangle? _cachedMinimapRect = null;
        private DateTime _lastFullScanTime = DateTime.MinValue;
        private const int FullScanIntervalMs = 2000; // 每 2 秒才強制校正一次（若沒丟失）
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

        #region 血條檢測功能群組

        /// <summary>
        /// 檢測隊友血條位置（主要入口方法）
        /// 使用 HSV 色彩空間檢測紅色血條
        /// </summary>
        /// <param name="frameMat">輸入畫面 Mat</param>
        /// <param name="uiExcludeRect">UI 排除區域（可選）</param>
        /// <param name="config">應用程式設定</param>
        /// <param name="cameraOffsetY">相機垂直偏移量（輸出參數）</param>
        /// <returns>血條矩形區域，未檢測到時返回 null</returns>
        public Rectangle? DetectBloodBar(Mat frameMat, Rectangle? uiExcludeRect, AppConfig config, out int cameraOffsetY)
        {
            // 1. 提取相機區域（內嵌邏輯）
            Mat cameraArea;
            if (uiExcludeRect.HasValue)
            {
                var cameraHeight = uiExcludeRect.Value.Y;
                cameraOffsetY = 0;
                cameraArea = frameMat[new OpenCvSharp.Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
            else
            {
                var totalHeight = frameMat.Height;
                var uiHeight = config.Vision.UiHeightFromBottom;
                var cameraHeight = Math.Max(totalHeight - uiHeight, totalHeight / 2);
                cameraOffsetY = 0;
                cameraArea = frameMat[new OpenCvSharp.Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }

            using (cameraArea)
            {
                // 2. 轉換為 HSV 並創建紅色遮罩（內嵌邏輯）
                using var hsvImage = new Mat();
                Cv2.CvtColor(cameraArea, hsvImage, ColorConversionCodes.BGR2HSV);

                using var redMask = new Mat();
                
                // 紅色在 HSV 空間中分佈在兩端 (0-10 和 160-180)
                // 範圍 1: 低色相
                var lower1 = new Scalar(config.Vision.LowerRedHsv[0], config.Vision.LowerRedHsv[1], config.Vision.LowerRedHsv[2]);
                var upper1 = new Scalar(config.Vision.UpperRedHsv[0], config.Vision.UpperRedHsv[1], config.Vision.UpperRedHsv[2]);
                using var mask1 = new Mat();
                Cv2.InRange(hsvImage, lower1, upper1, mask1);

                // 範圍 2: 高色相 (160-180) - 使用相同的飽和度與明度門檻
                var lower2 = new Scalar(160, config.Vision.LowerRedHsv[1], config.Vision.LowerRedHsv[2]);
                var upper2 = new Scalar(180, 255, 255);
                using var mask2 = new Mat();
                Cv2.InRange(hsvImage, lower2, upper2, mask2);

                // 合併兩個遮罩
                Cv2.BitwiseOr(mask1, mask2, redMask);

                // 3. 找最佳血條（保留，因為邏輯複雜）
                var bestBar = FindBestRedBar(redMask, config);

                //  直接返回轉換後的座標,不需要額外函數
                return bestBar.HasValue
                    ? new Rectangle(bestBar.Value.X, bestBar.Value.Y + cameraOffsetY,
                                   bestBar.Value.Width, bestBar.Value.Height)
                    : null;
            }
        }

        /// <summary>
        /// 完整的血條檢測處理（一次性計算所有相關資訊）
        /// 檢測血條並同時計算檢測框和攻擊範圍框
        /// </summary>
        /// <param name="frameMat">輸入畫面 Mat</param>
        /// <param name="uiExcludeRect">UI 排除區域（可選）</param>
        /// <returns>包含血條、檢測框列表和攻擊範圍框列表的元組</returns>
        public (Rectangle? BloodBar, List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            ProcessBloodBarDetection(Mat frameMat, Rectangle? uiExcludeRect)
        {
            var config = AppConfig.Instance;
            var bloodBar = DetectBloodBar(frameMat, uiExcludeRect, config, out _);

            if (bloodBar.HasValue)
            {
                //  一次計算兩種框架
                var (detectionBoxes, attackRangeBoxes) =
                    CalculateBloodBarRelatedBoxes(bloodBar.Value, config);

                return (bloodBar, detectionBoxes, attackRangeBoxes);
            }

            return (null, new List<Rectangle>(), new List<Rectangle>());
        }


        /// <summary>
        /// 計算血條相關的所有框架（檢測框 + 攻擊範圍框）
        /// 根據血條位置計算怪物檢測區域和角色攻擊範圍
        /// </summary>
        /// <param name="bloodBarRect">血條矩形區域</param>
        /// <param name="config">應用程式設定</param>
        /// <returns>包含檢測框列表和攻擊範圍框列表的元組</returns>
        public (List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            CalculateBloodBarRelatedBoxes(Rectangle bloodBarRect, AppConfig config)
        {
            // 計算偵測框
            var dotCenterX = bloodBarRect.X + bloodBarRect.Width / 2;
            var dotCenterY = bloodBarRect.Y + bloodBarRect.Height + config.Vision.DotOffsetY;

            var detectionBox = new Rectangle(
                dotCenterX - config.Vision.DetectionBoxWidth / 2,
                dotCenterY - config.Vision.DetectionBoxHeight / 2,
                config.Vision.DetectionBoxWidth,
                config.Vision.DetectionBoxHeight
            );

            // 計算攻擊範圍框
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

                // 1. 固定位置模式 (Fixed Mode)
                if (config.Vision.UseFixedMinimapPosition)
                {
                    return new Rectangle(0, 0, config.Vision.FixedMinimapWidth, config.Vision.FixedMinimapHeight);
                }

                // 2. 🚀 快取驗證模式 (ROI Cache Validation)
                var now = DateTime.UtcNow;
                if (_cachedMinimapRect.HasValue && (now - _lastFullScanTime).TotalMilliseconds < FullScanIntervalMs)
                {
                    if (VerifyMinimapAt(fullFrameMat, _cachedMinimapRect.Value))
                    {
                        return _cachedMinimapRect;
                    }
                }

                // 3. 顏色基礎偵測模式 (Color-Based Mode) - 全域掃描
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

                // 採樣 4 個角點
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
            catch { return false; }
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

                // 解析邊框顏色 (BGR)
                var colorParts = config.Vision.MinimapFrameColorBgr.Split(',');
                byte b = byte.Parse(colorParts[0].Trim());
                byte g = byte.Parse(colorParts[1].Trim());
                byte r = byte.Parse(colorParts[2].Trim());
                var frameColor = new Scalar(b, g, r);

                // 1. 建立白色遮罩
                using var maskWhite = new Mat();
                Cv2.InRange(fullFrameMat, frameColor, frameColor, maskWhite);

                // 2. 連通分量分析
                using var labels = new Mat();
                using var stats = new Mat();
                using var centroids = new Mat();
                int numLabels = Cv2.ConnectedComponentsWithStats(maskWhite, labels, stats, centroids, PixelConnectivity.Connectivity8);

                // 3. 遍歷每個連通區域（跳過背景 label 0）
                for (int i = 1; i < numLabels; i++)
                {
                    int x0 = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                    int y0 = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                    int rw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                    int rh = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

                    // 過濾過小的區域
                    if (rw < config.Vision.MinMinimapWidth || rh < config.Vision.MinMinimapHeight)
                        continue;

                    int x1 = x0 + rw - 1;
                    int y1 = y0 + rh - 1;

                    // 4. 驗證四邊是完整的白色邊框 (1px)
                    bool validFrame = true;

                    // 上邊
                    for (int x = x0; x <= x1 && validFrame; x++)
                    {
                        var pixel = fullFrameMat.At<Vec3b>(y0, x);
                        if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                            validFrame = false;
                    }

                    // 下邊
                    for (int x = x0; x <= x1 && validFrame; x++)
                    {
                        var pixel = fullFrameMat.At<Vec3b>(y1, x);
                        if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                            validFrame = false;
                    }

                    // 左邊
                    for (int y = y0; y <= y1 && validFrame; y++)
                    {
                        var pixel = fullFrameMat.At<Vec3b>(y, x0);
                        if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                            validFrame = false;
                    }

                    // 右邊
                    for (int y = y0; y <= y1 && validFrame; y++)
                    {
                        var pixel = fullFrameMat.At<Vec3b>(y, x1);
                        if (pixel.Item0 != b || pixel.Item1 != g || pixel.Item2 != r)
                            validFrame = false;
                    }

                    if (!validFrame)
                        continue;

                    // 5. 找到有效框後，計算內部非白色區域的 BoundingBox
                    using var roiMat = fullFrameMat[new OpenCvSharp.Rect(x0, y0, rw, rh)];
                    using var maskNonWhite = new Mat();
                    Cv2.InRange(roiMat, frameColor, frameColor, maskNonWhite);
                    Cv2.BitwiseNot(maskNonWhite, maskNonWhite); // 反轉：非白色區域為白

                    using var nonZeroMat = new Mat();
                    Cv2.FindNonZero(maskNonWhite, nonZeroMat);
                    if (nonZeroMat.Empty())
                        continue;

                    var innerRect = Cv2.BoundingRect(nonZeroMat);

                    // 轉換回原始座標
                    int minimapX = x0 + innerRect.X;
                    int minimapY = y0 + innerRect.Y;
                    int minimapW = innerRect.Width;
                    int minimapH = innerRect.Height;

                    //Logger.Info($"[小地圖-顏色偵測] 找到小地圖: ({minimapX}, {minimapY}) {minimapW}x{minimapH}");
                    return new Rectangle(minimapX, minimapY, minimapW, minimapH);
                }

                return null; // 沒有符合條件的區域
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
        /// <param name="captureTime">🔧 畫面擷取時間戳，用於精確時間同步</param>
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

                // 保存小地圖 Mat 供 MinimapViewer 使用（線程安全）
                lock (_minimapMatLock)
                {
                    LastMinimapMat?.Dispose();
                    LastMinimapMat = minimapMat.Clone();
                }

                // ✅ 方案1：使用顏色匹配偵測玩家位置（比模板匹配更穩定，不會誤判其他玩家）
                System.Drawing.PointF? playerPos = null;
                try
                {
                    var playerStyle = AppConfig.Instance.Appearance.MinimapPlayer;

                    // 解析玩家顏色 RGB（圖片是 RGB 格式）
                    var colorParts = playerStyle.PlayerColorBgr.Split(',');
                    int r = int.Parse(colorParts[0].Trim());  // 第一個是 R
                    int g = int.Parse(colorParts[1].Trim());  // 第二個是 G  
                    int b = int.Parse(colorParts[2].Trim());  // 第三個是 B
                    int tolerance = playerStyle.ColorTolerance;
                    int minPixels = playerStyle.MinPixelCount;

                    // ✅ 圖片是 BGR 格式，Scalar 順序是 (B, G, R)
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

                    // 執行顏色匹配
                    using var mask = new Mat();
                    Cv2.InRange(minimapMat, lowerBound, upperBound, mask);

                    // 🚀 方案優化：使用影像矩 (Moments) 代替 C# 像素迴圈
                    var moments = Cv2.Moments(mask, true);
                    if (moments.M00 >= minPixels)
                    {
                        // 質心計算公式：x = M10/M00, y = M01/M00
                        float avgX = (float)(moments.M10 / moments.M00);
                        float avgY = (float)(moments.M01 / moments.M00);

                        // 🔧基準點回歸像素中心點並套用垂直偏移
                        playerPos = new System.Drawing.PointF(avgX, avgY + playerStyle.OffsetY);

                        // 只在規模變化較大時輸出（選用性）
                        if (Math.Abs(moments.M00 - _lastPixelCount) > 5)
                        {
                            _lastPixelCount = (int)moments.M00;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[顏色匹配] ⚠️ 像素數不足: {moments.M00:F0} < {minPixels}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[小地圖] 顏色匹配偵測錯誤: {ex.Message}");
                }

                //  直接內嵌其他玩家檢測（使用浮點數座標）
                var otherPlayers = new List<System.Drawing.PointF>();
                if (AppConfig.Instance.Navigation.EnableOtherPlayersDetection == true)
                {
                    try
                    {
                        var threshold = AppConfig.Instance.Navigation.PlayerPositionThreshold;
                        var result = MatchTemplateInternal(minimapMat, "OtherPlayers", threshold);
                        if (result.HasValue)
                        {
                            // 轉換為浮點數座標
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
                    captureTime, // 🔧 使用傳入的擷取時間戳，而非當前時間
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

        #region 綠色遮罩工具

        /// <summary>
        /// 建立綠色背景遮罩
        /// 綠色區域 (0, 255, 0) 會被標記為 0 (忽略)
        /// 非綠色區域會被標記為 255 (用於匹配)
        /// </summary>
        /// <param name="template">模板影像 (BGR 格式)</param>
        /// <returns>遮罩 Mat (與模板相同通道數)，需要調用者負責 Dispose</returns>
        public static Mat CreateGreenMask(Mat template)
        {
            if (template?.Empty() != false) return new Mat();

            try
            {
                // 定義綠色範圍 (BGR 格式)
                // 純綠色 = (0, 255, 0)，容許一些誤差
                var lowerGreen = new Scalar(0, 240, 0);
                var upperGreen = new Scalar(20, 255, 20);

                using var greenMask = new Mat();
                Cv2.InRange(template, lowerGreen, upperGreen, greenMask);

                // 反轉遮罩：綠色區域變 0，非綠色區域變 255
                using var invertedMask = new Mat();
                Cv2.BitwiseNot(greenMask, invertedMask);

                // 🔧 優化：侵蝕 (Erode) 遮罩，減少邊緣雜訊
                // SqDiff 對邊緣非常敏感。怪物邊緣常有「半綠色」的像素 (Anti-aliasing)，
                // 這些像素既不是純綠 (所以沒被 InRange 抓到)，又跟背景不同 (導致 SqDiff 變大)。
                // 我們對白色區域 (有效區域) 進行侵蝕，讓它「內縮」一點，排除掉邊緣。
                // 3x3 的結構元素通常足夠 (迭代 1 次)
                var element = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
                Cv2.Erode(invertedMask, invertedMask, element, null, 1);

                // 🔧 關鍵修復：將單通道遮罩轉換為與模板相同的通道數
                // OpenCV MatchTemplate 需要遮罩與模板有相同的通道數
                var result = new Mat();
                if (template.Channels() == 3)
                {
                    Cv2.Merge(new[] { invertedMask, invertedMask, invertedMask }, result);
                }
                else if (template.Channels() == 4)
                {
                    Cv2.Merge(new[] { invertedMask, invertedMask, invertedMask, invertedMask }, result);
                }
                else
                {
                    result = invertedMask.Clone();
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"[遮罩] 建立綠色遮罩錯誤: {ex.Message}");
                return new Mat();
            }
        }

        #endregion

        #region 怪物檢測功能群組

        /// <summary>
        /// 使用模板匹配尋找怪物
        /// 支援多種檢測模式（彩色、灰階等）
        /// 支援綠色背景遮罩以提高匹配準確度
        /// </summary>
        /// <param name="sourceMat">來源影像 Mat</param>
        /// <param name="templateMats">怪物模板列表</param>
        /// <param name="mode">檢測模式</param>
        /// <param name="threshold">檢測閾值（0.0-1.0）</param>
        /// <param name="monsterName">怪物名稱（用於結果標記）</param>
        /// <param name="templateMasks">模板遮罩列表（可選，與 templateMats 一一對應）</param>
        /// <returns>檢測結果列表</returns>
        public List<DetectionResult> FindMonsters(
            Mat sourceMat,
            List<Mat> templateMats,
            MonsterDetectionMode mode,
            double threshold = 0.7,
            string monsterName = "",
            List<Mat>? templateMasks = null)
        {
            if (templateMats?.Count == 0) return new List<DetectionResult>();

            var allResults = new List<DetectionResult>();

            for (int i = 0; i < templateMats.Count; i++)
            {
                var templateMat = templateMats[i];
                if (templateMat?.Empty() != false) continue;

                // 取得或建立遮罩
                Mat? mask = null;
                bool disposeMask = false;

                if (templateMasks != null && i < templateMasks.Count && templateMasks[i]?.Empty() == false)
                {
                    mask = templateMasks[i];
                }
                else
                {
                    // 動態產生綠色遮罩
                    mask = CreateGreenMask(templateMat);
                    disposeMask = true;
                }

                // 🔧 除錯：記錄遮罩資訊
                if (mask != null && !mask.Empty())
                {
                    Logger.Debug($"[遮罩] 遮罩尺寸: {mask.Width}x{mask.Height}, Channels={mask.Channels()}, 模板尺寸: {templateMat.Width}x{templateMat.Height}");
                }
                else
                {
                    Logger.Warning($"[遮罩] 遮罩建立失敗或為空!");
                }

                try
                {
                    using var result = new Mat();

                    // 🔧 除錯：記錄模板和來源的格式資訊
                    Logger.Debug($"[怪物偵測] 模板: {templateMat.Width}x{templateMat.Height}, Channels={templateMat.Channels()}, 來源: {sourceMat.Width}x{sourceMat.Height}, Channels={sourceMat.Channels()}");

                    if (mode == MonsterDetectionMode.Grayscale)
                    {
                        using var sourceGray = ConvertToGrayscale(sourceMat);
                        using var templateGray = ConvertToGrayscale(templateMat);
                        Cv2.MatchTemplate(sourceGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                    }
                    else
                    {
                        // 彩色匹配使用綠色遮罩（如果可用）
                        if (mask != null && !mask.Empty())
                        {
                            // 使用 SqDiffNormed (歸一化平方差)，支援遮罩且比 CCorr 更嚴格
                            Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed, mask);
                        }
                        else
                        {
                            // 如果沒遮罩，依然可以用 SqDiffNormed，或者維持 CCoeffNormed (但為了邏輯一致建議統一)
                            // 這裡為了保持邏輯統一，全部改用 SqDiffNormed
                            Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.SqDiffNormed);
                        }
                    }

                    if (!result.Empty())
                    {
                        // 🔧 優化：改用 SqDiffNormed (歸一化平方差)

                        // 1. 先取得全局最佳匹配，用於除錯與診斷
                        Cv2.MinMaxLoc(result, out double globalMinVal, out _, out OpenCvSharp.Point globalMinLoc, out _);
                        float globalBestScore = (float)(1.0 - globalMinVal);

                        // 記錄全局最佳狀況，這樣即使低於閾值我們也能知道「最像的地方」分數是多少
                        Logger.Debug($"[怪物偵測] 全局最佳: 分數={globalBestScore:F4} (差異={globalMinVal:F4}), 位置={globalMinLoc}");

                        int count = 0;
                        int maxResults = 5;

                        while (count < maxResults)
                        {
                            // Iterative: 尋找當前最佳
                            Cv2.MinMaxLoc(result, out double minVal, out _, out OpenCvSharp.Point minLoc, out _);

                            // 轉換閾值邏輯
                            if (minVal > (1.0 - threshold))
                            {
                                break;
                            }

                            float matchScore = (float)(1.0 - minVal);

                            // 恢復日誌以便觀察
                            Logger.Debug($"[怪物偵測] 發現匹配: 分數={matchScore:F4} (差異={minVal:F4}), 位置={minLoc}");

                            allResults.Add(new DetectionResult(
                                monsterName,
                                new System.Drawing.Point(minLoc.X, minLoc.Y),
                                new System.Drawing.Size(templateMat.Width, templateMat.Height),
                                matchScore,
                                new Rectangle(minLoc.X, minLoc.Y, templateMat.Width, templateMat.Height)
                            ));

                            count++;

                            // NMS
                            int floodW = templateMat.Width / 2;
                            int floodH = templateMat.Height / 2;
                            var floodRect = new OpenCvSharp.Rect(
                                Math.Max(0, minLoc.X - floodW / 2),
                                Math.Max(0, minLoc.Y - floodH / 2),
                                floodW,
                                floodH
                            );

                            Cv2.Rectangle(result, floodRect, OpenCvSharp.Scalar.All(1.0), -1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[怪物偵測] 模板匹配錯誤: {ex.Message}");
                }
                finally
                {
                    // 釋放動態產生的遮罩
                    if (disposeMask) mask?.Dispose();
                }
            }

            return allResults;
        }

        /// <summary>
        /// 非同步載入怪物模板
        /// 從指定資料夾載入所有模板圖像並轉換為 Mat 格式，支援快取機制
        /// </summary>
        // 用於快取自動估算的閾值


        public async Task<List<Mat>> LoadMonsterTemplatesAsync(string monsterName, string monstersDirectory)
        {
            try
            {
                // 先檢查是否有已載入的模板快取 (Mat)
                if (_monsterTemplateCache.TryGetValue(monsterName, out var cachedMats))
                    return cachedMats;

                string monsterFolderPath = Path.Combine(monstersDirectory, monsterName);
                if (!Directory.Exists(monsterFolderPath))
                    return new List<Mat>();

                var templateFiles = await Task.Run(() => Directory.GetFiles(monsterFolderPath, "*.png"));
                var loadedMatTemplates = new List<Mat>();


                foreach (var file in templateFiles)
                {
                    try
                    {
                        // 1. 讀取圖片
                        using var tempBitmap = new System.Drawing.Bitmap(file);

                        if (tempBitmap == null || tempBitmap.Width < 5 || tempBitmap.Height < 5)
                            continue;

                        using var originalMat = BitmapConverter.ToMat(tempBitmap);

                        // 2. 準備用於匹配的 Mat (BGR 3通道)
                        Mat matForMatching = new Mat();

                        if (originalMat.Channels() == 4)
                        {
                            // BGRA -> BGR (移除 Alpha 通道)
                            Cv2.CvtColor(originalMat, matForMatching, ColorConversionCodes.BGRA2BGR);
                        }
                        else if (originalMat.Channels() == 1)
                        {
                            // 灰階 -> BGR
                            Cv2.CvtColor(originalMat, matForMatching, ColorConversionCodes.GRAY2BGR);
                        }
                        else
                        {
                            // 已經是 BGR (假設)
                            matForMatching = originalMat.Clone();
                        }

                        loadedMatTemplates.Add(matForMatching);

                        // 3. 自動產生水平翻轉版本 (應對怪物面向不同方向)
                        // 使用 FlipMode.Y (Code=1) 進行水平翻轉
                        Mat flippedMat = matForMatching.Clone();
                        Cv2.Flip(flippedMat, flippedMat, FlipMode.Y);
                        loadedMatTemplates.Add(flippedMat);

                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[模板] 載入失敗 {Path.GetFileName(file)}: {ex.Message}");
                    }
                }


                _monsterTemplateCache[monsterName] = loadedMatTemplates;
                return loadedMatTemplates;
            }
            catch (Exception ex)
            {
                Logger.Error($"[模板] LoadMonsterTemplatesAsync 錯誤: {ex.Message}");
                return new List<Mat>();
            }
        }

        #endregion

        #region 私有輔助方法 - 血條相關

        /// <summary>
        /// 從紅色遮罩中找出最佳的血條候選
        /// 使用輪廓檢測和多重條件篩選找出最符合血條特徵的矩形
        /// </summary>
        /// <param name="redMask">紅色二值化遮罩</param>
        /// <param name="config">應用程式設定（包含血條尺寸限制）</param>
        /// <returns>最佳血條矩形，未找到時返回 null</returns>
        private Rectangle? FindBestRedBar(Mat redMask, AppConfig config)
        {
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
                        var boundingRect = Cv2.BoundingRect(contour);
                        var rect = new Rectangle(boundingRect.X, boundingRect.Y,
                            boundingRect.Width, boundingRect.Height);

                        //  內嵌驗證邏輯
                        var width = rect.Width;
                        var height = rect.Height;
                        var area = width * height;

                        if (width >= config.Vision.MinBarWidth &&
                            width <= config.Vision.MaxBarWidth &&
                            height >= config.Vision.MinBarHeight &&
                            height <= config.Vision.MaxBarHeight &&
                            area >= config.Vision.MinBarArea)
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

                var bestCandidate = candidates.OrderByDescending(c => c.area).First();
                return bestCandidate.rect;
            }
            finally
            {
                hierarchy?.Dispose();
                if (contours != null)
                {
                    foreach (var contour in contours)
                    {
                        contour?.Dispose();
                    }
                }
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
                    templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, templatePath);

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

                    // 由於 ScreenCapture 現在回傳 BGR 格式，模板也應該保持 BGR
                    // ImRead(Color) 預設就是 BGR 3通道，直接使用即可
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
                // 忽略解析錯誤，返回預設顏色
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
                // 1. 尋找或確認捕捉目標
                selectedItem = await GetOrSelectCaptureItem(windowHandle, config, selectedItem, progressReporter);
                if (selectedItem == null)
                {
                    progressReporter?.Invoke("未選擇視窗");
                    return null;
                }

                // 2. 建立捕捉器並抓取一幀
                capturer = new GraphicsCapturer(selectedItem);
                await Task.Delay(100);

                //  使用 ResourceManager 安全處理 Mat
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

                //  安全裁切小地圖
                using var minimapMat = new Mat(fullFrame, new OpenCvSharp.Rect(
                    minimapRect.Value.X, minimapRect.Value.Y,
                    minimapRect.Value.Width, minimapRect.Value.Height));

                // 轉換為 Bitmap（OpenCvSharp.ToBitmap 內部會處理 BGR→RGB 轉換）
                var minimapBitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(minimapMat);

                return new MinimapResult(
                    minimapBitmap,           // MinimapImage
                    null,                    // PlayerPosition (如果沒有檢測到設為 null)
                    selectedItem,            // CaptureItem  
                    minimapRect.Value        // MinimapScreenRect
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
                        await SaveWindowSelection(selectedItem, config, progressReporter);
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
        private static async Task SaveWindowSelection(GraphicsCaptureItem item, AppConfig config, Action<string>? progressReporter)
        {
            try
            {
                progressReporter?.Invoke("正在保存視窗選擇到記憶中...");

                // 保存視窗名稱
                if (AppConfig.Instance != null)
                {
                    AppConfig.Instance.General.LastSelectedWindowName = item.DisplayName;
                }

                // 嘗試獲取對應的程序資訊作為備用恢復方式
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
                // 釋放地圖模板
                foreach (var template in _mapTemplates.Values)
                    template?.Dispose();
                _mapTemplates.Clear();

                // 釋放怪物模板
                foreach (var templates in _monsterTemplateCache.Values)
                {
                    foreach (var mat in templates)
                        mat?.Dispose();
                }
                _monsterTemplateCache.Clear();

                // 釋放最後的小地圖 Mat
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
