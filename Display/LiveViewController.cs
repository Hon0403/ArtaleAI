using ArtaleAI.Config;
using ArtaleAI.Detection;
using ArtaleAI.GameCapture;
using ArtaleAI.Interfaces;
using OpenCvSharp;
using OpenCvSharp.Extensions;


namespace ArtaleAI.Display
{
    public class LiveViewController :  IDisposable
    {
        private readonly TextBox _statusTextBox;
        private readonly IMainFormEvents _eventHandler;
        private readonly Control _parentControl;
        private readonly LiveViewService _liveViewService;
        private readonly PictureBox _displayPictureBox;
        private Mat? _currentFrameMat;
        private readonly object _frameLock = new object();

        private List<OverlayRenderer.MonsterRenderItem> _currentMonsterItems = new();
        private List<OverlayRenderer.MinimapRenderItem> _currentMinimapItems = new();
        private List<OverlayRenderer.PlayerRenderItem> _currentPlayerItems = new();
        private List<OverlayRenderer.PartyRedBarRenderItem> _currentPartyRedBarItems = new(); 
        private Rectangle? _currentMinimapRect;

        public Rectangle? GetMinimapRect() => _currentMinimapRect;

        private PlayerDetector? _playerDetector;
        private AppConfig? _config;

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
        /// 更新怪物辨識框 - 使用配置化樣式
        /// </summary>
        public void DrawMonsterRectangles(List<MonsterRenderInfo> renderInfos)
        {
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
                RenderAllOverlays();
            }
        }

        /// <summary>
        /// 更新隊友血條辨識框 - 使用配置化樣式
        /// </summary>
        public void DrawPartyRedBarRectangles(List<Rectangle> redBarRects)
        {
            lock (_frameLock)
            {
                var redBarStyle = _config?.OverlayStyle?.PartyRedBar;
                if (redBarStyle != null)
                {
                    _currentPartyRedBarItems = redBarRects?.Select(rect => new OverlayRenderer.PartyRedBarRenderItem(redBarStyle)
                    {
                        BoundingBox = rect
                    }).ToList() ?? new List<OverlayRenderer.PartyRedBarRenderItem>();
                }
                else
                {
                    _currentPartyRedBarItems.Clear();
                }

                RenderAllOverlays();
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

        /// <summary>
        /// 渲染所有疊加層 - 統一處理
        /// </summary>
        private void RenderAllOverlays()
        {
            if (_currentFrameMat != null && !_currentFrameMat.IsDisposed)
            {
                var baseBitmap = _currentFrameMat.ToBitmap();

                // 🔧 檢查是否有任何疊加層需要渲染（包含血條）
                if (_currentMonsterItems.Any() || _currentMinimapItems.Any() ||
                    _currentPlayerItems.Any() || _currentPartyRedBarItems.Any())
                {
                    var bitmap = OverlayRenderer.RenderOverlays(
                        baseBitmap,
                        _currentMonsterItems,
                        _currentMinimapItems,
                        _currentPlayerItems,
                        _currentPartyRedBarItems // 🔧 新增血條渲染
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

        #region ILiveViewEventHandler 實作

        public void OnFrameAvailable(Bitmap frame)
        {
            try
            {
                lock (_frameLock)
                {
                    // 轉換為 Mat 格式
                    _currentFrameMat?.Dispose();
                    _currentFrameMat = frame.ToMat();
                }

                // 🔧 每次新幀都重新渲染所有疊加層
                if (_currentFrameMat != null)
                {
                    RenderAllOverlays();
                }
            }
            catch (Exception ex)
            {
                OnError($"顯示幀時發生錯誤: {ex.Message}");
            }
            finally
            {
                frame?.Dispose();
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
                if (_parentControl.InvokeRequired)
                {
                    _parentControl.Invoke(new Action<string>(ShowErrorMessage), errorMessage);
                }
                else
                {
                    ShowErrorMessage(errorMessage);
                }
            }
            catch (Exception) { }
        }

        #endregion

        public Bitmap? GetCurrentCaptureFrame()
        {
            Mat? frameCopy = null;
            lock (_frameLock)
            {
                // 只在鎖內複製引用，最小化鎖時間
                frameCopy = _currentFrameMat?.Clone();
            }

            return frameCopy?.ToBitmap();
        }

        public (System.Drawing.Point? playerLocation, System.Drawing.Point? redBarLocation, Rectangle? redBarRect) DetectPlayerPosition(Bitmap? frame, Rectangle? minimapRect = null)
        {
            if (_playerDetector == null || frame == null)
                return (null, null, null);

            var actualMinimapRect = minimapRect ?? _currentMinimapRect;

            return _playerDetector.GetPlayerLocationByPartyRedBar(frame, actualMinimapRect);
        }

        /// <summary>
        /// 安全更新 PictureBox 顯示
        /// </summary>
        private void UpdateDisplaySafely(Bitmap newFrame)
        {
            if (_displayPictureBox.InvokeRequired)
            {
                _displayPictureBox.Invoke(() =>
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
            lock (_frameLock)
            {
                _currentFrameMat?.Dispose();
            }

            _playerDetector?.Dispose();
            _liveViewService?.Dispose();
        }
    }
}
