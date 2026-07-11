using ArtaleAI.Infrastructure.External;
using ArtaleAI.Infrastructure.External.Config;
using ArtaleAI.Models.Config;
using ArtaleAI.Vision;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Map;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Application.Pipeline;
using ArtaleAI.Application.Navigation;
using ArtaleAI.Application.Movement;
using ArtaleAI.Infrastructure.Capture;
using ArtaleAI.Infrastructure.Persistence;
using ArtaleAI.Infrastructure.Input;
using ArtaleAI.Contracts;
using ArtaleAI.UI;
using ArtaleAI.UI.MapEditor;
using ArtaleAI.Models.Visualization;
using ArtaleAI.Shared;
using ArtaleAI.Domain.Navigation;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;
using SdRect = System.Drawing.Rectangle;
using SdSize = System.Drawing.Size;
using Timer = System.Threading.Timer;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArtaleAI
{
    public partial class MainForm : Form
    {
        #region IConfigEventHandler 實作

        public void OnConfigLoaded(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigLoaded), config);
                return;
            }

            MsgLog.ShowStatus(textBox1, "配置檔案載入完成");
        }

        public void OnMapSaved(string fileName, bool isNewFile)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnMapSaved(fileName, isNewFile));
                return;
            }

            _mapEditor?.ClearDirty();
            _lastMapFileSelection = fileName;
            RefreshMapEditorStatusBar();

            if (isNewFile)
            {
                SyncMapFileDropdowns(true);
            }

            RefreshMinimap();
            RefreshMapEditorPropertyPanel();
            UpdateMapEditorWindowTitle();
            string message = isNewFile ? "新地圖儲存成功！" : "儲存成功！";
            MessageBox.Show(message, "地圖檔案管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            MsgLog.ShowStatus(textBox1, $"地圖儲存: {fileName}");
        }

        public void OnConfigSaved(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigSaved), config);
                return;
            }

            MsgLog.ShowStatus(textBox1, "設定已儲存");
        }

        public void OnConfigError(string errorMessage)
        {
            MsgLog.ShowError(textBox1, $"設定錯誤: {errorMessage}");
        }

        #endregion

        #region 辨識模式控制

        /// <summary>依設定檔填入怪物偵測模式下拉選單。</summary>
        private void InitializeDetectionModeDropdown()
        {
            cbo_DetectMode.Items.Clear();
            var config = AppConfig.Instance;

            if (config.Vision.DisplayOrder?.Any() == true && config.Vision.DetectionModes?.Any() == true)
            {
                try
                {
                    foreach (var mode in config.Vision.DisplayOrder)
                    {
                        if (config.Vision.DetectionModes.TryGetValue(mode, out var modeConfig))
                        {
                            cbo_DetectMode.Items.Add(modeConfig.DisplayName);
                        }
                    }

                    var defaultMode = config.Vision.DefaultMode;
                    if (config.Vision.DetectionModes.TryGetValue(defaultMode, out var defaultModeConfig))
                    {
                        cbo_DetectMode.SelectedItem = defaultModeConfig.DisplayName;
                    }

                    MsgLog.ShowStatus(textBox1, $"檢測模式已載入：{config.Vision.DisplayOrder.Count} 個模式，預設：{defaultMode}");
                }
                catch (Exception ex)
                {
                    MsgLog.ShowError(textBox1, $"檢測模式初始化失敗: {ex.Message}");
                }
            }
            else
            {
                MsgLog.ShowError(textBox1, "檢測模式配置無效");
            }

            cbo_DetectMode.SelectedIndexChanged += OnDetectionModeChanged;
        }

        private void OnDetectionModeChanged(object? sender, EventArgs e)
        {
            var selectedDisplayText = cbo_DetectMode.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedDisplayText)) return;

            var config = AppConfig.Instance;

            var selectedMode = config.Vision.DetectionModes?
                .FirstOrDefault(kvp => kvp.Value.DisplayName == selectedDisplayText).Key
                ?? config.Vision.DefaultMode ?? "Normal";

            var optimalOcclusion = "None";
            if (config.Vision.DetectionModes?.TryGetValue(selectedMode, out var modeConfig) == true)
            {
                optimalOcclusion = modeConfig.Occlusion;
            }

            config.Vision.DetectionMode = selectedMode;

            MsgLog.ShowStatus(textBox1, $" 偵測模式: {selectedMode} | 遮擋: {optimalOcclusion}");
        }

        #endregion
    }
}
