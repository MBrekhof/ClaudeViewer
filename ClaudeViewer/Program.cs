using DevExpress.LookAndFeel;
using DevExpress.Skins;

namespace ClaudeViewer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        SkinManager.EnableFormSkins();
        UserLookAndFeel.Default.SetSkinStyle(SkinStyle.WXICompact);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
