using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using ClaudeViewer.Models;
using ClaudeViewer.Services;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using DevExpress.XtraTreeList.Columns;

namespace ClaudeViewer.Controls;

public sealed class ConfigPanel : XtraUserControl
{
    private readonly TreeList _treeList = new();
    private readonly SimpleButton _reloadBtn = new() { Text = "Reload", Width = 90 };
    private readonly LabelControl _rootLabel = new() { AutoSizeMode = LabelAutoSizeMode.None, Dock = DockStyle.Fill };

    private string? _currentRoot;

    public ConfigPanel()
    {
        Dock = DockStyle.Fill;

        var header = new PanelControl
        {
            Dock = DockStyle.Top,
            Height = 38,
            BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder,
        };
        _reloadBtn.Dock = DockStyle.Right;
        _reloadBtn.Click += async (_, _) => await ReloadAsync();
        header.Controls.Add(_rootLabel);
        header.Controls.Add(_reloadBtn);

        _treeList.Dock = DockStyle.Fill;
        _treeList.OptionsBehavior.Editable = false;
        _treeList.OptionsView.ShowAutoFilterRow = true;
        _treeList.KeyFieldName = nameof(MergedSetting.Id);
        _treeList.ParentFieldName = nameof(MergedSetting.ParentId);
        AddColumn(nameof(MergedSetting.Key), "Key", 220);
        AddColumn(nameof(MergedSetting.Managed), "Managed", 100);
        AddColumn(nameof(MergedSetting.User), "User", 100);
        AddColumn(nameof(MergedSetting.Project), "Project", 100);
        AddColumn(nameof(MergedSetting.Local), "Local", 100);
        AddColumn(nameof(MergedSetting.Effective), "Effective", 160);
        AddColumn(nameof(MergedSetting.Winner), "Winner", 90);
        _treeList.DoubleClick += OnDoubleClick;

        Controls.Add(_treeList);
        Controls.Add(header);
    }

    public async Task LoadAsync(string root)
    {
        _currentRoot = root;
        _rootLabel.Text = $"Project root: {root}";
        _reloadBtn.Enabled = false;
        try
        {
            var list = await Task.Run(() =>
            {
                var managed = ClaudeSettingsReader.Read(@"C:\ProgramData\ClaudeCode\managed-settings.json");
                var user = ClaudeSettingsReader.Read(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"));
                var project = ClaudeSettingsReader.Read(Path.Combine(root, ".claude", "settings.json"));
                var local = ClaudeSettingsReader.Read(Path.Combine(root, ".claude", "settings.local.json"));
                return ClaudeSettingsMerger.Merge(managed, user, project, local);
            });

            _treeList.DataSource = list;
            _treeList.ExpandAll();
        }
        finally
        {
            _reloadBtn.Enabled = true;
        }
    }

    private Task ReloadAsync() => _currentRoot is null ? Task.CompletedTask : LoadAsync(_currentRoot);

    private void AddColumn(string fieldName, string caption, int width)
    {
        var col = new TreeListColumn
        {
            FieldName = fieldName,
            Caption = caption,
            Width = width,
            VisibleIndex = _treeList.Columns.Count,
        };
        _treeList.Columns.Add(col);
    }

    private void OnDoubleClick(object? sender, EventArgs e)
    {
        var node = _treeList.FocusedNode;
        if (node is null) return;
        if (_treeList.GetDataRecordByNode(node) is not MergedSetting row) return;
        if (row.IsGroup || string.IsNullOrEmpty(row.Winner) || _currentRoot is null) return;

        var path = ResolveScopeFile(row.Winner, _currentRoot);
        if (!File.Exists(path)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            XtraMessageBox.Show($"Couldn't open {path}: {ex.Message}");
        }
    }

    private static string ResolveScopeFile(string scope, string root) => scope switch
    {
        "Managed" => @"C:\ProgramData\ClaudeCode\managed-settings.json",
        "User" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"),
        "Project" => Path.Combine(root, ".claude", "settings.json"),
        "Local" => Path.Combine(root, ".claude", "settings.local.json"),
        _ => "",
    };
}
