using ArtaleAI.Config;
using ArtaleAI.Core;
using ArtaleAI.GameCapture;
using ArtaleAI.Minimap;
using ArtaleAI.Monster;
using ArtaleAI.UI;
using ArtaleAI.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Capture;
using System.Runtime.InteropServices;

namespace ArtaleAI
{
    public partial class MainForm : Form,
        IConfigEventHandler,           // 配置管理
        IMapFileEventHandler,          // 地圖檔案管理  
        IApplicationEventHandler       // 統一應用事件
    {

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeLibrary(IntPtr hModule);

        #region Private Fields

        private ConfigManager? _configurationManager;
        public ConfigManager? ConfigurationManager => _configurationManager;

        private readonly MinimapEditor _editorMinimap = new();
        private GraphicsCaptureItem? _selectedCaptureItem;
        private readonly MapEditor _mapEditor = new();
        private readonly MapData _mapData = new();
        private LiveViewController? _liveViewController;
        private FloatingMagnifier? _floatingMagnifier;
        private MonsterService? _monsterService;
        private MapFileManager? _mapFileManager;

        private System.Threading.Timer? _backgroundMonsterTimer;
        private bool _isMonsterDetectionRunning = false;
        private readonly object _detectionLock = new object();

        #endregion

        #region Constructor & Initialization

        public MainForm()
        {
            InitializeComponent();
            InitializeServices();
            BindEvents();
            InitializeTimer();

            var diagnosisTimer = new System.Windows.Forms.Timer
            {
                Interval = 500,
                Enabled = true
            };
            diagnosisTimer.Tick += (s, e) =>
            {
                diagnosisTimer.Stop();
                diagnosisTimer.Dispose();
                ComprehensiveOpenCvDiagnosis();
            };

        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            ComprehensiveOpenCvDiagnosis();
        }

        private void InitializeServices()
        {
            // 配置管理統一化
            _configurationManager = new ConfigManager(this);
            _configurationManager.Load();

            // 初始化各項服務
            _liveViewController = new LiveViewController(pictureBoxLiveView, textBox1, this);
            _floatingMagnifier = new FloatingMagnifier(this);

            _monsterService = new MonsterService(cbo_MonsterTemplates, this);
            _monsterService.InitializeMonsterDropdown();

            _mapFileManager = new MapFileManager(cbo_MapFiles, _mapEditor, this);
            _mapFileManager.InitializeMapFilesDropdown();

        }

        private void InitializeTimer()
        {
            _backgroundMonsterTimer = new System.Threading.Timer(
                ProcessMonsterDetectionBackground,
                null,
                Timeout.Infinite,
                Timeout.Infinite
            );

            OnStatusMessage("背景 Timer 初始化完成");
        }


        private void BindEvents()
        {
            // UI 控制項事件綁定
            tabControl1.SelectedIndexChanged += TabControl1_SelectedIndexChanged;
            numericUpDownZoom.ValueChanged += numericUpDownZoom_ValueChanged;

            // 地圖編輯模式事件
            rdo_PathMarker.CheckedChanged += OnEditModeChanged;
            rdo_SafeZone.CheckedChanged += OnEditModeChanged;
            rdo_RestrictedZone.CheckedChanged += OnEditModeChanged;
            rdo_RopeMarker.CheckedChanged += OnEditModeChanged;
            rdo_DeleteMarker.CheckedChanged += OnEditModeChanged;

            // 小地圖滑鼠事件
            pictureBoxMinimap.Paint += pictureBoxMinimap_Paint;
            pictureBoxMinimap.MouseDown += pictureBoxMinimap_MouseDown;
            pictureBoxMinimap.MouseUp += pictureBoxMinimap_MouseUp;
            pictureBoxMinimap.MouseMove += pictureBoxMinimap_MouseMove;
            pictureBoxMinimap.MouseLeave += pictureBoxMinimap_MouseLeave;
            pictureBoxMinimap.MouseClick += pictureBoxMinimap_MouseClick;

            // 按鈕事件
            btn_SaveMap.Click += btn_SaveMap_Click;
            btn_New.Click += btn_New_Click;

        }

        #endregion

        #region IConfigEventHandler 實作

        public void OnConfigLoaded(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigLoaded), config);
                return;
            }

            numericUpDownZoom.Value = config.General?.ZoomFactor ?? 15;
            OnStatusMessage("配置檔案載入完成");
        }

        public void OnConfigSaved(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigSaved), config);
                return;
            }

            OnStatusMessage("設定已儲存");
        }

        public void OnConfigError(string errorMessage)
        {
            OnError($"設定錯誤: {errorMessage}");
        }

        #endregion

        #region IMapFileEventHandler 實作

        public string GetMapDataDirectory() => PathUtils.GetMapDataDirectory();

        public void OnMapLoaded(string mapFileName)
        {
            OnStatusMessage($"成功載入地圖: {mapFileName}");
        }

        public void OnMapSaved(string mapFileName, bool isNewFile)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, bool>(OnMapSaved), mapFileName, isNewFile);
                return;
            }

            string message = isNewFile ? "新地圖儲存成功！" : "儲存成功！";
            MessageBox.Show(message, "地圖檔案管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OnStatusMessage($"地圖儲存: {mapFileName}");
        }

        public void OnNewMapCreated()
        {
            OnStatusMessage("已建立新地圖");
        }

        public void UpdateWindowTitle(string title)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(UpdateWindowTitle), title);
                return;
            }

            this.Text = title;
        }

        public void RefreshMinimap()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(RefreshMinimap));
                return;
            }

            pictureBoxMinimap.Invalidate();
        }

        #endregion

        #region IApplicationEventHandler 實作

        // 放大鏡功能
        public Bitmap? GetSourceImage() => pictureBoxMinimap.Image as Bitmap;

        public decimal GetZoomFactor() =>
            _configurationManager?.CurrentConfig?.General?.ZoomFactor ?? numericUpDownZoom.Value;

        public Point? ConvertToImageCoordinates(Point mouseLocation) =>
            _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, mouseLocation);

        // 怪物模板功能
        public string GetMonstersDirectory() => PathUtils.GetMonstersDirectory();

        public void OnTemplatesLoaded(string monsterName, int templateCount)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, int>(OnTemplatesLoaded), monsterName, templateCount);
                return;
            }

            OnStatusMessage($"成功載入 {templateCount} 個 '{monsterName}' 的模板");
        }

        // 即時顯示功能
        public void OnFrameAvailable(Bitmap frame)
        {
            // 這個方法會由 LiveViewController 內部處理
            // 保留此實作以符合介面需求
        }

        #endregion

        #region 統一狀態訊息處理

        public void OnStatusMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnStatusMessage), message);
                return;
            }

            textBox1.AppendText($"{DateTime.Now:HH:mm:ss} - {message}\r\n");
            textBox1.ScrollToCaret();
        }

        public void OnError(string errorMessage)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnError), errorMessage);
                return;
            }

            textBox1.AppendText($"{DateTime.Now:HH:mm:ss} - ❌ {errorMessage}\r\n");
            textBox1.ScrollToCaret();
            MessageBox.Show(errorMessage, "發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        #endregion

        #region UI 事件處理

        private void numericUpDownZoom_ValueChanged(object? sender, EventArgs e)
        {
            _configurationManager?.SetValue(cfg =>
            {
                if (cfg.General != null)
                    cfg.General.ZoomFactor = numericUpDownZoom.Value;
            }, autoSave: true);
        }

        private async void TabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_liveViewController != null && _liveViewController.IsRunning)
                await _liveViewController.StopAsync();

            _backgroundMonsterTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            switch (tabControl1.SelectedIndex)
            {
                case 1:
                    await UpdateMinimapSnapshotAsync();
                    break;
                case 2:
                    var config = _configurationManager?.CurrentConfig ?? new AppConfig();
                    await _liveViewController.StartAsync(config);
                    await Task.Delay(3000);
                    _backgroundMonsterTimer?.Change(0, 100); // 100ms 間隔
                    break;
            }
        }

        private async Task UpdateMinimapSnapshotAsync()
        {
            tabControl1.Enabled = false;
            pictureBoxMinimap.Image?.Dispose();
            pictureBoxMinimap.Image = null;

            OnStatusMessage("正在載入小地圖快照...");

            try
            {
                var config = _configurationManager?.CurrentConfig ?? new AppConfig();
                Action<string> reporter = message => OnStatusMessage(message);

                var result = await _editorMinimap.LoadSnapshotAsync(this.Handle, config, reporter);

                if (result?.MinimapImage != null)
                {
                    pictureBoxMinimap.Image = result.MinimapImage;
                    OnStatusMessage("小地圖快照載入成功");

                    // 更新選中的捕捉項目
                    _selectedCaptureItem = result.CaptureItem;
                }
                else
                {
                    OnStatusMessage("小地圖載入操作已取消或失敗");
                }
            }
            catch (Exception ex)
            {
                OnError($"載入小地圖時發生錯誤: {ex.Message}");
                MessageBox.Show(ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                tabControl1.Enabled = true;
            }
        }

        #endregion

        #region 地圖編輯事件

        private void OnEditModeChanged(object? sender, EventArgs e)
        {
            if (sender is not RadioButton checkedButton || !checkedButton.Checked)
                return;

            EditMode selectedMode = checkedButton.Name switch
            {
                nameof(rdo_PathMarker) => EditMode.Waypoint,
                nameof(rdo_SafeZone) => EditMode.SafeZone,
                nameof(rdo_RestrictedZone) => EditMode.RestrictedZone,
                nameof(rdo_RopeMarker) => EditMode.Rope,
                nameof(rdo_DeleteMarker) => EditMode.Delete,
                _ => EditMode.None
            };

            _mapEditor.SetEditMode(selectedMode);
            pictureBoxMinimap.Invalidate();

            OnStatusMessage($"編輯模式切換至: {selectedMode}");
        }

        #endregion

        #region PictureBox 滑鼠事件

        private void pictureBoxMinimap_MouseDown(object sender, MouseEventArgs e)
        {
            var imgPoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (!imgPoint.HasValue) return;

            if (e.Button == MouseButtons.Left)
                _mapEditor.HandleMouseClick(imgPoint.Value);
            else if (e.Button == MouseButtons.Right)
                _mapEditor.HandleRightClick();

            pictureBoxMinimap.Invalidate();
        }

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            // 更新放大鏡
            _floatingMagnifier?.UpdateMagnifier(e.Location, pictureBoxMinimap);

            // 更新地圖編輯器的滑鼠位置
            var imgPoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (imgPoint.HasValue)
            {
                _mapEditor.HandleMouseMove(imgPoint.Value);
                pictureBoxMinimap.Invalidate();
            }
        }

        private void pictureBoxMinimap_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                pictureBoxMinimap.Invalidate();
        }

        private void pictureBoxMinimap_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            var imgPoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (imgPoint.HasValue)
            {
                _mapEditor.HandleMouseClick(imgPoint.Value);
                pictureBoxMinimap.Invalidate();
            }
        }

        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            _mapEditor.Render(
                e.Graphics,
                pointF => _editorMinimap.ConvertToDisplayCoordinates(pictureBoxMinimap, Point.Round(pointF)));
        }

        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e)
        {
            _floatingMagnifier?.Hide();
        }

        #endregion

        #region 按鈕事件

        private void btn_SaveMap_Click(object sender, EventArgs e)
        {
            try
            {
                _mapFileManager?.SaveCurrentMap();
            }
            catch (Exception ex)
            {
                OnError($"儲存地圖時發生錯誤: {ex.Message}");
            }
        }

        private void btn_New_Click(object sender, EventArgs e)
        {
            try
            {
                _mapFileManager?.CreateNewMap();
            }
            catch (Exception ex)
            {
                OnError($"建立新地圖時發生錯誤: {ex.Message}");
            }
        }

        #endregion

        #region 怪物匹配

        private async void ProcessMonsterDetectionBackground(object? state)
        {
            lock (_detectionLock)
            {
                if (_isMonsterDetectionRunning) return;
                _isMonsterDetectionRunning = true;
            }

            try
            {
                Bitmap? screenCapture = _liveViewController?.GetCurrentCaptureFrame();

                if (screenCapture != null && _monsterService != null && _monsterService.HasTemplates)
                {
                    var monsterResults = await _monsterService.DetectMonstersOnScreenAsync(screenCapture);

                    if (monsterResults.Any())
                    {
                        this.BeginInvoke(() =>
                        {
                            OnStatusMessage($"找到了 {monsterResults.Count} 隻怪物！");
                            var templateSize = _monsterService.GetTemplate(0)?.Size ?? new Size(32, 32);
                            var locations = monsterResults.Select(r => r.Location).ToList();
                            _liveViewController?.DrawMonsterRectangles(locations, templateSize);
                        });
                    }

                    screenCapture.Dispose();
                }
            }
            catch (Exception ex)
            {
                this.BeginInvoke(() => OnError($"怪物偵測錯誤: {ex.Message}"));
            }
            finally
            {
                lock (_detectionLock)
                {
                    _isMonsterDetectionRunning = false;
                }
            }
        }


        #endregion

        #region 清理與釋放

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                // 停止計時器
                _backgroundMonsterTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _backgroundMonsterTimer?.Dispose();

                // 清理所有資源
                _floatingMagnifier?.Dispose();
                _liveViewController?.Dispose();
                _monsterService?.Dispose();
                _mapFileManager?.Dispose();

                // 確保圖片資源被釋放
                pictureBoxMinimap.Image?.Dispose();

                OnStatusMessage("應用程式已清理完成");
            }
            catch (Exception ex)
            {
                // 清理過程中的錯誤不應該阻止程式關閉
                System.Diagnostics.Debug.WriteLine($"清理資源時發生錯誤: {ex.Message}");
            }

            base.OnFormClosed(e);
        }

        #endregion

        #region OpenCV 診斷功能

        private void ComprehensiveOpenCvDiagnosis()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== OpenCvSharp4.Windows 完整診斷 ===");

                var currentDir = AppDomain.CurrentDomain.BaseDirectory;

                // 檢查系統環境
                System.Diagnostics.Debug.WriteLine($"應用程式目錄: {currentDir}");
                System.Diagnostics.Debug.WriteLine($"處理程序架構: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
                System.Diagnostics.Debug.WriteLine($".NET 版本: {Environment.Version}");

                // 檢查關鍵 DLL 檔案
                var criticalDlls = new[]
                {
            "OpenCvSharpExtern.dll",
            "OpenCvSharp.dll",
            "OpenCvSharp.Extensions.dll",
            "msvcp140.dll",
            "vcruntime140.dll",
            "vcruntime140_1.dll"
        };

                foreach (var dll in criticalDlls)
                {
                    var dllPath = Path.Combine(currentDir, dll);
                    var exists = File.Exists(dllPath);
                    System.Diagnostics.Debug.WriteLine($"{dll}: {(exists ? "✅存在" : "❌缺失")}");

                    if (exists)
                    {
                        var handle = LoadLibrary(dllPath);
                        var error = Marshal.GetLastWin32Error();
                        System.Diagnostics.Debug.WriteLine($"  LoadLibrary: 0x{handle.ToInt64():X}, 錯誤: {error}");

                        if (handle != IntPtr.Zero)
                        {
                            FreeLibrary(handle);
                        }
                    }
                }

                // 測試不同的 Mat 建立方式
                System.Diagnostics.Debug.WriteLine("\n=== OpenCV Mat 建立測試 ===");

                // 測試 1: 預設建構函數
                try
                {
                    using var emptyMat = new OpenCvSharp.Mat();
                    System.Diagnostics.Debug.WriteLine($"空 Mat: IsContinuous={emptyMat.IsContinuous()}, Data=0x{emptyMat.Data.ToInt64():X}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 空 Mat 建立失敗: {ex.Message}");
                }

                // 測試 2: 有尺寸的 Mat
                try
                {
                    using var sizeMat = new OpenCvSharp.Mat(100, 100, OpenCvSharp.MatType.CV_8UC3);
                    System.Diagnostics.Debug.WriteLine($"尺寸 Mat: {sizeMat.Width}x{sizeMat.Height}, Data=0x{sizeMat.Data.ToInt64():X}");

                    if (sizeMat.IsContinuous() && sizeMat.Data != IntPtr.Zero)
                    {
                        // 測試基本功能
                        using var template = OpenCvSharp.Mat.Ones(20, 20, OpenCvSharp.MatType.CV_8UC3);
                        using var result = new OpenCvSharp.Mat();

                        OpenCvSharp.Cv2.MatchTemplate(sizeMat, template, result, OpenCvSharp.TemplateMatchModes.CCoeffNormed);
                        OpenCvSharp.Cv2.MinMaxLoc(result, out var minVal, out var maxVal, out var minLoc, out var maxLoc);

                        System.Diagnostics.Debug.WriteLine($"模板匹配: maxVal={maxVal:F6}");

                        if (!double.IsInfinity(maxVal) && !double.IsNaN(maxVal))
                        {
                            System.Diagnostics.Debug.WriteLine("🎉 OpenCV 功能完全正常！");

                            // ✅ 修正：安全的 UI 更新方式
                            if (this.IsHandleCreated && !this.IsDisposed)
                            {
                                this.BeginInvoke(() =>
                                {
                                    MessageBox.Show("🎉 OpenCV 功能已恢復正常！", "測試成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                });
                            }
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 尺寸 Mat 測試失敗: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("❌ OpenCV 初始化仍然失敗");

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 診斷過程失敗: {ex.Message}");
            }
        }

        #endregion
    }
}
    
