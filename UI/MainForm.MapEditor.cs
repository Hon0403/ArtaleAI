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
    public partial class MainForm
    {
        #region 地圖編輯事件

        private void InitializeAdvancedModeCheckBox()
        {
            rdo_TwoPointLink.Enabled = false;
        }

        private void chk_AdvancedMode_CheckedChanged(object? sender, EventArgs e)
        {
            if (!chk_AdvancedMode.Checked && _mapEditor?.GetCurrentEditMode() == EditMode.ManualEdge)
                rdo_SelectMode.Checked = true;

            UpdateEditModeAndActionUi();
        }

        private void InitializeMapEditorPropertyPanel()
        {
            _mapPropertyPanel = new MapEditorPropertyPanel
            {
                Dock = DockStyle.Fill
            };
            groupBox_PropertyPanel.Controls.Add(_mapPropertyPanel);

            panelToolsScroll.Resize += (_, _) => SyncSidebarToolsLayout();
            panel4.Resize += (_, _) => SyncSidebarToolsLayout();
            splitSidebar.SplitterMoved += (_, _) => SyncSidebarToolsLayout();
            SyncSidebarToolsLayout();

            if (_mapEditor == null) return;

            _mapEditor.SelectionChanged += OnMapEditorSelectionChanged;
            _mapEditor.DirtyStateChanged += OnMapEditorDirtyStateChanged;
            _mapEditor.MapMutated += OnMapEditorMapMutated;
            _mapEditor.ValidationChanged += OnMapEditorValidationChanged;
            _mapEditor.HistoryChanged += OnMapEditorHistoryChanged;
            _mapEditor.Layers.Changed += OnMapEditorLayersChanged;
            _mapEditor.ConfirmDestructiveAction = message =>
                MessageBox.Show(message, "確認刪除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) ==
                DialogResult.Yes;

            _mapPropertyPanel.Bind(_mapEditor);
            _mapPropertyPanel.ValidationIssueActivated += OnValidationIssueActivated;
            RefreshMapEditorStatusBar();
        }

        private void SyncSidebarToolsLayout()
        {
            int width = Math.Max(280, panelToolsScroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            flowToolsStack.Width = width;
            foreach (Control child in flowToolsStack.Controls)
                child.Width = width;

            flowToolsStack.PerformLayout();
            panelToolsScroll.AutoScrollMinSize = new SdSize(0, flowToolsStack.PreferredSize.Height + 6);
        }

        private void OnLayerCheckboxChanged(object? sender, EventArgs e)
        {
            if (_mapEditor == null) return;

            _mapEditor.Layers.ShowPlatforms = chk_LayerPlatforms.Checked;
            _mapEditor.Layers.ShowRopes = chk_LayerRopes.Checked;
            _mapEditor.Layers.ShowJumpLinks = chk_LayerJumpLinks.Checked;
            _mapEditor.Layers.ShowSafeZones = chk_LayerSafeZones.Checked;
            _mapEditor.Layers.ShowManualAnchors = chk_LayerManualAnchors.Checked;
            _mapEditor.Layers.ShowNodes = chk_LayerNodes.Checked;
            _mapEditor.Layers.ShowEdges = chk_LayerEdges.Checked;
            _mapEditor.Layers.ShowValidationOverlays = chk_LayerValidation.Checked;
            _mapEditor.Layers.NotifyChanged();
        }

        private void OnMapEditorLayersChanged()
        {
            if (InvokeRequired)
            {
                BeginInvoke(OnMapEditorLayersChanged);
                return;
            }

            pictureBoxMinimap.Invalidate();
        }

        private void OnMapEditorHistoryChanged()
        {
            RefreshMapEditorStatusBar();
        }

        private void RefreshMapEditorStatusBar()
        {
            if (InvokeRequired)
            {
                BeginInvoke(RefreshMapEditorStatusBar);
                return;
            }

            if (_mapEditor == null)
            {
                lbl_MapStatus.Text = "—";
                return;
            }

            string undo = _mapEditor.CanUndo ? "可復原" : "—";
            lbl_MapStatus.Text =
                $"{_mapEditor.GetCurrentEditMode()} | {_mapEditor.FormatStatusSummary()} | Undo:{undo}";
        }

        private bool ConfirmDiscardUnsavedChanges(string actionDescription)
        {
            if (_mapEditor?.IsDirty != true)
                return true;

            var result = MessageBox.Show(
                $"目前有未儲存的地圖變更，確定要{actionDescription}嗎？",
                "未儲存變更",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            return result == DialogResult.Yes;
        }

        private void OnMapEditorMapMutated()
        {
            if (InvokeRequired)
            {
                BeginInvoke(OnMapEditorMapMutated);
                return;
            }

            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void OnMapEditorValidationChanged()
        {
            RefreshMapEditorPropertyPanel();
            RefreshMapEditorStatusBar();
        }

        private void OnValidationIssueActivated(MapEditorValidationIssue issue)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnValidationIssueActivated(issue));
                return;
            }

            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void OnMapEditorSelectionChanged()
        {
            RefreshMapEditorPropertyPanel();
            RefreshMapEditorStatusBar();
        }

        private void OnMapEditorDirtyStateChanged()
        {
            RefreshMapEditorPropertyPanel();
            UpdateMapEditorWindowTitle();
            RefreshMapEditorStatusBar();
        }

        private void RefreshMapEditorPropertyPanel()
        {
            if (InvokeRequired)
            {
                BeginInvoke(RefreshMapEditorPropertyPanel);
                return;
            }

            _mapPropertyPanel?.RefreshFromEditor(_mapEditor);
        }

        private void UpdateMapEditorWindowTitle()
        {
            if (tabControl1.SelectedTab != tabPage2) return;

            string fileName = _mapFileManager?.CurrentMapFileName ?? "未命名";
            string dirtySuffix = _mapEditor?.IsDirty == true ? " *" : string.Empty;
            UpdateWindowTitle($"{_mapEditorTitleBase} - {fileName}{dirtySuffix}");
        }

        private void UpdateEditModeAndActionUi()
        {
            EditMode selectedMode = EditMode.None;
            if (rdo_PathMarker.Checked) selectedMode = EditMode.Platform;
            else if (rdo_RopeMarker.Checked) selectedMode = EditMode.Rope;
            else if (rdo_JumpLinkMarker.Checked) selectedMode = EditMode.JumpLink;
            else if (rdo_DeleteMarker.Checked) selectedMode = EditMode.Delete;
            else if (rdo_SelectMode.Checked) selectedMode = EditMode.Select;
            else if (rdo_TwoPointLink.Checked) selectedMode = EditMode.ManualEdge;

            bool advancedActive = chk_AdvancedMode.Checked;

            rdo_TwoPointLink.Enabled = advancedActive;
            groupBox_Action.Enabled = (selectedMode == EditMode.ManualEdge) && advancedActive;

            if (_mapEditor != null)
            {
                _mapEditor.SetEditMode(selectedMode);
            }
            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void OnEditModeChanged(object? sender, EventArgs e)
        {
            if (sender is RadioButton rb && !rb.Checked)
                return;

            UpdateEditModeAndActionUi();
            RefreshMapEditorStatusBar();

            EditMode selectedMode = _mapEditor?.GetCurrentEditMode() ?? EditMode.None;
            MsgLog.ShowStatus(textBox1, $"編輯模式切換至: {selectedMode}");
        }

        private void rdo_SelectMode_CheckedChanged(object sender, EventArgs e)
        {
            OnEditModeChanged(sender, e);
        }

        #region Merged UI Events (from partial classes)

        private void cbo_MapFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressMapFileSelectionChange || cbo_MapFiles.SelectedItem == null) return;

            string selectedFile = cbo_MapFiles.SelectedItem.ToString() ?? "";
            if (selectedFile == "null" || string.IsNullOrEmpty(selectedFile)) return;

            if (!ConfirmDiscardUnsavedChanges("載入另一張地圖"))
            {
                _suppressMapFileSelectionChange = true;
                try
                {
                    if (!string.IsNullOrEmpty(_lastMapFileSelection) &&
                        cbo_MapFiles.Items.Contains(_lastMapFileSelection))
                        cbo_MapFiles.SelectedItem = _lastMapFileSelection;
                }
                finally
                {
                    _suppressMapFileSelectionChange = false;
                }
                return;
            }

            _lastMapFileSelection = selectedFile;
            MsgLog.ShowStatus(textBox1, $"載入地圖檔案: {selectedFile}");
            _mapFileManager?.LoadMapFile(selectedFile);
        }

        private void cbo_LoadPathFile_SelectedIndexChanged(object sender, EventArgs e)
        {
            string? selectedFile = cbo_LoadPathFile.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedFile) || selectedFile == "null")
            {
                loadedPathData = null;
                _mapEditor?.LoadMapData(new MapData());
                pictureBoxMinimap.Invalidate();
                UpdateAutoAttackState();
                return;
            }
            try
            {
                var mapFilePath = System.IO.Path.Combine(
                    PathManager.MapDataDirectory, $"{selectedFile}.json");
                var mapData = MapFileManager.LoadMapFromFile(mapFilePath);
                if (mapData != null)
                {
                    loadedPathData = mapData;
                    _mapEditor?.LoadMapData(mapData);
                    _pathPlanningManager?.LoadMap(mapData);
                    pictureBoxMinimap.Invalidate();
                    MsgLog.ShowStatus(textBox1, $"已載入路徑檔: {selectedFile}");
                    UpdateAutoAttackState();
                }
                else
                {
                    loadedPathData = null;
                    _mapEditor?.LoadMapData(new MapData());
                    pictureBoxMinimap.Invalidate();
                    MsgLog.ShowError(textBox1, $"無法載入路徑檔: {selectedFile}");
                    UpdateAutoAttackState();
                }
            }
            catch (Exception ex)
            {
                loadedPathData = null;
                _mapEditor?.LoadMapData(new MapData());
                pictureBoxMinimap.Invalidate();
                MsgLog.ShowError(textBox1, $"路徑檔載入失敗: {ex.Message}");
                UpdateAutoAttackState();
            }
        }

        #endregion

        private void InitializeActionComboBox()
        {
            cbo_ActionType.Items.Clear();
            cbo_ActionType.Items.Add(new ComboBoxItem("Walk (走路)", (int)NavigationActionType.Walk));
            cbo_ActionType.Items.Add(new ComboBoxItem("SideJump (側跳)", (int)NavigationActionType.SideJump));
            cbo_ActionType.Items.Add(new ComboBoxItem("Jump (原地跳)", (int)NavigationActionType.Jump));
            cbo_ActionType.Items.Add(new ComboBoxItem("JumpDown (下跳)", (int)NavigationActionType.JumpDown));
            cbo_ActionType.Items.Add(new ComboBoxItem("Teleport (傳送)", (int)NavigationActionType.Teleport));

            cbo_ActionType.SelectedIndex = 0;
        }

        private void cbo_ActionType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbo_ActionType.SelectedItem is not ComboBoxItem item || _mapEditor == null) return;

            _mapEditor.SetCurrentActionType(item.Value);
            pictureBoxMinimap.Invalidate();
        }


        private class ComboBoxItem
        {
            public string Text { get; }
            public int Value { get; }
            public ComboBoxItem(string text, int value) { Text = text; Value = value; }
            public override string ToString() => Text;
        }

        #endregion
    }
}
