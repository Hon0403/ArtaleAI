namespace ArtaleAI;

using ArtaleAI.UI.MapEditing;

static class Program
{
    [System.STAThread]
    static int Main()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("ARTALEAI_LAYOUT_VERIFY"), "1", StringComparison.Ordinal))
            return MapEditorSidebarLayoutVerify.Run();

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        System.Windows.Forms.Application.Run(new MainForm());
        return 0;
    }
}
