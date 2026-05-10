using ClaudeViewer.Models;
using ClaudeViewer.Services;
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

    public Task LoadAsync(Artifact left, Artifact right)
    {
        Text = $"Compare · {left.FileName}  ⇄  {right.FileName}";
        _leftLabel.Text = FormatLabel(left);
        _rightLabel.Text = FormatLabel(right);
        _leftLabel.ToolTip = left.FullPath;
        _rightLabel.ToolTip = right.FullPath;

        return RenderAsync(left, right, changedOnly: null);
    }

    public Task RefreshIfMatchesAsync(Artifact a)
    {
        var left = LeftArtifact;
        var right = RightArtifact;
        if (left is null || right is null) return Task.CompletedTask;

        var leftMatch = string.Equals(left.FullPath, a.FullPath, StringComparison.OrdinalIgnoreCase);
        var rightMatch = string.Equals(right.FullPath, a.FullPath, StringComparison.OrdinalIgnoreCase);
        if (!leftMatch && !rightMatch) return Task.CompletedTask;

        var newLeft = leftMatch ? a : left;
        var newRight = rightMatch ? a : right;

        if (leftMatch) _leftLabel.Text = FormatLabel(a);
        if (rightMatch) _rightLabel.Text = FormatLabel(a);

        return RenderAsync(newLeft, newRight, changedOnly: a);
    }

    private async Task RenderAsync(Artifact left, Artifact right, Artifact? changedOnly)
    {
        // Two MD files → side-by-side line diff.
        // Anything else (HTML/HTML, mixed, unsupported) → straight render, mirroring single-tab behaviour.
        if (left.Kind == ArtifactKind.Markdown && right.Kind == ArtifactKind.Markdown)
        {
            var leftText = await FileReader.ReadAllTextWithRetryAsync(left.FullPath);
            var rightText = await FileReader.ReadAllTextWithRetryAsync(right.FullPath);
            var (leftHtml, rightHtml) = DiffRenderer.Render(
                leftText,
                rightText,
                left.Title ?? left.FileName,
                right.Title ?? right.FileName);
            await Task.WhenAll(
                _left.LoadHtmlAsync(leftHtml, left),
                _right.LoadHtmlAsync(rightHtml, right));
            return;
        }

        var tasks = new List<Task>(2);
        if (changedOnly is null ||
            string.Equals(changedOnly.FullPath, left.FullPath, StringComparison.OrdinalIgnoreCase))
            tasks.Add(_left.LoadAsync(left));
        if (changedOnly is null ||
            string.Equals(changedOnly.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase))
            tasks.Add(_right.LoadAsync(right));
        await Task.WhenAll(tasks);
    }

    public bool Mentions(string fullPath) =>
        (LeftArtifact is { } l && string.Equals(l.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)) ||
        (RightArtifact is { } r && string.Equals(r.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));

    private static string FormatLabel(Artifact a) =>
        a.Title is null ? a.FileName : $"{a.FileName}  ·  {a.Title}";
}
