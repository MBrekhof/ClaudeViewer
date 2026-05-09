using ClaudeViewer.Models;
using DevExpress.XtraEditors;

namespace ClaudeViewer.Controls;

public sealed class ArtifactForm : XtraForm
{
    private readonly ArtifactPanel _panel = new() { Dock = DockStyle.Fill };

    public Artifact? Current => _panel.Current;

    public ArtifactForm()
    {
        Text = "Artifact";
        Size = new Size(900, 700);
        Controls.Add(_panel);
    }

    public Task LoadAsync(Artifact a)
    {
        Text = a.Title ?? a.FileName;
        return _panel.LoadAsync(a);
    }

    public Task ReloadAsync() => _panel.ReloadAsync();
}
