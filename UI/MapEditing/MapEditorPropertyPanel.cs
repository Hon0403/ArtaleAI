using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Map;
using System.Linq;

namespace ArtaleAI.UI.MapEditing
{
    /// <summary>
    /// 地圖編輯器右側屬性面板：顯示狀態與非幾何欄位。
    /// 幾何座標請在畫布上操作，不在此輸入數字。
    /// </summary>
    public sealed class MapEditorPropertyPanel : UserControl
    {
        private readonly Label _lblMode;
        private readonly Label _lblDirty;
        private readonly Label _lblError;
        private readonly Panel _scrollHost;
        private readonly FlowLayoutPanel _content;

        private MapEditor? _editor;
        private bool _isBinding;
        private const int ContentMaxWidth = 272;

        public MapEditorPropertyPanel()
        {
            _lblMode = CreateHeaderLabel();
            _lblDirty = CreateHeaderLabel();
            _lblDirty.ForeColor = Color.DarkOrange;
            _lblError = CreateHeaderLabel();
            _lblError.ForeColor = Color.Firebrick;
            _lblError.Visible = false;

            _content = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 4, 0, 8),
                Margin = Padding.Empty
            };

            _scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };
            _scrollHost.Controls.Add(_content);
            _scrollHost.Resize += (_, _) => UpdateScrollMetrics();

            var layout = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            layout.Controls.Add(_scrollHost);
            layout.Controls.Add(_lblError);
            layout.Controls.Add(_lblDirty);
            layout.Controls.Add(_lblMode);
            Controls.Add(layout);
        }

        public void Bind(MapEditor editor)
        {
            _editor = editor;
            RefreshFromEditor(editor);
        }

        public void RefreshFromEditor(MapEditor? editor)
        {
            _editor = editor;
            if (_isBinding) return;

            _isBinding = true;
            try
            {
                if (editor == null)
                {
                    _lblMode.Text = "模式: —";
                    _lblDirty.Text = string.Empty;
                    ClearError();
                    RebuildContent(null);
                    return;
                }

                _lblMode.Text = $"模式: {editor.GetCurrentEditMode()}";
                _lblDirty.Text = editor.IsDirty ? "● 未儲存變更" : string.Empty;
                ClearError();
                RebuildContent(editor);
            }
            finally
            {
                _isBinding = false;
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateScrollMetrics();
        }

        private void RebuildContent(MapEditor? editor)
        {
            _content.Controls.Clear();
            _content.SuspendLayout();

            if (editor == null)
            {
                _content.Controls.Add(MakeLabel("（無編輯器）"));
                _content.ResumeLayout();
                UpdateScrollMetrics();
                return;
            }

            var selection = editor.Selection;
            if (selection.IsEmpty)
            {
                BuildEmptyState(editor);
            }
            else
            {
                switch (selection.Kind)
                {
                    case MapEditorSelectionKind.Platform when selection.Platform != null:
                        BuildPlatformView(editor, selection.Platform, selection.SegmentIndex);
                        break;
                    case MapEditorSelectionKind.Rope when selection.RopeIndex >= 0:
                        BuildRopeView(editor, selection.RopeIndex);
                        break;
                    case MapEditorSelectionKind.ManualEdge when selection.ManualEdge != null:
                        BuildManualEdgeView(editor, selection.ManualEdge);
                        break;
                    case MapEditorSelectionKind.RuntimeNode:
                        BuildRuntimeNodeView(editor, selection.RuntimeNodeIndex);
                        break;
                }
            }

            if (!editor.Selection.IsEmpty)
                AppendInspectorFooter(editor);

            AppendValidationSection(editor);

            _content.ResumeLayout(true);
            UpdateScrollMetrics();
        }

        private void UpdateScrollMetrics()
        {
            if (_scrollHost.ClientSize.Width <= 0)
                return;

            int contentWidth = Math.Min(
                ContentMaxWidth,
                _scrollHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            if (contentWidth < 120)
                contentWidth = _scrollHost.ClientSize.Width - 4;

            _content.Width = contentWidth;
            _content.PerformLayout();

            var preferred = _content.GetPreferredSize(new Size(contentWidth, 0));
            int scrollHeight = preferred.Height + _content.Padding.Vertical + 4;
            _scrollHost.AutoScrollMinSize = new Size(0, Math.Max(scrollHeight, _scrollHost.ClientSize.Height + 1));
        }

        public event Action<MapEditorValidationIssue>? ValidationIssueActivated;

        private void AppendValidationSection(MapEditor editor)
        {
            var validation = editor.LastValidation;
            AddSectionTitle("驗證");
            AddRow($"連通子圖: {validation.ConnectedComponentCount}");
            AddRow($"錯誤 {validation.ErrorCount} / 警告 {validation.WarningCount} / 提示 {validation.InfoCount}");

            if (!validation.HasIssues)
            {
                AddHint("目前無驗證問題。");
                return;
            }

            int contentWidth = Math.Min(ContentMaxWidth, _content.Width > 0 ? _content.Width : ContentMaxWidth);
            var list = new ListBox
            {
                Width = contentWidth,
                Height = Math.Min(140, validation.Issues.Count * 17 + 8),
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle
            };

            foreach (var issue in validation.Issues)
            {
                list.Items.Add(new ValidationListItem(issue));
            }

            list.SelectedIndexChanged += (_, _) =>
            {
                if (list.SelectedItem is not ValidationListItem item || _editor == null)
                    return;

                _editor.FocusValidationIssue(item.Issue);
                ValidationIssueActivated?.Invoke(item.Issue);
            };

            _content.Controls.Add(list);
            AddHint("點選警告可跳轉至對應物件。");
        }

        private void AppendInspectorFooter(MapEditor editor)
        {
            AddSectionTitle("Runtime 檢視");
            var lbl = MakeLabel(editor.FormatInspectorText());
            lbl.Font = new Font("Consolas", 8f);
            _content.Controls.Add(lbl);
        }

        private void BuildEmptyState(MapEditor editor)
        {
            var summary = editor.GetMapSummary();
            AddSectionTitle("地圖摘要");
            AddRow($"平台: {summary.PlatformCount}");
            AddRow($"繩索: {summary.RopeCount}");
            AddRow($"手動邊: {summary.ManualEdgeCount}");
            AddRow($"Runtime 節點: {summary.RuntimeNodeCount}");
            AddRow($"Runtime 邊: {summary.RuntimeEdgeCount}");
            AddCanvasGuide(
                "路線標記：在畫布點擊建立折線",
                "繩索標記：拖曳上下兩端",
                "兩點連線（進階）：點選起終點平台",
                "選取：檢視屬性；拖曳折點調整形狀",
                "Shift+點擊：循環選節點");
        }

        private void BuildPlatformView(MapEditor editor, PolylinePlatformData platform, int segmentIndex)
        {
            var stats = editor.GetPlatformStats(platform, segmentIndex);
            AddSectionTitle("Platform");

            var idRow = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 4)
            };
            var txtId = new TextBox { Width = 120, Text = platform.Id };
            var btnId = new Button { Text = "套用 Id", AutoSize = true };
            btnId.Click += (_, _) =>
            {
                if (editor.TryRenamePlatformId(platform, txtId.Text, out var error))
                    RefreshFromEditor(editor);
                else
                    ShowError(error);
            };
            idRow.Controls.Add(MakeCaption("Id:"));
            idRow.Controls.Add(txtId);
            idRow.Controls.Add(btnId);
            _content.Controls.Add(idRow);

            if (stats.SelectedSegmentIndex.HasValue)
                AddRow($"Segment: {stats.SelectedSegmentIndex.Value}");
            AddRow($"點數: {stats.PointCount} | 長度: {stats.TotalLength:F1}px");
            AddRow($"相依 ManualEdge: {stats.DependentManualEdgeCount}");

            AddSectionTitle("折點（唯讀）");
            for (int i = 0; i < platform.Points.Count; i++)
            {
                var p = platform.Points[i];
                AddRow($"  [{i}] ({p.X:F1}, {p.Y:F1})");
            }

            AddCanvasGuide(
                "修改形狀：選取模式下拖曳折點",
                "插點：切到「路線標記」，點折線可插點",
                "完成折線：右鍵或 Enter",
                "刪除平台：切到「刪除標記」再點選");
        }

        private void BuildRopeView(MapEditor editor, int ropeIndex)
        {
            if (editor.GetCurrentMapData().Ropes == null ||
                ropeIndex < 0 ||
                ropeIndex >= editor.GetCurrentMapData().Ropes.Count)
            {
                AddRow("（繩索已不存在）");
                return;
            }

            var rope = editor.GetCurrentMapData().Ropes[ropeIndex];
            var stats = editor.GetRopeStats(ropeIndex);
            AddSectionTitle($"Rope #{ropeIndex}");
            AddRow($"ropeX: {rope[0]:F1}");
            AddRow($"topY: {rope[1]:F1}");
            AddRow($"bottomY: {rope[2]:F1}");
            AddRow($"Climb 邊（全圖）: {stats.ClimbEdgeCount}");
            AddCanvasGuide(
                "調整位置：刪除後用「繩索標記」在畫布重畫",
                "刪除：切到「刪除標記」再點選");
        }

        private void BuildManualEdgeView(MapEditor editor, ManualEdgeAnchor anchor)
        {
            var stats = editor.GetManualEdgeStats(anchor);
            AddSectionTitle("ManualEdge");

            AddRow($"From: {anchor.FromPlatformId} ({anchor.FromX:F1}, {anchor.FromY:F1})");
            AddRow($"To:   {anchor.ToPlatformId} ({anchor.ToX:F1}, {anchor.ToY:F1})");

            var cboAction = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            cboAction.Items.Add(new ActionItem("Jump", NavigationActionType.Jump));
            cboAction.Items.Add(new ActionItem("SideJump", NavigationActionType.SideJump));
            cboAction.Items.Add(new ActionItem("JumpDown", NavigationActionType.JumpDown));
            cboAction.Items.Add(new ActionItem("Teleport", NavigationActionType.Teleport));
            cboAction.SelectedItem = cboAction.Items
                .Cast<ActionItem>()
                .FirstOrDefault(i => i.Value == anchor.ActionType) ?? cboAction.Items[0];

            var btnAction = new Button { Text = "套用動作類型", AutoSize = true };
            btnAction.Click += (_, _) =>
            {
                if (cboAction.SelectedItem is not ActionItem actionItem)
                    return;
                if (editor.TryUpdateManualEdgeActionType(anchor, actionItem.Value, out var error))
                    RefreshFromEditor(editor);
                else
                    ShowError(error);
            };

            AddFieldRow("ActionType", cboAction);
            _content.Controls.Add(btnAction);

            AddRow(stats.Resolved ? "解析: 成功" : "解析: 失敗");
            AddRow(stats.HasReverse ? "反向: 已有其他 ManualEdge" : "反向: 無（單向）");
            AddCanvasGuide(
                "移動錨點：刪除後用「兩點連線」在畫布重標",
                "僅改動作類型：可用上方下拉（不需輸入座標）");
        }

        private void BuildRuntimeNodeView(MapEditor editor, int nodeIndex)
        {
            AddSectionTitle("Runtime Node（唯讀）");
            AddHint("節點由拓撲自動產生，請修改對應 Platform / Rope / ManualEdge。");
            if (nodeIndex < 0 || nodeIndex >= editor.GetCurrentMapData().Nodes.Count)
            {
                AddRow("（節點已不存在）");
                return;
            }

            var node = editor.GetCurrentMapData().Nodes[nodeIndex];
            AddRow($"索引: {nodeIndex}");
            AddRow($"Id: {node.Id}");
            AddRow($"座標: ({node.X:F1}, {node.Y:F1})");
            AddRow($"PlatformId: {node.PlatformId ?? "—"}");
        }

        private void AddCanvasGuide(params string[] lines)
        {
            AddSectionTitle("畫布操作");
            foreach (var line in lines)
                AddHint("• " + line);
        }

        private void AddSectionTitle(string text) =>
            _content.Controls.Add(new Label
            {
                Text = text,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 2),
                MaximumSize = new Size(ContentMaxWidth, 0)
            });

        private void AddRow(string text) => _content.Controls.Add(MakeLabel(text));

        private void AddHint(string text)
        {
            var lbl = MakeLabel(text);
            lbl.ForeColor = Color.DimGray;
            _content.Controls.Add(lbl);
        }

        private void AddFieldRow(string caption, Control editor)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 4)
            };
            row.Controls.Add(MakeCaption($"{caption}:"));
            row.Controls.Add(editor);
            _content.Controls.Add(row);
        }

        private Label MakeLabel(string text) =>
            new()
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2),
                MaximumSize = new Size(ContentMaxWidth, 0)
            };

        private static Label MakeCaption(string text) =>
            new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 4, 0) };

        private static Label CreateHeaderLabel() =>
            new() { Dock = DockStyle.Top, AutoSize = false, Height = 20 };

        private void ShowError(string? message)
        {
            _lblError.Text = message ?? "操作失敗";
            _lblError.Visible = true;
        }

        private void ClearError()
        {
            _lblError.Text = string.Empty;
            _lblError.Visible = false;
        }

        private sealed class ActionItem
        {
            public string Text { get; }
            public NavigationActionType Value { get; }
            public ActionItem(string text, NavigationActionType value) { Text = text; Value = value; }
            public override string ToString() => Text;
        }

        private sealed class ValidationListItem
        {
            public MapEditorValidationIssue Issue { get; }
            public ValidationListItem(MapEditorValidationIssue issue) => Issue = issue;

            public override string ToString()
            {
                string tag = Issue.Severity switch
                {
                    MapEditorValidationSeverity.Error => "錯",
                    MapEditorValidationSeverity.Warning => "警",
                    _ => "訊"
                };
                return $"[{tag}] {Issue.Code}: {Issue.Message}";
            }
        }
    }
}
