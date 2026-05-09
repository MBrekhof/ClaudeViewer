using ClaudeViewer.Models;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;

namespace ClaudeViewer.Controls;

public sealed class CompareForm : XtraForm
{
    private readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical,  // vertical splitter → panels side by side
        SplitterWidth = 6,
    };

    private readonly ArtifactPanel _left = new() { Dock = DockStyle.Fill };
    private readonly ArtifactPanel _right = new() { Dock = DockStyle.Fill };
    private readonly LabelControl _leftLabel = new();
    private readonly LabelControl _rightLabel = new();

    public Artifact? LeftArtifact => _left.Current;
    public Artifact? RightArtifact => _right.Current;

    public CompareForm()
    {
        Text = "Compare";
        Size = new Size(1300, 800);

        _split.Panel1.Controls.Add(BuildPane(_left, _leftLabel));
        _split.Panel2.Controls.Add(BuildPane(_right, _rightLabel));
        Controls.Add(_split);

        _split.HandleCreated += (_, _) =>
            _split.SplitterDistance = _split.Width / 2;
    }

    private static Control BuildPane(ArtifactPanel panel, LabelControl label)
    {
        var host = new PanelControl
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyles.NoBorder,
        };

        var header = new PanelControl
        {
            Dock = DockStyle.Top,
            Height = 28,
            BorderStyle = BorderStyles.NoBorder,
        };

        label.Dock = DockStyle.Fill;
        label.AutoSizeMode = LabelAutoSizeMode.None;
        label.AutoEllipsis = true;
        label.Padding = new Padding(10, 0, 6, 0);
        label.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Center;
        header.Controls.Add(label);

        host.Controls.Add(panel);
        host.Controls.Add(header);
        return host;
    }

    public async Task LoadAsync(Artifact left, Artifact right)
    {
        Text = $"Compare · {left.FileName}  ⇄  {right.FileName}";
        _leftLabel.Text = FormatLabel(left);
        _rightLabel.Text = FormatLabel(right);
        _leftLabel.ToolTip = left.FullPath;
        _rightLabel.ToolTip = right.FullPath;

        await Task.WhenAll(_left.LoadAsync(left), _right.LoadAsync(right));
    }

    public async Task RefreshIfMatchesAsync(Artifact a)
    {
        if (LeftArtifact is { } l && string.Equals(l.FullPath, a.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _leftLabel.Text = FormatLabel(a);
            await _left.LoadAsync(a);
        }
        if (RightArtifact is { } r && string.Equals(r.FullPath, a.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _rightLabel.Text = FormatLabel(a);
            await _right.LoadAsync(a);
        }
    }

    public bool Mentions(string fullPath) =>
        (LeftArtifact is { } l && string.Equals(l.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)) ||
        (RightArtifact is { } r && string.Equals(r.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));

    private static string FormatLabel(Artifact a) =>
        a.Title is null ? a.FileName : $"{a.FileName}  ·  {a.Title}";
}
