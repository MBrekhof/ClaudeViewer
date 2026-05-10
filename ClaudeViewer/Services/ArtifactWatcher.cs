using System.ComponentModel;
using System.Text.RegularExpressions;
using ClaudeViewer.Models;

namespace ClaudeViewer.Services;

public sealed partial class ArtifactWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly SynchronizationContext _ui;
    private readonly string _root;
    private readonly bool _recursive;

    public BindingList<Artifact> Artifacts { get; } = new();

    public event Action<Artifact>? ArtifactUpdated;

    public ArtifactWatcher(string rootDirectory, SynchronizationContext uiContext, bool recursive)
    {
        _root = rootDirectory;
        _ui = uiContext;
        _recursive = recursive;
        Directory.CreateDirectory(rootDirectory);

        _watcher = new FileSystemWatcher(rootDirectory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = recursive,
            EnableRaisingEvents = true,
        };
        _watcher.Created += OnTouched;
        _watcher.Changed += OnTouched;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;

        SeedExisting();
    }

    private void SeedExisting()
    {
        var search = _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = new DirectoryInfo(_root)
            .EnumerateFiles("*.*", search)
            .Where(IsTracked)
            .OrderByDescending(f => f.LastWriteTime);

        foreach (var f in files)
            Artifacts.Add(MakeArtifact(f));
    }

    private static bool IsTracked(FileInfo f) =>
        f.Extension.ToLowerInvariant() is ".html" or ".htm" or ".md" or ".markdown";

    private Artifact MakeArtifact(FileInfo f)
    {
        var kind = f.Extension.ToLowerInvariant() switch
        {
            ".html" or ".htm" => ArtifactKind.Html,
            ".md" or ".markdown" => ArtifactKind.Markdown,
            _ => ArtifactKind.Other,
        };
        return new Artifact
        {
            FileName = f.Name,
            FullPath = f.FullName,
            ModifiedAt = f.LastWriteTime,
            Kind = kind,
            SizeBytes = f.Length,
            Title = TryReadTitle(f, kind),
            Folder = RelativeFolder(f),
        };
    }

    private string RelativeFolder(FileInfo f)
    {
        var dir = f.DirectoryName ?? _root;
        var rel = Path.GetRelativePath(_root, dir);
        return rel == "." ? "" : rel;
    }

    private static string? TryReadTitle(FileInfo f, ArtifactKind kind)
    {
        try
        {
            using var stream = new FileStream(
                f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var head = new char[4096];
            var read = reader.Read(head, 0, head.Length);
            var text = new string(head, 0, read);

            if (kind == ArtifactKind.Html)
            {
                var m = TitleTagRegex().Match(text);
                if (m.Success) return DecodeEntities(m.Groups[1].Value).Trim();
            }
            else if (kind == ArtifactKind.Markdown)
            {
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("# "))
                        return trimmed[2..].Trim();
                }
            }
        }
        catch
        {
            // file may be locked mid-write — title will fill in next change event
        }
        return null;
    }

    private static string DecodeEntities(string s) =>
        System.Net.WebUtility.HtmlDecode(s);

    private void OnTouched(object sender, FileSystemEventArgs e)
    {
        var fi = new FileInfo(e.FullPath);
        if (!IsTracked(fi)) return;
        Post(() => Upsert(fi));
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Post(() =>
        {
            Remove(e.OldFullPath);
            var fi = new FileInfo(e.FullPath);
            if (fi.Exists && IsTracked(fi)) Upsert(fi);
        });
    }

    private void OnDeleted(object sender, FileSystemEventArgs e) =>
        Post(() => Remove(e.FullPath));

    private void Upsert(FileInfo fi)
    {
        if (!fi.Exists) return;
        try
        {
            var fresh = MakeArtifact(fi);
            var existing = Artifacts.FirstOrDefault(
                a => string.Equals(a.FullPath, fi.FullName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) Artifacts.Remove(existing);
            Artifacts.Insert(0, fresh);
            ArtifactUpdated?.Invoke(fresh);
        }
        catch
        {
            // race during write — next event will retry
        }
    }

    private void Remove(string fullPath)
    {
        var existing = Artifacts.FirstOrDefault(
            a => string.Equals(a.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Artifacts.Remove(existing);
    }

    private void Post(Action action) => _ui.Post(_ => action(), null);

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }

    [GeneratedRegex(@"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTagRegex();
}
