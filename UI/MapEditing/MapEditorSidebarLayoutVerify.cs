using System.Reflection;
using ArtaleAI.UI;
using ArtaleAI.UI.MapEditing;

namespace ArtaleAI.UI.MapEditing;

/// <summary>
/// 地圖編輯器右側欄版面自動驗證（由 ARTALEAI_LAYOUT_VERIFY=1 觸發）。
/// </summary>
internal static class MapEditorSidebarLayoutVerify
{
    public static int Run()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();

        var failures = new List<string>();
        using var form = new MainForm();
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Size = new Size(1280, 800);
        form.Show();
        Application.DoEvents();

        SelectMapEditorTab(form);
        ForceLayout(form);

        VerifySplitterAdjustable(form, failures);
        VerifyToolsAreaScrolls(form, failures);
        VerifyPropertyPanelScrollAndBottomBar(form, failures);

        form.Close();

        if (failures.Count == 0)
        {
            Console.WriteLine("PASS: splitSidebar 三項版面行為驗證通過");
            return 0;
        }

        Console.Error.WriteLine("FAIL: 版面驗證未通過");
        foreach (string failure in failures)
            Console.Error.WriteLine("  - " + failure);
        return 1;
    }

    private static void SelectMapEditorTab(MainForm form)
    {
        var tabControl = FindControl<TabControl>(form, "tabControl1")
            ?? throw new InvalidOperationException("找不到 tabControl1");
        var mapTab = tabControl.TabPages.Cast<TabPage>()
            .FirstOrDefault(p => p.Name == "tabPage2" || p.Text.Contains("地圖"))
            ?? throw new InvalidOperationException("找不到地圖編輯分頁");
        tabControl.SelectedTab = mapTab;
        Application.DoEvents();
    }

    private static void VerifySplitterAdjustable(MainForm form, List<string> failures)
    {
        var split = GetField<SplitContainer>(form, "splitSidebar");
        if (split == null)
        {
            failures.Add("splitSidebar 不存在");
            return;
        }

        if (split.Orientation != Orientation.Horizontal)
            failures.Add($"splitSidebar.Orientation 應為 Horizontal，實際 {split.Orientation}");

        if (split.IsSplitterFixed)
            failures.Add("splitSidebar.IsSplitterFixed 為 true，分隔線不可拖曳");

        int beforePanel1 = split.Panel1.Height;
        int beforePanel2 = split.Panel2.Height;
        int target = Math.Clamp(
            split.Height - split.Panel2MinSize - split.SplitterWidth - 40,
            split.Panel1MinSize,
            split.Height - split.Panel2MinSize - split.SplitterWidth);
        split.SplitterDistance = target;
        ForceLayout(form);

        if (split.Panel1.Height == beforePanel1 && split.Panel2.Height == beforePanel2)
            failures.Add("變更 SplitterDistance 後 Panel 高度未改變");

        if (split.Panel1.Height < split.Panel1MinSize - 2)
            failures.Add($"Panel1 高度 {split.Panel1.Height} 低於 MinSize {split.Panel1MinSize}");

        if (split.Panel2.Height < split.Panel2MinSize - 2)
            failures.Add($"Panel2 高度 {split.Panel2.Height} 低於 MinSize {split.Panel2MinSize}");
    }

    private static void VerifyToolsAreaScrolls(MainForm form, List<string> failures)
    {
        var split = GetField<SplitContainer>(form, "splitSidebar");
        var toolsScroll = GetField<Panel>(form, "panelToolsScroll");
        var toolsStack = GetField<FlowLayoutPanel>(form, "flowToolsStack");
        if (split == null || toolsScroll == null || toolsStack == null)
        {
            failures.Add("工具區控制項缺失（splitSidebar / panelToolsScroll / flowToolsStack）");
            return;
        }

        if (!toolsScroll.AutoScroll)
            failures.Add("panelToolsScroll.AutoScroll 未啟用");

        InvokePrivate(form, "SyncSidebarToolsLayout");
        ForceLayout(form);

        int contentHeight = toolsStack.PreferredSize.Height + 6;
        if (contentHeight <= toolsScroll.ClientSize.Height)
            failures.Add($"工具區內容高度 {contentHeight} 未大於可視高度 {toolsScroll.ClientSize.Height}，無法驗證捲動");

        split.SplitterDistance = split.Panel1MinSize;
        InvokePrivate(form, "SyncSidebarToolsLayout");
        ForceLayout(form);

        var minSize = toolsScroll.AutoScrollMinSize;
        if (minSize.Height <= toolsScroll.ClientSize.Height)
            failures.Add($"壓縮工具區後 AutoScrollMinSize.Height={minSize.Height} 未大於 ClientSize={toolsScroll.ClientSize.Height}");

        if (!(minSize.Height > toolsScroll.ClientSize.Height && toolsScroll.AutoScroll))
            failures.Add("工具區在空間不足時無法垂直捲動");
    }

    private static void VerifyPropertyPanelScrollAndBottomBar(MainForm form, List<string> failures)
    {
        var panel4 = GetField<Panel>(form, "panel4");
        var bottomHost = GetField<Panel>(form, "panel4BottomHost");
        var split = GetField<SplitContainer>(form, "splitSidebar");
        var propertyGroup = GetField<GroupBox>(form, "groupBox_PropertyPanel");
        var propertyPanel = GetField<MapEditorPropertyPanel>(form, "_mapPropertyPanel");
        if (panel4 == null || bottomHost == null || split == null || propertyGroup == null || propertyPanel == null)
        {
            failures.Add("屬性區或底部狀態列控制項缺失");
            return;
        }

        if (bottomHost.Dock != DockStyle.Bottom)
            failures.Add("panel4BottomHost 未 Dock Bottom");

        if (split.Dock != DockStyle.Fill)
            failures.Add("splitSidebar 未 Dock Fill");

        if (!panel4.Controls.Contains(bottomHost) || !panel4.Controls.Contains(split))
            failures.Add("panel4 未同時包含 splitSidebar 與 panel4BottomHost");

        if (bottomHost.Height <= 0)
            failures.Add("底部狀態列高度為 0");

        if (bottomHost.Top < split.Bottom - 2)
            failures.Add($"底部狀態列 (Top={bottomHost.Top}) 與 splitSidebar (Bottom={split.Bottom}) 重疊");

        var scrollHost = GetField<Panel>(propertyPanel, "_scrollHost");
        if (scrollHost == null)
        {
            failures.Add("MapEditorPropertyPanel._scrollHost 不存在");
            return;
        }

        if (!scrollHost.AutoScroll)
            failures.Add("屬性面板 _scrollHost.AutoScroll 未啟用");

        var mapEditor = GetField<MapEditor>(form, "_mapEditor");
        if (mapEditor != null)
        {
            mapEditor.RunValidation();
            propertyPanel.RefreshFromEditor(mapEditor);
        }

        InvokePrivate(propertyPanel, "UpdateScrollMetrics");
        ForceLayout(form);

        split.SplitterDistance = split.Height - split.Panel2MinSize - split.SplitterWidth;
        ForceLayout(form);
        InvokePrivate(propertyPanel, "UpdateScrollMetrics");
        ForceLayout(form);

        if (scrollHost.AutoScrollMinSize.Height <= scrollHost.ClientSize.Height)
            failures.Add($"屬性面板 AutoScrollMinSize.Height={scrollHost.AutoScrollMinSize.Height} 未大於 ClientSize={scrollHost.ClientSize.Height}");

        if (propertyGroup.Bottom > bottomHost.Top + 2)
            failures.Add($"屬性面板底部 {propertyGroup.Bottom} 超出 split 區域，可能被狀態列遮擋");
    }

    private static void ForceLayout(Control root)
    {
        root.PerformLayout();
        root.Refresh();
        Application.DoEvents();
    }

    private static T? GetField<T>(object instance, string name) where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return field?.GetValue(instance) as T;
    }

    private static T? FindControl<T>(Control root, string name) where T : Control
    {
        if (root.Name == name && root is T match)
            return match;

        foreach (Control child in root.Controls)
        {
            var found = FindControl<T>(child, name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        method?.Invoke(instance, null);
    }
}
