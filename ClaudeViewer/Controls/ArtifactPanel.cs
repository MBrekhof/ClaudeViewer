using ClaudeViewer.Models;
using ClaudeViewer.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClaudeViewer.Controls;

public sealed class ArtifactPanel : UserControl
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private bool _ready;

    public Artifact? Current { get; private set; }

    public ArtifactPanel()
    {
        Controls.Add(_web);
        _web.CoreWebView2InitializationCompleted += OnInitialized;
        _ = _web.EnsureCoreWebView2Async();
    }

    private void OnInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        _ready = e.IsSuccess;
        if (_ready && Current is not null)
            _ = LoadAsync(Current);
    }

    public async Task LoadAsync(Artifact a)
    {
        Current = a;

        if (!_ready)
            await _web.EnsureCoreWebView2Async();

        switch (a.Kind)
        {
            case ArtifactKind.Html:
                _web.CoreWebView2.Navigate(new Uri(a.FullPath).AbsoluteUri);
                break;
            case ArtifactKind.Markdown:
                var md = await FileReader.ReadAllTextWithRetryAsync(a.FullPath);
                _web.NavigateToString(MarkdownRenderer.ToHtml(md, a.Title ?? a.FileName));
                break;
            default:
                _web.NavigateToString(
                    "<html><body style='font-family:sans-serif;padding:32px'>Unsupported file type.</body></html>");
                break;
        }
    }

    public async Task LoadHtmlAsync(string html, Artifact source)
    {
        Current = source;

        if (!_ready)
            await _web.EnsureCoreWebView2Async();

        _web.NavigateToString(html);
    }

    public Task ReloadAsync() =>
        Current is null ? Task.CompletedTask : LoadAsync(Current);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _web.CoreWebView2InitializationCompleted -= OnInitialized;
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}
