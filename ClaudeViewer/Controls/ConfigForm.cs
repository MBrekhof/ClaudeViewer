using DevExpress.XtraEditors;

namespace ClaudeViewer.Controls;

public sealed class ConfigForm : XtraForm
{
    public ConfigPanel Panel { get; }

    public ConfigForm()
    {
        Text = "Claude Code Configuration";
        Panel = new ConfigPanel();
        Controls.Add(Panel);
        ClientSize = new System.Drawing.Size(1100, 600);
    }
}
