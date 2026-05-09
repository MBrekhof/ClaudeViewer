using ClaudeViewer.Models;
using ClaudeViewer.Services;
using DevExpress.XtraEditors;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClaudeViewer.Controls;

public sealed class ArtifactForm : XtraForm
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private bool _ready;

    public Artifact? Current { get; private set; }

    public ArtifactForm()
    {
        Text = "Artifact";
        Size = new Size(900, 700);
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
        Text = a.Title ?? a.FileName;

        if (!_ready)
        {
            await _web.EnsureCoreWebView2Async();
        }

        switch (a.Kind)
        {
            case ArtifactKind.Html:
                _web.CoreWebView2.Navigate(new Uri(a.FullPath).AbsoluteUri);
                break;
            case ArtifactKind.Markdown:
                var md = await ReadAllTextWithRetryAsync(a.FullPath);
                _web.NavigateToString(MarkdownRenderer.ToHtml(md, a.Title ?? a.FileName));
                break;
            default:
                _web.NavigateToString("<html><body style='font-family:sans-serif;padding:32px'>Unsupported file type.</body></html>");
                break;
        }
    }

    public Task ReloadAsync() =>
        Current is null ? Task.CompletedTask : LoadAsync(Current);

    private static async Task<string> ReadAllTextWithRetryAsync(string path)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                return await reader.ReadToEndAsync();
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(60 * (attempt + 1));
            }
        }
        return string.Empty;
    }

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
