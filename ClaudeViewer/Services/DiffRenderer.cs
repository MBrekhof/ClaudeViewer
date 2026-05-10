using System.Net;
using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace ClaudeViewer.Services;

public static class DiffRenderer
{
    private static readonly SideBySideDiffBuilder Builder = new(new Differ());

    public static (string LeftHtml, string RightHtml) Render(
        string leftText, string rightText, string leftTitle, string rightTitle)
    {
        var model = Builder.BuildDiffModel(leftText ?? string.Empty, rightText ?? string.Empty);
        return (
            BuildSide(model.OldText.Lines, leftTitle),
            BuildSide(model.NewText.Lines, rightTitle));
    }

    private static string BuildSide(IList<DiffPiece> lines, string title)
    {
        var sb = new StringBuilder(64 * lines.Count + 1024);
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>");
        sb.Append(WebUtility.HtmlEncode(title ?? "Diff"));
        sb.Append("</title><style>");
        sb.Append("""
            * { box-sizing: border-box; }
            body {
              margin: 0; padding: 0;
              background: #f4ecdd; color: #1a1612;
              font-family: 'JetBrains Mono','Cascadia Mono',Consolas,monospace;
              font-size: 13px; line-height: 1.55;
            }
            .row { display: flex; align-items: stretch; min-height: 1.55em; }
            .ln {
              flex: 0 0 48px;
              padding: 0 8px; text-align: right;
              color: #8a7a68; user-select: none;
              border-right: 1px solid #e0d4ba;
              background: rgba(0,0,0,0.02);
            }
            .text {
              flex: 1;
              padding: 0 12px;
              white-space: pre-wrap;
              word-break: break-word;
              overflow-wrap: anywhere;
            }
            .row.inserted   { background: #d4f4dd; }
            .row.deleted    { background: #fde0e0; }
            .row.modified   { background: #fff5b1; }
            .row.unchanged  { }
            .row.imaginary  { background: rgba(0,0,0,0.03); color: #b0a08e; }
            .row.imaginary .ln { color: #d6c9b1; }
            """);
        sb.Append("</style></head><body>");

        foreach (var line in lines)
        {
            var cls = line.Type switch
            {
                ChangeType.Inserted => "inserted",
                ChangeType.Deleted => "deleted",
                ChangeType.Modified => "modified",
                ChangeType.Imaginary => "imaginary",
                _ => "unchanged",
            };
            sb.Append("<div class=\"row ").Append(cls).Append("\"><span class=\"ln\">");
            sb.Append(line.Position?.ToString() ?? string.Empty);
            sb.Append("</span><span class=\"text\">");
            sb.Append(WebUtility.HtmlEncode(line.Text ?? string.Empty));
            sb.Append("</span></div>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }
}
