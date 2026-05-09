using ClaudeViewer.Controls;
using ClaudeViewer.Models;
using ClaudeViewer.Services;
using DevExpress.XtraBars.Docking;
using DevExpress.XtraBars.Docking2010;
using DevExpress.XtraBars.Docking2010.Views;
using DevExpress.XtraBars.Docking2010.Views.Tabbed;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;

namespace ClaudeViewer;

public sealed class MainForm : XtraForm
{
    private const string ArtifactDirectoryEnvVar = "CLAUDE_VIEWER_DIR";

    private readonly DocumentManager _documentManager = new();
    private readonly TabbedView _tabbedView = new();
    private readonly DockManager _dockManager = new();
    private readonly DockPanel _leftPanel;
    private readonly GridControl _grid = new() { Dock = DockStyle.Fill };
    private readonly GridView _gridView = new();
    private readonly Dictionary<string, ArtifactForm> _openTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Settings _settings;
    private readonly LabelControl _folderLabel;
    private readonly bool _envVarOverride;

    private ArtifactWatcher _watcher;
    private string _artifactDirectory;

    public MainForm()
    {
        _settings = Settings.Load();

        var envOverride = Environment.GetEnvironmentVariable(ArtifactDirectoryEnvVar);
        _envVarOverride = !string.IsNullOrWhiteSpace(envOverride);
        _artifactDirectory = _envVarOverride ? envOverride! : _settings.ArtifactDirectory;

        Text = $"Claude Viewer  ·  {_artifactDirectory}";
        ClientSize = new Size(1400, 900);
        StartPosition = FormStartPosition.CenterScreen;
        IsMdiContainer = true;

        _dockManager.Form = this;
        _leftPanel = _dockManager.AddPanel(DockingStyle.Left);
        _leftPanel.Text = "Artifacts";
        _leftPanel.Width = 340;
        _leftPanel.Options.ShowCloseButton = false;
        _leftPanel.Options.ShowAutoHideButton = false;

        // Header strip: folder label + "Change…" button
        var header = new PanelControl
        {
            Dock = DockStyle.Top,
            Height = 38,
            BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder,
        };

        var changeBtn = new SimpleButton
        {
            Text = "Change…",
            Dock = DockStyle.Right,
            Width = 96,
            Margin = new Padding(0),
        };
        changeBtn.Click += (_, _) => ChooseFolder();
        if (_envVarOverride)
        {
            changeBtn.Enabled = false;
            changeBtn.ToolTip = $"Locked by {ArtifactDirectoryEnvVar} environment variable.";
        }

        _folderLabel = new LabelControl
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            AutoSizeMode = LabelAutoSizeMode.None,
            Padding = new Padding(10, 0, 6, 0),
            Text = _artifactDirectory,
        };
        _folderLabel.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Center;
        _folderLabel.ToolTip = _artifactDirectory;

        header.Controls.Add(_folderLabel);
        header.Controls.Add(changeBtn);

        // Order matters: add the Fill control first, then the Top control
        _leftPanel.Controls.Add(_grid);
        _leftPanel.Controls.Add(header);

        _grid.MainView = _gridView;
        _gridView.OptionsView.ShowGroupPanel = false;
        _gridView.OptionsView.ShowIndicator = false;
        _gridView.OptionsBehavior.Editable = false;
        _gridView.OptionsSelection.MultiSelect = false;
        _gridView.OptionsView.RowAutoHeight = false;
        _gridView.RowClick += (_, e) => OpenAtRow(e.RowHandle);
        _gridView.DoubleClick += (_, _) => OpenAtRow(_gridView.FocusedRowHandle);

        _documentManager.MdiParent = this;
        _documentManager.View = _tabbedView;
        _tabbedView.DocumentClosed += OnTabClosed;

        _watcher = CreateWatcher(_artifactDirectory);
        _grid.DataSource = _watcher.Artifacts;
        ConfigureGridColumns();

        FormClosed += (_, _) =>
        {
            _watcher.ArtifactUpdated -= OnArtifactUpdated;
            _watcher.Dispose();
        };
    }

    private ArtifactWatcher CreateWatcher(string directory)
    {
        var w = new ArtifactWatcher(directory, SynchronizationContext.Current!);
        w.ArtifactUpdated += OnArtifactUpdated;
        return w;
    }

    private void ConfigureGridColumns()
    {
        _gridView.Columns.Clear();

        var titleCol = _gridView.Columns.AddVisible(nameof(Artifact.Title), "Title");
        titleCol.Width = 180;

        var fileCol = _gridView.Columns.AddVisible(nameof(Artifact.FileName), "File");
        fileCol.Width = 140;

        var kindCol = _gridView.Columns.AddVisible(nameof(Artifact.KindDisplay), "Kind");
        kindCol.Width = 50;

        var modCol = _gridView.Columns.AddVisible(nameof(Artifact.ModifiedAt), "Modified");
        modCol.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
        modCol.DisplayFormat.FormatString = "HH:mm:ss";
        modCol.Width = 70;

        var sizeCol = _gridView.Columns.AddVisible(nameof(Artifact.SizeDisplay), "Size");
        sizeCol.Width = 60;
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder Claude Viewer should watch for artifacts",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(_artifactDirectory)
                ? _artifactDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SwitchTo(dialog.SelectedPath);
    }

    private void SwitchTo(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return;
        if (string.Equals(newPath, _artifactDirectory, StringComparison.OrdinalIgnoreCase)) return;

        _watcher.ArtifactUpdated -= OnArtifactUpdated;
        _watcher.Dispose();

        _artifactDirectory = newPath;
        _settings.ArtifactDirectory = newPath;
        _settings.Save();

        Text = $"Claude Viewer  ·  {_artifactDirectory}";
        _folderLabel.Text = newPath;
        _folderLabel.ToolTip = newPath;

        _watcher = CreateWatcher(newPath);
        _grid.DataSource = _watcher.Artifacts;
    }

    private void OpenAtRow(int rowHandle)
    {
        if (rowHandle < 0) return;
        if (_gridView.GetRow(rowHandle) is not Artifact a) return;
        OpenArtifact(a);
    }

    private void OpenArtifact(Artifact a)
    {
        if (_openTabs.TryGetValue(a.FullPath, out var existing) && !existing.IsDisposed)
        {
            ActivateForm(existing);
            _ = existing.LoadAsync(a);
            return;
        }

        var form = new ArtifactForm
        {
            MdiParent = this,
            Text = a.Title ?? a.FileName,
        };
        _openTabs[a.FullPath] = form;
        form.FormClosed += (_, _) => _openTabs.Remove(a.FullPath);
        form.Show();
        _ = form.LoadAsync(a);
    }

    private void ActivateForm(Form form)
    {
        foreach (BaseDocument doc in _tabbedView.Documents)
        {
            if (ReferenceEquals(doc.Form, form))
            {
                _tabbedView.Controller.Activate(doc);
                return;
            }
        }
    }

    private void OnArtifactUpdated(Artifact a)
    {
        if (_openTabs.TryGetValue(a.FullPath, out var form) && !form.IsDisposed)
            _ = form.LoadAsync(a);
    }

    private void OnTabClosed(object? sender, DocumentEventArgs e)
    {
        if (e.Document.Form is ArtifactForm af && af.Current is not null)
            _openTabs.Remove(af.Current.FullPath);
    }
}
