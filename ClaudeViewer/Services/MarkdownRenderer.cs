using Markdig;

namespace ClaudeViewer.Services;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static string ToHtml(string markdown, string? title = null)
    {
        var body = Markdown.ToHtml(markdown, Pipeline);
        var pageTitle = string.IsNullOrWhiteSpace(title) ? "Markdown" : title;
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8">
              <title>{{System.Net.WebUtility.HtmlEncode(pageTitle)}}</title>
              <style>
                :root {
                  --paper: #f4ecdd;
                  --paper-deep: #e9dfca;
                  --ink: #1a1612;
                  --ink-soft: #4a3f35;
                  --accent: #6e1f2a;
                }
                * { box-sizing: border-box; }
                body {
                  font-family: 'Newsreader', Georgia, 'Times New Roman', serif;
                  background: var(--paper);
                  color: var(--ink);
                  max-width: 780px;
                  margin: 32px auto;
                  padding: 0 28px 64px;
                  line-height: 1.62;
                  font-size: 17px;
                }
                h1, h2, h3, h4 {
                  font-family: 'Fraunces', Georgia, serif;
                  letter-spacing: -0.012em;
                  line-height: 1.15;
                  margin: 1.6em 0 0.5em;
                  font-weight: 600;
                }
                h1 { font-size: 38px; border-bottom: 1px solid var(--ink); padding-bottom: 8px; }
                h2 { font-size: 28px; }
                h3 { font-size: 22px; color: var(--ink-soft); }
                p { margin: 0 0 1em; }
                a { color: var(--accent); text-decoration: underline; text-underline-offset: 3px; }
                code {
                  background: var(--ink); color: #d4c8b6;
                  padding: 1px 6px; border-radius: 2px;
                  font-family: 'JetBrains Mono', 'Cascadia Mono', Consolas, monospace;
                  font-size: 0.88em;
                }
                pre {
                  background: var(--ink); color: #d4c8b6;
                  padding: 14px 18px; margin: 1em 0;
                  border-left: 3px solid var(--accent);
                  overflow-x: auto;
                  font-family: 'JetBrains Mono', 'Cascadia Mono', Consolas, monospace;
                  font-size: 13.5px; line-height: 1.55;
                }
                pre code { background: transparent; padding: 0; font-size: inherit; }
                blockquote {
                  border-left: 3px solid var(--accent);
                  padding: 4px 0 4px 18px; margin: 1em 0;
                  color: var(--ink-soft); font-style: italic;
                }
                table { border-collapse: collapse; width: 100%; margin: 1em 0; }
                td, th { border: 1px solid var(--ink); padding: 8px 12px; text-align: left; }
                th { background: var(--paper-deep); }
                hr { border: none; border-top: 1px solid var(--ink); margin: 2em 0; }
                ul, ol { padding-left: 1.6em; }
                li { margin: 0.25em 0; }
                img { max-width: 100%; height: auto; }
              </style>
            </head>
            <body>
              {{body}}
            </body>
            </html>
            """;
    }
}
