using ArtaleAI.Config;
using ArtaleAI.Detection;
using ArtaleAI.GameCapture;
using ArtaleAI.Interfaces;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using static ArtaleAI.Display.OverlayRenderer;

namespace ArtaleAI.Display
{
    public class LiveViewController : IDisposable
    {
        private readonly TextBox _statusTextBox;
        private readonly IMainFormEvents _eventHandler;
        private readonly PictureBox _displayPictureBox;
        private readonly LiveViewService _liveViewService;

        private Mat? _currentFrameMat;
        private readonly object _frameLock = new object();

        private List<OverlayRenderer.MonsterRenderItem> _currentMonsterItems = new();
        private List<OverlayRenderer.MinimapRenderItem> _currentMinimapItems = new();
        private List<OverlayRenderer.PlayerRenderItem> _currentPlayerItems = new();
        private List<OverlayRenderer.PartyRedBarRenderItem> _currentPartyRedBarItems = new();
        private List<DetectionBoxRenderItem> _currentDetectionBoxItems = new();
        private List<Rectangle> _currentDetectionBoxes = new();

        private Rectangle? _currentMinimapRect;
        private PlayerDetector? _playerDetector;
        private AppConfig? _config;
        private MonsterService? _monsterService;

        public Rectangle? GetMinimapRect() => _currentMinimapRect;
        public bool IsRunning => _liveViewService.IsRunning;

        public LiveViewController(TextBox statusTextBox, IMainFormEvents eventHandler, PictureBox pictureBox)
        {
            _statusTextBox = statusTextBox ?? throw new ArgumentNullException(nameof(statusTextBox));
            _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
            _displayPictureBox = pictureBox ?? throw new ArgumentNullException(nameof(pictureBox));
            _liveViewService = new LiveViewService(_eventHandler);
        }

        public void SetConfig(AppConfig config)
        {
            _config = config;
            _playerDetector = new PlayerDetector(_config);

            // 訂閱事件
            _playerDetector.BloodBarDetected += OnBloodBarDetected;
            _playerDetector.StatusMessage += _eventHandler.OnStatusMessage;
        }

        public void SetMonsterService(MonsterService? monsterService)
        {
            // 取消訂閱舊事件
            if (_monsterService != null)
            {
                _monsterService.MonsterDetected -= OnMonsterDetected;
            }

            _monsterService = monsterService;

            // 訂閱新事件
            if (_monsterService != null)
            {
                _monsterService.MonsterDetected += OnMonsterDetected;
            }
        }

        public async Task StartAsync(AppConfig config)
        {
            await _liveViewService.StartAsync(config);
        }

        public async Task StopAsync()
        {
            await _liveViewService.StopAsync();
            lock (_frameLock)
            {
                _currentFrameMat?.Dispose();
                _currentFrameMat = null;
            }
        }

        /// <summary>
        /// 更新小地圖疊加層 - 使用配置化樣式
        /// </summary>
        public void UpdateMinimapOverlay(Bitmap minimap, Rectangle minimapOnScreenRect, Rectangle playerRectInMinimap)
        {
            lock (_frameLock)
            {
                _currentMinimapRect = minimapOnScreenRect;
                var minimapStyle = _config?.OverlayStyle?.Minimap;
                var playerStyle = _config?.OverlayStyle?.Player;

                _currentMinimapItems.Clear();
                if (minimap != null && minimapStyle != null)
                {
                    _currentMinimapItems.Add(new OverlayRenderer.MinimapRenderItem(minimapStyle)
                    {
                        BoundingBox = minimapOnScreenRect
                    });
                }

                // 更新玩家位置項目
                _currentPlayerItems.Clear();
                if (!playerRectInMinimap.IsEmpty && playerStyle != null)
                {
                    // 計算玩家在大畫面上的實際位置
                    var playerOnScreen = new Rectangle(
                        minimapOnScreenRect.X + (int)(playerRectInMinimap.X *
                            (double)minimapOnScreenRect.Width / minimap.Width),
                        minimapOnScreenRect.Y + (int)(playerRectInMinimap.Y *
                            (double)minimapOnScreenRect.Height / minimap.Height),
                        Math.Max(8, (int)(playerRectInMinimap.Width *
                            (double)minimapOnScreenRect.Width / minimap.Width)),
                        Math.Max(8, (int)(playerRectInMinimap.Height *
                            (double)minimapOnScreenRect.Height / minimap.Height))
                    );

                    _currentPlayerItems.Add(new OverlayRenderer.PlayerRenderItem(playerStyle)
                    {
                        BoundingBox = playerOnScreen
                    });
                }

                RenderAllOverlays();
            }
        }

        public void OnFrameAvailable(Bitmap frame)
        {
            if (frame == null) return;

            try
            {
                // 🔧 關鍵修復：在方法開始就創建所有需要的副本
                Bitmap displayFrame, matFrame, playerFrame, monsterFrame;

                // 使用 lock 確保副本創建過程的執行緒安全
                lock (frame)
                {
                    displayFrame = new Bitmap(frame);
                    matFrame = new Bitmap(frame);
                    playerFrame = new Bitmap(frame);
                    monsterFrame = new Bitmap(frame);
                }

                // 立即處理顯示
                UpdateDisplaySafely(displayFrame);

                // 處理 Mat 轉換
                lock (_frameLock)
                {
                    _currentFrameMat?.Dispose();
                    using var tempMat = ImageUtils.BitmapToThreeChannelMat(matFrame);
                    _currentFrameMat = tempMat.Clone();
                }
                matFrame.Dispose(); // 立即釋放

                if (_config != null)
                {
                    // 血條檢測 - 使用已創建的副本
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using (playerFrame)
                            {
                                await _playerDetector?.ProcessFrameAsync(playerFrame, _currentMinimapRect);
                            }
                        }
                        catch (Exception ex)
                        {
                            OnError($"血條檢測錯誤: {ex.Message}");
                        }
                    });

                    // 怪物檢測 - 使用已創建的副本
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using (monsterFrame)
                            {
                                var detectionBoxes = GetCurrentDetectionBoxes();
                                await _monsterService?.ProcessFrameAsync(monsterFrame, _config, detectionBoxes);
                            }
                        }
                        catch (Exception ex)
                        {
                            OnError($"怪物檢測錯誤: {ex.Message}");
                        }
                    });
                }
                else
                {
                    // 如果不需要處理，記得釋放副本
                    playerFrame?.Dispose();
                    monsterFrame?.Dispose();
                }
            }
            catch (Exception ex)
            {
                OnError($"幀處理錯誤: {ex.Message}");
            }
            finally
            {
                // 🔧 重要：釋放原始 frame
                frame?.Dispose();
            }
        }

        /// <summary>
        /// 渲染所有疊加層 - 統一處理
        /// </summary>
        private void RenderAllOverlays()
        {
            Mat? frameMatCopy = null;

            lock (_frameLock)
            {
                if (_currentFrameMat != null && !_currentFrameMat.IsDisposed)
                {
                    frameMatCopy = _currentFrameMat.Clone(); // 創建安全副本
                }
            }

            if (frameMatCopy != null)
            {
                try
                {
                    using (frameMatCopy)
                    {
                        var baseBitmap = frameMatCopy.ToBitmap();

                        if (_currentMonsterItems.Any() || _currentMinimapItems.Any() ||
                            _currentPlayerItems.Any() || _currentPartyRedBarItems.Any() ||
                            _currentDetectionBoxItems.Any())
                        {
                            var bitmap = OverlayRenderer.RenderOverlays(
                                baseBitmap,
                                _currentMonsterItems,
                                _currentMinimapItems,
                                _currentPlayerItems,
                                _currentPartyRedBarItems,
                                _currentDetectionBoxItems
                            );

                            UpdateDisplaySafely(bitmap);
                            baseBitmap.Dispose();
                        }
                        else
                        {
                            UpdateDisplaySafely(baseBitmap);
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnError($"渲染疊加層失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 怪物識別結果
        /// </summary>
        private void OnMonsterDetected(List<MonsterRenderInfo> renderInfos)
        {
            if (_displayPictureBox.InvokeRequired)
            {
                _displayPictureBox.BeginInvoke(() => OnMonsterDetected(renderInfos));
                return;
            }

            lock (_frameLock)
            {
                var monsterStyle = _config?.OverlayStyle?.Monster;
                if (monsterStyle != null)
                {
                    _currentMonsterItems = OverlayRenderer.FromMonsterRenderInfos(renderInfos, monsterStyle);
                }
                else
                {
                    _currentMonsterItems.Clear();
                }
            }

            RenderAllOverlays();
        }

        ///
        /// 血條識別結果
        ///
        private void OnBloodBarDetected(List<Rectangle> redBarRects)
        {
            if (_displayPictureBox.InvokeRequired)
            {
                _displayPictureBox.BeginInvoke(() => OnBloodBarDetected(redBarRects));
                return;
            }

            lock (_frameLock)
            {
                var redBarStyle = _config?.OverlayStyle?.PartyRedBar;
                var detectionBoxStyle = _config?.OverlayStyle?.DetectionBox;

                if (redBarStyle != null && detectionBoxStyle != null)
                {
                    // 創建血條框線
                    _currentPartyRedBarItems = redBarRects.Select(rect =>
                        new OverlayRenderer.PartyRedBarRenderItem(redBarStyle)
                        {
                            BoundingBox = rect
                        }).ToList();

                    // 🆕 創建檢測框並保存位置
                    _currentDetectionBoxes.Clear(); // 清空舊的檢測框
                    _currentDetectionBoxItems = redBarRects.Select(rect =>
                    {
                        var dotCenterX = rect.X + rect.Width / 2;
                        var dotCenterY = rect.Y + rect.Height + (_config?.PartyRedBar?.DotOffsetY ?? 10);
                        var boxWidth = _config?.PartyRedBar?.DetectionBoxWidth ?? 100;
                        var boxHeight = _config?.PartyRedBar?.DetectionBoxHeight ?? 80;

                        var detectionBox = new Rectangle(
                            dotCenterX - boxWidth / 2,
                            dotCenterY - boxHeight / 2,
                            boxWidth,
                            boxHeight);

                        // 🆕 保存檢測框位置供怪物辨識使用
                        _currentDetectionBoxes.Add(detectionBox);

                        return new OverlayRenderer.DetectionBoxRenderItem(detectionBoxStyle)
                        {
                            BoundingBox = detectionBox
                        };
                    }).ToList();
                }
                else
                {
                    _currentPartyRedBarItems.Clear();
                    _currentDetectionBoxItems.Clear();
                    _currentDetectionBoxes.Clear(); // 🆕
                }
            }

            RenderAllOverlays();
        }

        /// <summary>
        /// 🆕 獲取當前檢測框列表
        /// </summary>
        public List<Rectangle> GetCurrentDetectionBoxes()
        {
            lock (_frameLock)
            {
                return _currentDetectionBoxes.ToList();
            }
        }

        public void OnStatusMessage(string message)
        {
            try
            {
                if (_statusTextBox.InvokeRequired)
                {
                    _statusTextBox.Invoke(new Action<string>(AppendStatusMessage), message);
                }
                else
                {
                    AppendStatusMessage(message);
                }
            }
            catch (Exception) { }
        }

        public void OnError(string errorMessage)
        {
            try
            {
                if (_displayPictureBox.InvokeRequired)
                {
                    _displayPictureBox.Invoke(new Action<string>(ShowErrorMessage), errorMessage);
                }
                else
                {
                    ShowErrorMessage(errorMessage);
                }
            }
            catch (Exception) { }
        }

        public Bitmap? GetCurrentCaptureFrame()
        {
            Mat? frameCopy = null;
            lock (_frameLock)
            {
                frameCopy = _currentFrameMat?.Clone();
            }

            return frameCopy?.ToBitmap();
        }

        /// <summary>
        /// 安全更新 PictureBox 顯示
        /// </summary>
        private void UpdateDisplaySafely(Bitmap newFrame)
        {
            if (_displayPictureBox.InvokeRequired)
            {
                _displayPictureBox.BeginInvoke(() =>
                {
                    if (!_displayPictureBox.IsDisposed)
                    {
                        var oldFrame = _displayPictureBox.Image;
                        _displayPictureBox.Image = newFrame;
                        oldFrame?.Dispose();
                    }
                    else
                    {
                        newFrame?.Dispose();
                    }
                });
            }
            else
            {
                if (!_displayPictureBox.IsDisposed)
                {
                    var oldFrame = _displayPictureBox.Image;
                    _displayPictureBox.Image = newFrame;
                    oldFrame?.Dispose();
                }
                else
                {
                    newFrame?.Dispose();
                }
            }
        }

        private void AppendStatusMessage(string message)
        {
            if (_statusTextBox.IsDisposed) return;
            _statusTextBox.AppendText($"{DateTime.Now:HH:mm:ss} - {message}\r\n");
            _statusTextBox.ScrollToCaret();
        }

        private void ShowErrorMessage(string errorMessage)
        {
            AppendStatusMessage($"❌ {errorMessage}");
            MessageBox.Show(errorMessage, "即時顯示發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public void Dispose()
        {
            // 取消事件訂閱
            if (_playerDetector != null)
            {
                _playerDetector.BloodBarDetected -= OnBloodBarDetected;
                _playerDetector.StatusMessage -= _eventHandler.OnStatusMessage;
            }

            if (_monsterService != null)
            {
                _monsterService.MonsterDetected -= OnMonsterDetected;
            }

            lock (_frameLock)
            {
                _currentFrameMat?.Dispose();
            }

            _liveViewService?.Dispose();
        }
    }
}
