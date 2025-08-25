using ArtaleAI.Config;
using ArtaleAI.Detection;
using ArtaleAI.GameCapture;
using ArtaleAI.Interfaces;
using ArtaleAI.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        /// <summary>
        /// 渲染所有疊加層 - 統一處理
        /// </summary>
        private void RenderAllOverlays()
        {
            if (_currentFrameMat != null && !_currentFrameMat.IsDisposed)
            {
                var baseBitmap = _currentFrameMat.ToBitmap();
                // 檢查是否有任何疊加層需要渲染
                if (_currentMonsterItems.Any() || _currentMinimapItems.Any() ||
                    _currentPlayerItems.Any() || _currentPartyRedBarItems.Any())
                {
                    var bitmap = OverlayRenderer.RenderOverlays(
                        baseBitmap,
                        _currentMonsterItems,
                        _currentMinimapItems,
                        _currentPlayerItems,
                        _currentPartyRedBarItems
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

        public void OnFrameAvailable(Bitmap frame)
        {
            if (frame == null) return;

            try
            {
                // ✅ 立即更新顯示（創建顯示副本）
                var displayFrame = new Bitmap(frame);
                UpdateDisplaySafely(displayFrame);

                // ✅ 更新內部Mat（用於疊加層渲染）
                lock (_frameLock)
                {
                    _currentFrameMat?.Dispose();
                    using var tempMat = ImageUtils.BitmapToThreeChannelMat(frame);
                    _currentFrameMat = tempMat.Clone();
                }

                // ✅ 保持並行處理，直接傳入原始frame
                if (_config != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 🔧 關鍵：直接傳入原始frame，不創建副本
                            await _playerDetector?.ProcessFrameAsync(frame, _currentMinimapRect);
                        }
                        catch (Exception ex)
                        {
                            OnError($"血條檢測錯誤: {ex.Message}");
                        }
                    });

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 🔧 關鍵：直接傳入原始frame，不創建副本
                            await _monsterService?.ProcessFrameAsync(frame, _config);
                        }
                        catch (Exception ex)
                        {
                            OnError($"怪物檢測錯誤: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                OnError($"幀處理錯誤: {ex.Message}");
            }

            // 🔧 不要在這裡釋放原始frame，讓調用者處理
        }

        /// <summary>
        /// 解析辨識模式字串
        /// </summary>
        private MonsterDetectionMode ParseDetectionMode(string modeString)
        {
            return modeString switch
            {
                "Basic" => MonsterDetectionMode.Basic,
                "ContourOnly" => MonsterDetectionMode.ContourOnly,
                "Grayscale" => MonsterDetectionMode.Grayscale,
                "Color" => MonsterDetectionMode.Color,
                "TemplateFree" => MonsterDetectionMode.TemplateFree,
                _ => MonsterDetectionMode.Color // 預設值
            };
        }


        /// <summary>
        /// 事件處理：怪物識別結果
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
                RenderAllOverlays();
            }
        }

        /// <summary>
        /// 事件處理：血條識別結果
        /// </summary>
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
                if (redBarStyle != null)
                {
                    _currentPartyRedBarItems = redBarRects.Select(rect =>
                        new OverlayRenderer.PartyRedBarRenderItem(redBarStyle)
                        {
                            BoundingBox = rect
                        }).ToList();
                }
                else
                {
                    _currentPartyRedBarItems.Clear();
                }
                RenderAllOverlays();
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

            // 🔧 確保返回的是三通道處理後的結果
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
