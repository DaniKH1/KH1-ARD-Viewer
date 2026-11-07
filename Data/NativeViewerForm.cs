using System.Diagnostics;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Kh1Import;

internal sealed class NativeViewerForm : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private readonly TreeView _models = new();
    private readonly FlowLayoutPanel _textures = new();
    private readonly FlowLayoutPanel _archiveActions = new();
    private readonly FlowLayoutPanel _textureGallery = new();
    private readonly NativeGlViewport _viewport = new();
    private readonly Label _status = new();
    private readonly Label _loadProgress = new();
    private readonly Label _stats = new();
    private readonly Label _exportToast = new();
    private readonly NativeActionButton _export = new();
    private readonly NativeActionButton _exportMset = new();
    private readonly NativeActionButton _bones = new();
    private readonly NativeActionButton _animations = new();
    private readonly NativeActionButton _msetPlay = new();
    private readonly TrackBar _msetTimeline = new();
    private readonly Label _msetTime = new();
    private readonly System.Windows.Forms.Timer _exportToastTimer = new() { Interval = 50 };
    private readonly NativeActionButton _resetCamera = new();
    private readonly System.Windows.Forms.Timer _msetPlaybackTimer = new() { Interval = 16 };
    private readonly Dictionary<ArdViewerModel, TreeNode> _nodes = new();
    private readonly Dictionary<string, TreeNode> _archiveCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ArdViewerModel> _allModels = new();
    private System.Drawing.Icon? _applicationIcon;
    private Font? _categoryFont;
    private bool _isLoading;
    private string? _lastFolder;
    private string? _settingsWarning;
    private ArdViewerModel? _selected;
    private int _activeMsetSection = -1;
    private float _activeMsetFrame;
    private long _msetPlaybackStart;
    private bool _updatingMsetTimeline;
    private bool _updatingMsetAnimationList;
    private bool _hasMsetTimelineSpace;
    private bool _autoPlay;
    private bool _loop = true;
    private bool _allowAnimationsWindowClose;
    private DateTime _exportToastShownAt;
    private Form? _animationsWindow;
    private ListBox? _animationsList;

    private sealed record MsetAnimationItem(int SectionIndex, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private static string LastFolderSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KH1 ARD Viewer", "settings.json");

    private static string LegacyLastFolderSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KH1 ARD Viewer", "last-folder.txt");

    private sealed class ViewerSettings
    {
        public string? LastFolder { get; set; }
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    internal NativeViewerForm(string? initialArchive = null)
    {
        Text = "KH1 ARD Viewer";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(850, 560);
        StartPosition = FormStartPosition.CenterScreen;
        ShowIcon = true;
        BackColor = Color.FromArgb(16, 19, 25);
        ForeColor = Color.FromArgb(229, 233, 240);
        Font = new Font("Segoe UI", 9F);
        _applicationIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (_applicationIcon is not null) Icon = _applicationIcon;
        _lastFolder = ReadLastFolder();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(24, 29, 37),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        BuildSidebar(layout);
        BuildViewport(layout);
        layout.SetCellPosition(layout.Controls[0], new TableLayoutPanelCellPosition(0, 0));
        layout.SetCellPosition(layout.Controls[1], new TableLayoutPanelCellPosition(1, 0));
        Resize += (_, _) => UpdateAnimationsWindowBounds();
        LocationChanged += (_, _) => UpdateAnimationsWindowBounds();
        _models.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is ArdViewerModel model) SelectModel(model);
        };
        _models.AfterExpand += (_, e) => UpdateArchiveNodeLabel(e.Node);
        _models.AfterCollapse += (_, e) => UpdateArchiveNodeLabel(e.Node);
        _models.NodeMouseClick += (_, e) =>
        {
            if (e.Node == null || e.Node.Nodes.Count == 0) return;
            if (e.Node.IsExpanded) e.Node.Collapse();
            else e.Node.Expand();
        };
        _status.Text = _settingsWarning ?? "";
        Shown += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(initialArchive))
            {
                if (File.Exists(initialArchive))
                {
                    string? folder = Path.GetDirectoryName(Path.GetFullPath(initialArchive));
                    if (folder != null) RememberFolder(folder);
                }
                await LoadArchivesAsync([initialArchive]);
            }
            else
            {
                await LoadLastFolderAsync();
            }
        };
    }

    private void BuildSidebar(TableLayoutPanel parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12),
            BackColor = Color.FromArgb(24, 29, 37)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Dock = DockStyle.Fill;
        parent.Controls.Add(layout, 0, 0);

        _archiveActions.AutoSize = true;
        _archiveActions.WrapContents = false;
        _archiveActions.Margin = Padding.Empty;
        _archiveActions.Anchor = AnchorStyles.None;
        Button openFile = MakeButton("Open .ard");
        Button openFolder = MakeButton("Open folder...");
        openFolder.Margin = Padding.Empty;
        openFile.Click += async (_, _) => await OpenArchiveDialogAsync();
        openFolder.Click += async (_, _) => await OpenFolderDialogAsync();
        _archiveActions.Controls.Add(openFile);
        _archiveActions.Controls.Add(openFolder);
        layout.Controls.Add(_archiveActions, 0, 0);

        _models.Dock = DockStyle.Fill;
        _models.BackColor = Color.FromArgb(16, 20, 27);
        _models.ForeColor = Color.FromArgb(220, 228, 239);
        _models.BorderStyle = BorderStyle.FixedSingle;
        _models.HideSelection = false;
        _models.FullRowSelect = true;
        _models.ShowLines = false;
        _models.ShowRootLines = false;
        _models.ShowPlusMinus = false;
        _models.HandleCreated += (_, _) => ApplyDarkScrollbars(_models);
        layout.Controls.Add(_models, 0, 1);

        layout.Controls.Add(new Label
        {
            Text = "TEXTURES",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(170, 180, 195),
            Font = new Font(Font.FontFamily, Font.Size + 1F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 2);

        _textures.Dock = DockStyle.Fill;
        _textures.AutoScroll = true;
        _textures.WrapContents = true;
        _textures.BackColor = Color.FromArgb(16, 20, 27);
        _textures.HandleCreated += (_, _) => ApplyDarkScrollbars(_textures);
        layout.Controls.Add(_textures, 0, 3);

        _loadProgress.Dock = DockStyle.Fill;
        _loadProgress.ForeColor = Color.FromArgb(183, 197, 216);
        _loadProgress.TextAlign = ContentAlignment.MiddleCenter;
        _loadProgress.Visible = false;
        layout.Controls.Add(_loadProgress, 0, 4);

        _status.Dock = DockStyle.Bottom;
        _status.Height = 40;
        _status.ForeColor = Color.FromArgb(183, 197, 216);
        _status.Padding = new Padding(6);
        layout.Controls.Add(_status, 0, 5);
    }

    private static void ApplyDarkScrollbars(Control control)
    {
        SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        int enabled = 1;
        int result = DwmSetWindowAttribute(control.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        if (result != 0)
            DwmSetWindowAttribute(control.Handle, DwmwaUseImmersiveDarkMode - 1, ref enabled, sizeof(int));
    }

    private static void UpdateArchiveNodeLabel(TreeNode? node)
    {
        if (node == null || node.Nodes.Count == 0) return;
        string? label = node.Tag switch
        {
            ArchiveCategoryTag category => category.Name,
            string archivePath => Path.GetFileName(archivePath),
            _ => null
        };
        if (label is not null)
            node.Text = $"{(node.IsExpanded ? "▾" : "▸")} {label}";
    }

    private sealed record ArchiveCategoryTag(string Name);

    private void BuildViewport(TableLayoutPanel parent)
    {
        var stage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 17, 22) };
        stage.Dock = DockStyle.Fill;
        parent.Controls.Add(stage, 1, 0);
        _viewport.Dock = DockStyle.Fill;
        stage.Controls.Add(_viewport);

        _exportToast.Size = new Size(440, 34);
        _exportToast.Anchor = AnchorStyles.Top;
        _exportToast.ForeColor = Color.FromArgb(89, 220, 133);
        _exportToast.BackColor = Color.Transparent;
        _exportToast.Font = new Font(Font, FontStyle.Bold);
        _exportToast.TextAlign = ContentAlignment.MiddleCenter;
        _exportToast.Visible = false;
        stage.Controls.Add(_exportToast);
        _exportToast.BringToFront();
        _exportToastTimer.Tick += (_, _) => FadeExportToast();

        _stats.Size = new Size(170, 116);
        _stats.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _stats.BackColor = Color.FromArgb(24, 29, 37);
        _stats.ForeColor = Color.FromArgb(197, 207, 221);
        _stats.Padding = new Padding(8);
        _stats.BorderStyle = BorderStyle.FixedSingle;
        _stats.TextAlign = ContentAlignment.MiddleLeft;
        _stats.Visible = false;
        stage.Controls.Add(_stats);
        _stats.BringToFront();

        _export.Text = "Export";
        StyleButton(_export);
        _export.Size = new Size(104, 38);
        _export.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _export.Location = new Point(14, 14);
        _export.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _export.Enabled = false;
        _export.Visible = false;
        _export.Click += (_, _) => ExportSelected();
        stage.Controls.Add(_export);
        _export.BringToFront();

        _exportMset.Text = "Export selected MSETS...";
        StyleButton(_exportMset);
        _exportMset.Size = new Size(190, 38);
        _exportMset.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _exportMset.Location = new Point(14, 58);
        _exportMset.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _exportMset.Visible = false;
        _exportMset.Click += (_, _) => ExportSelectedMsets();
        stage.Controls.Add(_exportMset);
        _exportMset.BringToFront();

        _bones.Text = "Show bones";
        StyleButton(_bones);
        _bones.Size = new Size(124, 38);
        _bones.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _bones.Location = new Point(126, 14);
        _bones.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _bones.Visible = false;
        _bones.Click += (_, _) =>
        {
            _bones.Text = _viewport.ToggleShowBones() ? "Hide bones" : "Show bones";
        };
        stage.Controls.Add(_bones);
        _bones.BringToFront();

        _animations.Text = "Animations";
        StyleButton(_animations);
        _animations.Size = new Size(126, 38);
        _animations.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _animations.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _animations.Visible = false;
        _animations.Click += (_, _) => ShowMsetAnimations();
        stage.Controls.Add(_animations);
        _animations.BringToFront();

        _msetPlay.Text = "Play";
        StyleButton(_msetPlay);
        _msetPlay.Size = new Size(76, 38);
        _msetPlay.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _msetPlay.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        _msetPlay.Visible = false;
        _msetPlay.Click += (_, _) => ToggleMsetPlayback();
        stage.Controls.Add(_msetPlay);
        _msetPlay.BringToFront();

        _msetTime.AutoSize = false;
        _msetTime.Size = new Size(82, 28);
        _msetTime.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        _msetTime.ForeColor = Color.FromArgb(197, 207, 221);
        _msetTime.BackColor = Color.Transparent;
        _msetTime.TextAlign = ContentAlignment.MiddleCenter;
        _msetTime.Visible = false;
        stage.Controls.Add(_msetTime);
        _msetTime.BringToFront();

        _msetTimeline.Minimum = 0;
        _msetTimeline.Maximum = 1000;
        _msetTimeline.SmallChange = 1;
        _msetTimeline.LargeChange = 50;
        _msetTimeline.TickStyle = TickStyle.None;
        _msetTimeline.Height = 38;
        _msetTimeline.BackColor = stage.BackColor;
        _msetTimeline.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _msetTimeline.Visible = false;
        _msetTimeline.MouseDown += (_, _) =>
        {
            if (_msetPlaybackTimer.Enabled) StopMsetPlayback();
        };
        _msetTimeline.ValueChanged += (_, _) =>
        {
            if (_updatingMsetTimeline || _selected == null ||
                _activeMsetSection < 0 || _activeMsetSection >= _selected.MsetSections.Count)
                return;
            int sliderValue = _msetTimeline.Value;
            if (_msetPlaybackTimer.Enabled)
            {
                _msetPlaybackTimer.Stop();
                _msetPlay.Text = "Play";
            }
            ArdViewerMsetSection section = _selected.MsetSections[_activeMsetSection];
            _activeMsetFrame = sliderValue / (float)_msetTimeline.Maximum * section.FrameEnd;
            _viewport.SetMsetAnimationFrame(_activeMsetSection, _activeMsetFrame);
            UpdateMsetTimeline(section);
            UpdateMsetControls();
        };
        stage.Controls.Add(_msetTimeline);
        _msetTimeline.BringToFront();
        _msetPlaybackTimer.Tick += (_, _) => AdvanceMsetPose();

        _resetCamera.Text = "Reset camera";
        _resetCamera.Size = new Size(162, 38);
        _resetCamera.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _resetCamera.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        StyleButton(_resetCamera);
        _resetCamera.Click += (_, _) => _viewport.ResetCamera();
        stage.Controls.Add(_resetCamera);
        _resetCamera.BringToFront();
        stage.Resize += (_, _) => LayoutViewportControls(stage);
        LayoutViewportControls(stage);
    }

    private void LayoutViewportControls(Panel stage)
    {
        int bottom = Math.Max(10, stage.ClientSize.Height - 48);
        _stats.Location = new Point(12, Math.Max(10, stage.ClientSize.Height - _stats.Height - 16));
        _animations.Location = new Point(stage.ClientSize.Width - _animations.Width - 12, bottom);
        _msetPlay.Location = new Point(_animations.Left - _msetPlay.Width - 8, bottom);
        _msetTime.Location = new Point(_msetPlay.Left - _msetTime.Width - 8, bottom + 5);
        if (_animationsWindow is { IsDisposed: false, Visible: true })
            PositionAnimationsWindow(_animationsWindow);

        int timelineWidth = Math.Min(260, _msetTime.Left - 8 - 12);
        _hasMsetTimelineSpace = timelineWidth >= 80;
        _msetTimeline.Width = Math.Max(1, timelineWidth);
        _msetTimeline.Location = new Point(_msetTime.Left - _msetTimeline.Width - 8, bottom);
        _msetTimeline.Visible = _hasMsetTimelineSpace && _activeMsetSection >= 0;
        _msetTime.Visible = _hasMsetTimelineSpace && _activeMsetSection >= 0;
        _resetCamera.Location = new Point(stage.ClientSize.Width - _resetCamera.Width - 14, 14);
        _exportToast.Location = new Point((stage.ClientSize.Width - _exportToast.Width) / 2, 14);
    }

    private static Button MakeButton(string text)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat };
        StyleButton(button);
        return button;
    }

    private static void StyleButton(Button button)
    {
        button.BackColor = Color.FromArgb(37, 45, 57);
        button.ForeColor = Color.FromArgb(237, 242, 248);
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderColor = Color.FromArgb(60, 70, 86);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 64, 81);
        button.Margin = new Padding(0, 0, 7, 0);
    }

    private async Task OpenArchiveDialogAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open ARD file",
            Filter = "ARD files (*.ard)|*.ard|All files (*.*)|*.*",
            Multiselect = true
        };
        if (_lastFolder is not null && Directory.Exists(_lastFolder))
            dialog.InitialDirectory = _lastFolder;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string? folder = Path.GetDirectoryName(dialog.FileNames[0]);
        if (folder is not null) RememberFolder(folder);
        await LoadArchivesAsync(dialog.FileNames);
    }

    private async Task OpenFolderDialogAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder containing ARD files",
            SelectedPath = _lastFolder is not null && Directory.Exists(_lastFolder) ? _lastFolder : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        RememberFolder(dialog.SelectedPath);
        await LoadFolderAsync(dialog.SelectedPath);
    }

    private async Task LoadLastFolderAsync()
    {
        if (_lastFolder is null || !Directory.Exists(_lastFolder)) return;
        await LoadFolderAsync(_lastFolder);
    }

    private async Task LoadFolderAsync(string folder)
    {
        string[] paths;
        try
        {
            paths = Directory.GetFiles(folder, "*.ard", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, error.Message, "Could not scan folder",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (paths.Length == 0)
        {
            _status.Text = "No .ard files were found.";
            if (_settingsWarning is not null)
                _status.Text += $" · {_settingsWarning}";
            return;
        }
        await LoadArchivesAsync(paths);
    }

    private string? ReadLastFolder()
    {
        try
        {
            if (File.Exists(LastFolderSettingsPath))
            {
                ViewerSettings? settings = JsonSerializer.Deserialize<ViewerSettings>(
                    File.ReadAllText(LastFolderSettingsPath, Encoding.UTF8));
                string? savedFolder = settings?.LastFolder?.Trim();
                return !string.IsNullOrEmpty(savedFolder) && Directory.Exists(savedFolder) ? savedFolder : null;
            }

            if (!File.Exists(LegacyLastFolderSettingsPath)) return null;
            string folder = File.ReadAllText(LegacyLastFolderSettingsPath, Encoding.UTF8).Trim();
            if (Directory.Exists(folder))
            {
                RememberFolder(folder);
                return folder;
            }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _settingsWarning = $"Could not read the saved folder settings: {error.Message}";
            return null;
        }
    }

    private void RememberFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        _lastFolder = folder;
        bool saved = false;
        try
        {
            string? settingsDirectory = Path.GetDirectoryName(LastFolderSettingsPath);
            if (settingsDirectory is not null) Directory.CreateDirectory(settingsDirectory);
            string json = JsonSerializer.Serialize(new ViewerSettings { LastFolder = folder },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LastFolderSettingsPath, json, new UTF8Encoding(false));
            saved = true;
            _settingsWarning = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _settingsWarning = $"Could not save the last folder: {error.Message}";
        }

        if (saved && File.Exists(LegacyLastFolderSettingsPath))
        {
            try
            {
                File.Delete(LegacyLastFolderSettingsPath);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _settingsWarning = $"Could not remove the previous saved-folder file: {error.Message}";
            }
        }
    }

    private async Task LoadArchivesAsync(IEnumerable<string> archivePaths)
    {
        if (_isLoading) return;
        string[] paths = archivePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;
        bool categorizeArchives = paths.Length > 1;

        _isLoading = true;
        _archiveActions.Enabled = false;
        _loadProgress.Text = $"0 / {paths.Length} Files Loaded";
        _loadProgress.Visible = true;
        int loadedCount = 0;
        var failures = new List<string>();
        try
        {
            foreach (string path in paths)
            {
                List<ArdViewerModel> loaded;
                try
                {
                    loaded = await Task.Run(() => ArdViewer.Read(path));
                }
                catch (Exception error)
                {
                    failures.Add($"{Path.GetFileName(path)}: {error.Message}");
                    _loadProgress.Text = $"{loadedCount} / {paths.Length} Files Loaded";
                    continue;
                }
                TreeNode? categoryNode = null;
                string? categoryName = categorizeArchives
                    ? NativeArchiveCategories.GetCategory(path)
                    : null;
                if (categoryName is not null)
                {
                    if (!_archiveCategories.TryGetValue(categoryName, out categoryNode))
                    {
                        categoryNode = new TreeNode(categoryName)
                        {
                            Name = $"category:{categoryName}",
                            Tag = new ArchiveCategoryTag(categoryName),
                            ForeColor = Color.FromArgb(238, 242, 248)
                        };
                        _categoryFont ??= new Font(_models.Font, FontStyle.Bold);
                        categoryNode.NodeFont = _categoryFont;
                        categoryNode.Text = $"▸ {categoryName}";
                        _archiveCategories.Add(categoryName, categoryNode);
                        _models.Nodes.Add(categoryNode);
                    }
                }
                AddArchive(path, loaded, categoryNode);
                loadedCount++;
                _loadProgress.Text = $"{loadedCount} / {paths.Length} Files Loaded";
            }
            if (_archiveCategories.TryGetValue("Items", out TreeNode? itemsCategory))
            {
                itemsCategory.Remove();
                _models.Nodes.Add(itemsCategory);
            }
        }
        finally
        {
            _isLoading = false;
            _archiveActions.Enabled = true;
            _loadProgress.Visible = false;
        }

        _status.Text = $"{loadedCount} / {paths.Length} Files Loaded";
        if (failures.Count > 0)
        {
            _status.Text += $" ({failures.Count} failed).";
            MessageBox.Show(this, string.Join(Environment.NewLine, failures), "Could not open some ARD files",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        if (_settingsWarning is not null)
            _status.Text += $" · {_settingsWarning}";
    }

    private void AddArchive(string path, List<ArdViewerModel> loaded, TreeNode? categoryNode)
    {
        if (loaded.Count == 0) return;

        string archiveName = Path.GetFileName(path);
        var archiveNode = new TreeNode(archiveName)
        {
            Name = path,
            Tag = path,
            ForeColor = Color.FromArgb(238, 242, 248)
        };
        foreach (ArdViewerModel model in loaded)
        {
            model.Archive = archiveName;
            var node = new TreeNode($"{model.Name}.{model.Format.ToLowerInvariant()}") { Tag = model };
            archiveNode.Nodes.Add(node);
            _nodes[model] = node;
            _allModels.Add(model);
        }
        archiveNode.Text = $"▸ {archiveName}";
        (categoryNode?.Nodes ?? _models.Nodes).Add(archiveNode);
    }

    private void SelectModel(ArdViewerModel model)
    {
        StopMsetPlayback();
        _selected = model;
        _activeMsetSection = -1;
        _activeMsetFrame = 0;
        RefreshMsetAnimationList();
        if (GetUsableMsetAnimations().Length == 0)
            CloseMsetAnimationsWindow();
        _viewport.SetModel(model);
        _bones.Visible = true;
        _bones.Text = _viewport.BonesVisible ? "Hide bones" : "Show bones";
        _export.Enabled = true;
        _export.Visible = true;
        _exportMset.Visible = false;
        _stats.Visible = true;
        int vertices = model.Meshes.Sum(mesh => mesh.Positions.Length / 3);
        int triangles = model.Meshes.Sum(mesh => mesh.Indices.Length / 3);
        int msetChannels = model.MsetSections.Sum(section => section.Channels.Count);
        int msetCurves = model.MsetSections.Sum(section => section.Curves.Count + section.HelperCurves.Count);
        _stats.Text = $"Meshes: {model.Meshes.Count}\r\nVertices: {vertices:N0}\r\nTriangles: {triangles:N0}\r\nTextures: {model.Textures.Count}\r\nMSET data: {model.MsetSections.Count} sections / {msetChannels} channels / {msetCurves} curves";
        UpdateMsetControls();
        _status.Text = $"{model.Format} / {model.Name} · {model.Archive}";
        _textures.Controls.Clear();
        for (int i = 0; i < model.Textures.Count; i++)
        {
            ArdViewerTexture texture = model.Textures[i];
            var item = new Panel { Width = 76, Height = 86, BackColor = Color.FromArgb(24, 29, 37), Margin = new Padding(3) };
            var preview = new PictureBox
            {
                Image = MakeBitmap(texture),
                SizeMode = PictureBoxSizeMode.Zoom,
                Bounds = new Rectangle(8, 4, 60, 58),
                BackColor = Color.FromArgb(12, 15, 20),
                Cursor = Cursors.Hand,
                Tag = texture
            };
            string textureName = texture.Name;
            preview.Click += (_, _) => ShowTexturePreview(texture);
            item.Controls.Add(preview);
            item.Controls.Add(new Label
            {
                Text = textureName,
                Dock = DockStyle.Bottom,
                Height = 21,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(197, 207, 221),
                Font = new Font("Segoe UI", 7.5F),
                Tag = i
            });
            _textures.Controls.Add(item);
        }
    }

    private static Bitmap MakeBitmap(ArdViewerTexture texture)
    {
        byte[] rgba = Convert.FromBase64String(texture.Rgba);
        var bitmap = new Bitmap(texture.Width, texture.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, texture.Width, texture.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[checked(texture.Width * texture.Height * 4)];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                bgra[i] = rgba[i + 2];
                bgra[i + 1] = rgba[i + 1];
                bgra[i + 2] = rgba[i];
                bgra[i + 3] = rgba[i + 3];
            }
            for (int y = 0; y < texture.Height; y++)
                Marshal.Copy(bgra, y * texture.Width * 4, data.Scan0 + y * data.Stride, texture.Width * 4);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    private void ShowTexturePreview(ArdViewerTexture texture)
    {
        using var overlay = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = PointToScreen(Point.Empty),
            ClientSize = ClientSize,
            BackColor = Color.Black,
            Opacity = 0.62,
            KeyPreview = true
        };
        using var preview = new Form
        {
            Text = $"{texture.Name} · {texture.Width} × {texture.Height}",
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.FromArgb(16, 19, 25),
            Padding = new Padding(12),
            KeyPreview = true
        };
        int maxWidth = Math.Max(1, Math.Min(900, (int)(ClientSize.Width * 0.8)));
        int maxHeight = Math.Max(1, Math.Min(700, (int)(ClientSize.Height * 0.8) - 32));
        double scale = Math.Min(4, Math.Min((double)maxWidth / texture.Width, (double)maxHeight / texture.Height));
        int imageWidth = Math.Max(1, (int)Math.Round(texture.Width * scale));
        int imageHeight = Math.Max(1, (int)Math.Round(texture.Height * scale));
        var image = new PictureBox
        {
            Bounds = new Rectangle(12, 38, imageWidth, imageHeight),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = MakeBitmap(texture),
            BackColor = Color.FromArgb(12, 15, 20)
        };
        preview.ClientSize = new Size(imageWidth + 24, imageHeight + 50);
        preview.Controls.Add(new Label
        {
            Text = $"{texture.Name} · {texture.Width} × {texture.Height}",
            Bounds = new Rectangle(12, 8, imageWidth, 24),
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(229, 233, 240),
            TextAlign = ContentAlignment.MiddleLeft
        });
        preview.Controls.Add(image);
        overlay.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            e.Handled = true;
            overlay.Close();
        };
        overlay.MouseDown += (_, _) => overlay.Close();
        preview.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            e.Handled = true;
            overlay.Close();
        };
        overlay.Shown += (_, _) =>
        {
            preview.Show(overlay);
            preview.Location = new Point(
                overlay.Left + (overlay.Width - preview.Width) / 2,
                overlay.Top + (overlay.Height - preview.Height) / 2);
            preview.BringToFront();
        };
        overlay.ShowDialog(this);
        image.Image.Dispose();
    }

    private void ExportSelected()
    {
        if (_selected == null) return;
        try
        {
            string directory = GetExportDirectory(_selected);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, SanitizeFileName(_selected.Name, "model") + ".gltf");
            File.WriteAllText(path, NativeGltfExporter.Create(
                _selected, directory, _activeMsetSection, _activeMsetFrame), new UTF8Encoding(false));
            _status.Text = $"Exported: {path}";
            ShowExportConfirmation("Model and texture files exported successfully.");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Could not export model",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportSelectedMsets()
    {
        if (_selected == null) return;
        int[] sectionIndices = _animationsList is { IsDisposed: false }
            ? _animationsList.SelectedItems
                .Cast<MsetAnimationItem>()
                .Select(item => item.SectionIndex)
                .ToArray()
            : Array.Empty<int>();
        if (sectionIndices.Length == 0) return;
        try
        {
            string directory = GetExportDirectory(_selected);
            IReadOnlyList<string> files = NativeMsetExporter.Export(_selected, directory, sectionIndices);
            _status.Text = $"Exported {files.Count} original MSET file(s) to {directory}";
            ShowExportConfirmation($"Selected MSET file{(files.Count == 1 ? "" : "s")} extracted successfully.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, error.Message, "Could not export MSET data",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowExportConfirmation(string message)
    {
        _exportToast.Text = message;
        _exportToast.ForeColor = Color.FromArgb(89, 220, 133);
        _exportToastShownAt = DateTime.UtcNow;
        _exportToast.Visible = true;
        _exportToast.BringToFront();
        _exportToastTimer.Stop();
        _exportToastTimer.Start();
    }

    private void FadeExportToast()
    {
        const int displayMilliseconds = 5000;
        const int fadeMilliseconds = 800;
        double elapsed = (DateTime.UtcNow - _exportToastShownAt).TotalMilliseconds;
        if (elapsed < displayMilliseconds) return;

        double opacity = Math.Clamp(1 - (elapsed - displayMilliseconds) / fadeMilliseconds, 0, 1);
        Color background = Color.FromArgb(14, 17, 22);
        Color green = Color.FromArgb(89, 220, 133);
        _exportToast.ForeColor = Color.FromArgb(
            (int)(background.R + (green.R - background.R) * opacity),
            (int)(background.G + (green.G - background.G) * opacity),
            (int)(background.B + (green.B - background.B) * opacity));
        if (opacity > 0) return;
        _exportToastTimer.Stop();
        _exportToast.Visible = false;
    }

    private void ShowMsetAnimations()
    {
        if (_animationsWindow is { IsDisposed: false, Visible: true })
        {
            CloseMsetAnimationsWindow();
            return;
        }
        if (_selected == null || GetUsableMsetAnimations().Length == 0) return;

        var window = new Form
        {
            Text = $"Animations · {_selected.Name}",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            MinimizeBox = false,
            MaximizeBox = false,
            Size = new Size(340, 400),
            BackColor = Color.FromArgb(112, 132, 158),
            ForeColor = Color.FromArgb(229, 233, 240),
            Padding = new Padding(2),
            Font = Font
        };
        window.FormClosing += (_, e) =>
        {
            if (!_allowAnimationsWindowClose && e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true;
        };
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 29, 37),
            Padding = new Padding(8)
        };
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(16, 20, 27),
            ForeColor = Color.FromArgb(220, 228, 239),
            BorderStyle = BorderStyle.None,
            SelectionMode = SelectionMode.MultiExtended,
            IntegralHeight = false,
            ItemHeight = 28,
            Font = Font,
            Margin = new Padding(10)
        };
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            Padding = new Padding(8, 0, 0, 0),
            WrapContents = false,
            BackColor = content.BackColor
        };
        var autoPlay = new CheckBox
        {
            Text = "Auto Play",
            AutoSize = true,
            Checked = _autoPlay,
            ForeColor = window.ForeColor,
            BackColor = content.BackColor,
            UseVisualStyleBackColor = false,
            Margin = new Padding(3, 8, 18, 0)
        };
        autoPlay.CheckedChanged += (_, _) => _autoPlay = autoPlay.Checked;
        var loop = new CheckBox
        {
            Text = "Loop",
            AutoSize = true,
            Checked = _loop,
            ForeColor = window.ForeColor,
            BackColor = content.BackColor,
            UseVisualStyleBackColor = false,
            Margin = new Padding(3, 8, 3, 0)
        };
        loop.CheckedChanged += (_, _) => _loop = loop.Checked;
        options.Controls.Add(autoPlay);
        options.Controls.Add(loop);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingMsetAnimationList) return;
            if (list.SelectedItems.Count == 1 &&
                list.SelectedItems[0] is MsetAnimationItem item &&
                _selected != null)
                SelectMsetAnimation(item.SectionIndex);
            UpdateMsetControls();
        };
        content.Controls.Add(list);
        content.Controls.Add(options);
        window.Controls.Add(content);
        window.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_animationsWindow, window))
            {
                _animationsWindow = null;
                _animationsList = null;
                _allowAnimationsWindowClose = false;
                UpdateMsetControls();
                if (_animations.Parent is Panel stage)
                    LayoutViewportControls(stage);
            }
        };
        _animationsWindow = window;
        _animationsList = list;
        RefreshMsetAnimationList();
        PositionAnimationsWindow(window);
        window.Show(this);
        if (_animations.Parent is Panel stage)
            LayoutViewportControls(stage);
    }

    private void CloseMsetAnimationsWindow()
    {
        if (_animationsWindow is not { IsDisposed: false } window) return;
        _allowAnimationsWindowClose = true;
        window.Close();
    }

    private void RefreshMsetAnimationList()
    {
        if (_animationsWindow is not { IsDisposed: false } window ||
            _animationsList is not { IsDisposed: false } list)
            return;

        _updatingMsetAnimationList = true;
        try
        {
            list.Items.Clear();
            if (_selected == null)
            {
                UpdateMsetControls();
                return;
            }
            window.Text = $"Animations · {_selected.Name}";
            foreach (int sectionIndex in GetUsableMsetAnimations())
            {
                ArdViewerMsetSection section = _selected.MsetSections[sectionIndex];
                list.Items.Add(new MsetAnimationItem(sectionIndex,
                    $"Animation {sectionIndex + 1}  ·  MMTN +0x{section.Offset:X}  ·  {section.FrameEnd} frames  ·  {section.FramesPerSecond:0.##} FPS"));
            }
        }
        finally
        {
            _updatingMsetAnimationList = false;
        }
        UpdateMsetControls();
    }

    private void PositionAnimationsWindow(Form window)
    {
        if (WindowState == FormWindowState.Minimized) return;
        Control parent = _animations.Parent ?? this;
        Rectangle bounds = parent.RectangleToScreen(parent.ClientRectangle);
        Point buttonScreen = _animations.PointToScreen(Point.Empty);
        int width = Math.Max(1, Math.Min(340, bounds.Width));
        int availableAbove = buttonScreen.Y - bounds.Top - 8;
        int height = Math.Max(1, Math.Min(400, Math.Min(bounds.Height, availableAbove)));
        window.Size = new Size(width, height);
        int x = buttonScreen.X + (_animations.Width - window.Width) / 2;
        int y = buttonScreen.Y - window.Height - 8;
        int maxX = Math.Max(bounds.Left, bounds.Right - window.Width);
        int maxY = Math.Max(bounds.Top, bounds.Bottom - window.Height);
        window.Location = new Point(
            Math.Clamp(x, bounds.Left, maxX),
            Math.Clamp(y, bounds.Top, Math.Min(maxY, Math.Max(bounds.Top, buttonScreen.Y - window.Height - 8))));
    }

    private void UpdateAnimationsWindowBounds()
    {
        if (_animationsWindow is not { IsDisposed: false } window) return;
        if (WindowState == FormWindowState.Minimized)
        {
            window.Hide();
            return;
        }

        PositionAnimationsWindow(window);
        if (!window.Visible) window.Show(this);
    }

    private void UpdateMsetControls()
    {
        int[] animations = GetUsableMsetAnimations();
        bool hasAnimations = animations.Length > 0;
        bool hasActiveAnimation = animations.Contains(_activeMsetSection);
        int selectedAnimationCount = _animationsWindow is { IsDisposed: false, Visible: true } &&
            _animationsList is { IsDisposed: false }
                ? _animationsList.SelectedItems.Count
                : 0;
        _animations.Visible = hasAnimations ||
            _animationsWindow is { IsDisposed: false, Visible: true };
        _msetPlay.Visible = selectedAnimationCount == 1;
        _exportMset.Visible = selectedAnimationCount > 0;
        _msetPlay.Text = _msetPlaybackTimer.Enabled ? "Pause" : "Play";
        _msetTimeline.Visible = hasActiveAnimation && _hasMsetTimelineSpace;
        _msetTime.Visible = hasActiveAnimation && _hasMsetTimelineSpace;
        if (hasActiveAnimation && _selected != null)
            UpdateMsetTimeline(_selected.MsetSections[_activeMsetSection]);
    }

    private void SelectMsetAnimation(int sectionIndex, bool allowAutoPlay = true)
    {
        if (_selected == null || !GetUsableMsetAnimations().Contains(sectionIndex) ||
            sectionIndex == _activeMsetSection && !_msetPlaybackTimer.Enabled)
            return;

        if (_msetPlaybackTimer.Enabled) _msetPlaybackTimer.Stop();
        _activeMsetSection = sectionIndex;
        _activeMsetFrame = 0;
        _viewport.SetMsetAnimationFrame(sectionIndex, 0);
        _msetPlay.Text = "Play";
        UpdateMsetControls();
        if (allowAutoPlay && _autoPlay) ToggleMsetPlayback();
    }

    private int[] GetUsableMsetAnimations()
    {
        ArdViewerModel? selected = _selected;
        if (selected == null) return Array.Empty<int>();
        return selected.MsetSections
            .Select((section, index) => (section, index))
            .Where(item => item.section.HasAnimation &&
                item.section.FramesPerSecond > 0 &&
                item.section.FrameEnd > 0 &&
                item.section.JointLocalMatrices.Count == selected.Joints.Count)
            .Select(item => item.index)
            .ToArray();
    }

    private void ToggleMsetPlayback()
    {
        if (_msetPlaybackTimer.Enabled)
        {
            StopMsetPlayback();
            return;
        }

        int[] sections = GetUsableMsetAnimations();
        if (sections.Length == 0) return;
        if (!sections.Contains(_activeMsetSection))
        {
            SelectMsetAnimation(sections[0], allowAutoPlay: false);
        }
        ArdViewerMsetSection section = _selected!.MsetSections[_activeMsetSection];
        _activeMsetFrame %= section.FrameEnd;
        long offset = (long)(_activeMsetFrame / section.FramesPerSecond * Stopwatch.Frequency);
        _msetPlaybackStart = Stopwatch.GetTimestamp() - offset;
        _viewport.SetMsetAnimationFrame(_activeMsetSection, _activeMsetFrame);
        _msetPlaybackTimer.Start();
        UpdateMsetControls();
    }

    private void AdvanceMsetPose()
    {
        if (_selected == null ||
            _activeMsetSection < 0 ||
            _activeMsetSection >= _selected.MsetSections.Count)
        {
            StopMsetPlayback();
            return;
        }

        ArdViewerMsetSection section = _selected.MsetSections[_activeMsetSection];
        if (!section.HasAnimation || section.FrameEnd <= 0 || section.FramesPerSecond <= 0)
        {
            StopMsetPlayback();
            return;
        }
        double elapsed = (Stopwatch.GetTimestamp() - _msetPlaybackStart) / (double)Stopwatch.Frequency;
        float frame = (float)(elapsed * section.FramesPerSecond);
        if (!_loop && frame >= section.FrameEnd)
        {
            _activeMsetFrame = section.FrameEnd;
            _viewport.SetMsetAnimationFrame(_activeMsetSection, _activeMsetFrame);
            UpdateMsetTimeline(section);
            StopMsetPlayback();
            return;
        }
        _activeMsetFrame = _loop ? frame % section.FrameEnd : frame;
        _viewport.SetMsetAnimationFrame(_activeMsetSection, _activeMsetFrame);
        UpdateMsetTimeline(section);
    }

    private void StopMsetPlayback()
    {
        _msetPlaybackTimer.Stop();
        _msetPlay.Text = "Play";
        UpdateMsetControls();
    }

    private void UpdateMsetTimeline(ArdViewerMsetSection section)
    {
        int value = (int)Math.Round(_activeMsetFrame / section.FrameEnd * _msetTimeline.Maximum);
        _updatingMsetTimeline = true;
        try
        {
            _msetTimeline.Value = Math.Clamp(value, _msetTimeline.Minimum, _msetTimeline.Maximum);
        }
        finally
        {
            _updatingMsetTimeline = false;
        }
        UpdateMsetTimeLabel(section);
    }

    private void UpdateMsetTimeLabel(ArdViewerMsetSection section)
    {
        _msetTime.Text = $"{_activeMsetFrame:0.0}/{section.FrameEnd}";
    }

    private static string GetExportDirectory(ArdViewerModel model)
    {
        string archiveName = Path.GetFileNameWithoutExtension(model.Archive);
        string safeArchiveName = SanitizeFileName(archiveName, "archive");
        string safeModelName = SanitizeFileName(model.Name, "model");
        return Path.Combine(AppContext.BaseDirectory, "Extract", safeArchiveName, safeModelName + "_extracted");
    }

    private static string SanitizeFileName(string name, string fallback)
    {
        string safeName = string.Concat(name.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c)).Trim(' ', '.');
        return safeName.Length == 0 ? fallback : safeName;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CloseMsetAnimationsWindow();
        _msetPlaybackTimer.Dispose();
        _exportToastTimer.Dispose();
        foreach (Control control in _textures.Controls)
        {
            if (control is Panel panel)
                foreach (Control child in panel.Controls)
                    if (child is PictureBox picture) picture.Image?.Dispose();
        }
        _applicationIcon?.Dispose();
        _categoryFont?.Dispose();
        base.OnFormClosed(e);
    }
}

internal sealed class NativeActionButton : Button
{
    internal NativeActionButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        FlatStyle = FlatStyle.Flat;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width < 1 || bounds.Height < 1) return;
        Color background = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position))
            ? Color.FromArgb(52, 64, 81)
            : Color.FromArgb(37, 45, 57);
        using var fill = new SolidBrush(background);
        using var border = new Pen(Color.FromArgb(60, 70, 86));
        e.Graphics.FillRectangle(fill, bounds);
        e.Graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        e.Graphics.DrawString(Text, Font, textBrush, ClientRectangle, format);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6),
                Color.FromArgb(245, 248, 255), Color.FromArgb(37, 45, 57));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate();
    }
}

internal static class NativeArchiveCategories
{
    private static readonly (string Prefix, string Category)[] Regions =
    [
        ("al", "Agrabah"),
        ("aw", "Wonderland"),
        ("dc", "Disney Castle"),
        ("dh", "Dive into the Heart"),
        ("di", "Destiny Islands"),
        ("ew", "End of the World"),
        ("he", "Olympus Coliseum"),
        ("lm", "Atlantica"),
        ("nm", "Halloween Town"),
        ("pc", "Hollow Bastion"),
        ("pi", "Monstro"),
        ("po", "100th Acre Wood"),
        ("pp", "Neverland"),
        ("tw", "Traverse Town"),
        ("tz", "Deep Jungle")
    ];

    internal static string? GetCategory(string archivePath)
    {
        string name = Path.GetFileNameWithoutExtension(archivePath);
        if (name.Equals("prize", StringComparison.OrdinalIgnoreCase))
            return "Items";

        foreach ((string prefix, string category) in Regions)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return category;
        return null;
    }
}

internal static class NativeMsetExporter
{
    internal static IReadOnlyList<string> Export(
        ArdViewerModel model, string directory, IEnumerable<int> sectionIndices)
    {
        HashSet<int> selectedIndices = sectionIndices.ToHashSet();
        var sections = model.MsetSections
            .Select((section, index) => (section, index))
            .Where(item => selectedIndices.Contains(item.index) && item.section.RawData.Length != 0)
            .ToArray();
        if (sections.Length == 0)
            throw new InvalidOperationException("The selection contains no original MSET data.");

        Directory.CreateDirectory(directory);
        string modelName = SanitizeFileName(model.Name);
        var paths = new List<string>(sections.Length);
        for (int i = 0; i < sections.Length; i++)
        {
            ArdViewerMsetSection section = sections[i].section;
            string fileName = $"{modelName}_mset_{sections[i].index + 1:D2}_0x{section.Offset:X}.mset";
            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, CreateSingleAnimationBlock(section));
            paths.Add(path);
        }
        return paths;
    }

    private static byte[] CreateSingleAnimationBlock(ArdViewerMsetSection section)
    {
        byte[] data = section.RawData.ToArray();
        if (data.Length < 0x38)
            throw new InvalidDataException("An embedded MSET block has a truncated header.");

        uint tableOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x30));
        if (tableOffset > data.Length - 8)
            throw new InvalidDataException("An embedded MSET reference table is out of bounds.");

        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan((int)tableOffset), section.ReferenceValue);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan((int)tableOffset + 4), uint.MaxValue);
        return data;
    }

    private static string SanitizeFileName(string name)
    {
        string safeName = string.Concat(name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\' ? '_' : character))
            .Trim(' ', '.');
        return safeName.Length == 0 ? "model" : safeName;
    }
}

internal sealed class NativeGlViewport : Panel
{
    private const int PfdDrawToWindow = 0x00000004;
    private const int PfdSupportOpenGl = 0x00000020;
    private const int PfdDoubleBuffer = 0x00000001;
    private const byte PfdTypeRgba = 0;
    private const byte PfdMainPlane = 0;
    private const uint GlColorBufferBit = 0x00004000;
    private const uint GlDepthBufferBit = 0x00000100;
    private const uint GlDepthTest = 0x0B71;
    private const uint GlCullFace = 0x0B44;
    private const uint GlBack = 0x0405;
    private const uint GlBlend = 0x0BE2;
    private const uint GlSrcAlpha = 0x0302;
    private const uint GlOneMinusSrcAlpha = 0x0303;
    private const uint GlAlphaTest = 0x0BC0;
    private const uint GlGreaterEqual = 0x0206;
    private const uint GlProjection = 0x1701;
    private const uint GlModelview = 0x1700;
    private const uint GlTriangles = 0x0004;
    private const uint GlLines = 0x0001;
    private const uint GlPoints = 0x0000;
    private const uint GlTexture2D = 0x0DE1;
    private const uint GlRgba = 0x1908;
    private const uint GlUnsignedByte = 0x1401;
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlLinear = 0x2601;
    private const int GlRepeat = 0x2901;
    private const int GlClampToEdge = 0x812F;
    private const uint GlUnpackAlignment = 0x0CF5;
    private const uint GlColorMaterial = 0x0B57;
    private const uint GlFrontAndBack = 0x0408;
    private const uint GlAmbientAndDiffuse = 0x1602;

    private IntPtr _deviceContext;
    private IntPtr _renderContext;
    private bool _ready;
    private ArdViewerModel? _model;
    private int[] _textureIds = Array.Empty<int>();
    private float[] _positions = Array.Empty<float>();
    private float[] _basePositions = Array.Empty<float>();
    private int[] _jointIds = Array.Empty<int>();
    private float[] _uvs = Array.Empty<float>();
    private byte[] _colors = Array.Empty<byte>();
    private int[] _indices = Array.Empty<int>();
    private Matrix4x4[] _jointWorldMatrices = Array.Empty<Matrix4x4>();
    private int _activeMsetSection = -1;
    private float _activeMsetFrame;
    private bool _showBones;
    private float[] _center = new float[3];
    private float _radius = 1;
    private float _floorY;
    private float _distance = 2.8f;
    private float _theta = .65f;
    private float _phi = 1.05f;
    private readonly float[] _pan = new float[3];
    private Point _lastMouse;
    private bool _dragging;
    private bool _panning;
    private MouseButtons _dragButton;

    internal NativeGlViewport()
    {
        BackColor = Color.FromArgb(14, 17, 22);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        TabStop = true;
        MouseDown += HandleMouseDown;
        MouseUp += (_, _) => { _dragging = false; Capture = false; };
        MouseMove += HandleMouseMove;
        MouseWheel += HandleMouseWheel;
        Resize += (_, _) => Invalidate();
    }

    internal void SetModel(ArdViewerModel model)
    {
        _model = model;
        _activeMsetSection = -1;
        _activeMsetFrame = 0;
        UpdateJointWorldMatrices();
        if (_ready) UploadModel();
        Invalidate();
    }

    internal bool BonesVisible => _showBones;

    internal bool ToggleShowBones()
    {
        _showBones = !_showBones;
        Invalidate();
        return _showBones;
    }

    internal void SetMsetPose(int sectionIndex)
    {
        SetMsetAnimationFrame(sectionIndex, 0);
    }

    internal void SetMsetAnimationFrame(int sectionIndex, float frame)
    {
        if (_model == null || sectionIndex < -1 || sectionIndex >= _model.MsetSections.Count) return;
        _activeMsetSection = sectionIndex;
        _activeMsetFrame = frame;
        UpdateJointWorldMatrices();
        float[] positions = BuildMsetPositions(sectionIndex, frame);
        Array.Copy(positions, _positions, positions.Length);
        Invalidate();
    }

    private float[] BuildMsetPositions(int sectionIndex, float frame)
    {
        var result = _basePositions.ToArray();
        if (_model == null || sectionIndex < 0) return result;

        ArdViewerMsetSection section = _model.MsetSections[sectionIndex];
        if (section.JointLocalMatrices.Count != _model.Joints.Count ||
            !section.HasPose)
            return result;

        List<float[]> localMatrices = ArdViewer.BuildMsetJointLocals(_model.Joints, section, frame);
        var world = new Matrix4x4[_model.Joints.Count];
        for (int i = 0; i < world.Length; i++)
        {
            Matrix4x4 local = ReadMatrix(localMatrices[i]);
            int parent = _model.Joints[i].ParentIndex;
            world[i] = parent >= 0 && parent < i ? local * world[parent] : local;
        }

        var deformations = new Matrix4x4[world.Length];
        for (int i = 0; i < world.Length; i++)
            deformations[i] = ReadMatrix(_model.Joints[i].InverseBindMatrix) * world[i];

        for (int i = 0; i < _jointIds.Length; i++)
        {
            int joint = _jointIds[i];
            if (joint < 0 || joint >= deformations.Length) continue;
            int offset = i * 3;
            Vector3 posed = Vector3.Transform(
                new Vector3(_basePositions[offset], _basePositions[offset + 1], _basePositions[offset + 2]),
                deformations[joint]);
            result[offset] = posed.X;
            result[offset + 1] = posed.Y;
            result[offset + 2] = posed.Z;
        }

        return result;
    }

    private static Matrix4x4 ReadMatrix(float[] values) =>
        new(values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);

    private void UpdateJointWorldMatrices()
    {
        if (_model == null)
        {
            _jointWorldMatrices = Array.Empty<Matrix4x4>();
            return;
        }

        ArdViewerMsetSection? section = _activeMsetSection >= 0 &&
            _activeMsetSection < _model.MsetSections.Count
                ? _model.MsetSections[_activeMsetSection]
                : null;
        List<float[]>? animatedLocals = section != null &&
            section.JointLocalMatrices.Count == _model.Joints.Count &&
            section.HasPose
                ? ArdViewer.BuildMsetJointLocals(_model.Joints, section, _activeMsetFrame)
                : null;
        _jointWorldMatrices = new Matrix4x4[_model.Joints.Count];
        for (int i = 0; i < _jointWorldMatrices.Length; i++)
        {
            Matrix4x4 local = ReadMatrix(animatedLocals?[i] ?? _model.Joints[i].LocalMatrix);
            int parent = _model.Joints[i].ParentIndex;
            _jointWorldMatrices[i] = parent >= 0 && parent < i
                ? local * _jointWorldMatrices[parent]
                : local;
        }
    }

    internal void ResetCamera()
    {
        _theta = .65f;
        _phi = 1.05f;
        _distance = _radius * 2.8f;
        Array.Clear(_pan);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _deviceContext = GetDC(Handle);
        var format = new PixelFormatDescriptor
        {
            Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(),
            Version = 1,
            Flags = PfdDrawToWindow | PfdSupportOpenGl | PfdDoubleBuffer,
            PixelType = PfdTypeRgba,
            ColorBits = 32,
            DepthBits = 24,
            StencilBits = 8,
            LayerType = PfdMainPlane
        };
        int pixelFormat = ChoosePixelFormat(_deviceContext, ref format);
        if (pixelFormat == 0 || !SetPixelFormat(_deviceContext, pixelFormat, ref format))
            throw new InvalidOperationException("Could not create the OpenGL pixel format.");
        _renderContext = WglCreateContext(_deviceContext);
        if (_renderContext == IntPtr.Zero || !WglMakeCurrent(_deviceContext, _renderContext))
            throw new InvalidOperationException("Could not initialize the OpenGL context.");
        _ready = true;
        GlEnable(GlDepthTest);
        GlEnable(GlCullFace);
        GlCullFaceMode(GlBack);
        GlEnable(GlBlend);
        GlBlendFunc(GlSrcAlpha, GlOneMinusSrcAlpha);
        GlEnable(GlColorMaterial);
        GlColorMaterialFunc(GlFrontAndBack, GlAmbientAndDiffuse);
        GlPixelStorei(GlUnpackAlignment, 1);
        if (_model != null) UploadModel();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_renderContext != IntPtr.Zero)
        {
            WglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            WglDeleteContext(_renderContext);
            _renderContext = IntPtr.Zero;
        }
        if (_deviceContext != IntPtr.Zero)
        {
            ReleaseDC(Handle, _deviceContext);
            _deviceContext = IntPtr.Zero;
        }
        _ready = false;
        base.OnHandleDestroyed(e);
    }

    private void UploadModel()
    {
        if (!_ready || _model == null) return;
        WglMakeCurrent(_deviceContext, _renderContext);
        if (_textureIds.Length > 0) GlDeleteTextures(_textureIds.Length, _textureIds);
        _textureIds = new int[_model.Textures.Count];
        if (_textureIds.Length > 0) GlGenTextures(_textureIds.Length, _textureIds);
        for (int i = 0; i < _model.Textures.Count; i++)
        {
            ArdViewerTexture texture = _model.Textures[i];
            byte[] rgba = Convert.FromBase64String(texture.Rgba);
            GlBindTexture(GlTexture2D, (uint)_textureIds[i]);
            GlTexParameteri(GlTexture2D, GlTextureMinFilter, GlLinear);
            GlTexParameteri(GlTexture2D, GlTextureMagFilter, GlLinear);
            GlTexParameteri(GlTexture2D, GlTextureWrapS, texture.Repeat ? GlRepeat : GlClampToEdge);
            GlTexParameteri(GlTexture2D, GlTextureWrapT, texture.Repeat ? GlRepeat : GlClampToEdge);
            GlTexImage2D(GlTexture2D, 0, (int)GlRgba, texture.Width, texture.Height, 0,
                GlRgba, GlUnsignedByte, rgba);
        }

        int vertexCount = _model.Meshes.Sum(mesh => mesh.Positions.Length / 3);
        _positions = new float[vertexCount * 3];
        _basePositions = new float[vertexCount * 3];
        _jointIds = new int[vertexCount];
        _uvs = new float[vertexCount * 2];
        _colors = new byte[vertexCount * 4];
        int indexCount = _model.Meshes.Sum(mesh => mesh.Indices.Length);
        _indices = new int[indexCount];
        int vertexOffset = 0, indexOffset = 0;
        float[] lo = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        float[] hi = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
        foreach (ArdViewerMesh mesh in _model.Meshes)
        {
            int count = mesh.Positions.Length / 3;
            Array.Copy(mesh.Positions, 0, _positions, vertexOffset * 3, mesh.Positions.Length);
            Array.Copy(mesh.Positions, 0, _basePositions, vertexOffset * 3, mesh.Positions.Length);
            if (mesh.JointIds.Length == count)
                Array.Copy(mesh.JointIds, 0, _jointIds, vertexOffset, count);
            Array.Copy(mesh.Indices, 0, _indices, indexOffset, mesh.Indices.Length);
            byte[] colors = Convert.FromBase64String(mesh.Colors);
            Array.Copy(colors, 0, _colors, vertexOffset * 4, colors.Length);
            for (int i = 0; i < mesh.Uvs.Length; i += 2)
            {
                _uvs[(vertexOffset * 2) + i] = mesh.Uvs[i];
                _uvs[(vertexOffset * 2) + i + 1] = _model.Format == "MDLS" ? 1 - mesh.Uvs[i + 1] : mesh.Uvs[i + 1];
            }
            for (int i = 0; i < mesh.Positions.Length; i += 3)
                for (int axis = 0; axis < 3; axis++)
                {
                    float value = mesh.Positions[i + axis];
                    lo[axis] = Math.Min(lo[axis], value);
                    hi[axis] = Math.Max(hi[axis], value);
                }
            vertexOffset += count;
            for (int i = 0; i < mesh.Indices.Length; i++)
                _indices[indexOffset + i] = mesh.Indices[i] + vertexOffset - count;
            indexOffset += mesh.Indices.Length;
        }
        if (!float.IsFinite(lo[0])) { lo = new[] { -1f, -1f, -1f }; hi = new[] { 1f, 1f, 1f }; }
        for (int i = 0; i < 3; i++) _center[i] = (lo[i] + hi[i]) / 2;
        _radius = Math.Max(.01f, MathF.Sqrt(MathF.Pow(hi[0] - lo[0], 2) +
            MathF.Pow(hi[1] - lo[1], 2) + MathF.Pow(hi[2] - lo[2], 2)) / 2);
        _floorY = lo[1] - _radius * .001f;
        ResetCamera();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_ready || _renderContext == IntPtr.Zero) return;
        WglMakeCurrent(_deviceContext, _renderContext);
        GlViewport(0, 0, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        GlClearColor(.055f, .067f, .086f, 1);
        GlClear(GlColorBufferBit | GlDepthBufferBit);
        float aspect = Math.Max(.01f, (float)ClientSize.Width / Math.Max(1, ClientSize.Height));
        float near = Math.Max(.001f, _distance / 1000f), far = _distance + _radius * 100;
        float top = near * MathF.Tan(MathF.PI / 8);
        GlMatrixMode(GlProjection);
        GlLoadIdentity();
        GlFrustum(-top * aspect, top * aspect, -top, top, near, far);
        var target = new Vector3(_center[0] + _pan[0], _center[1] + _pan[1], _center[2] + _pan[2]);
        float sin = MathF.Sin(_phi);
        var eye = target + new Vector3(_distance * sin * MathF.Sin(_theta),
            _distance * MathF.Cos(_phi), _distance * sin * MathF.Cos(_theta));
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        GlMatrixMode(GlModelview);
        GlLoadMatrixf(new[] { view.M11, view.M12, view.M13, view.M14, view.M21, view.M22,
            view.M23, view.M24, view.M31, view.M32, view.M33, view.M34, view.M41, view.M42,
            view.M43, view.M44 });
        DrawGrid();
        if (_model != null) DrawModel();
        if (_showBones) DrawBones();
        SwapBuffers(_deviceContext);
    }

    private void DrawGrid()
    {
        float step = _radius * .5f, extent = step * 16;
        float y = _floorY;
        GlDisable(GlTexture2D);
        GlBegin(GlLines);
        for (int i = -16; i <= 16; i++)
        {
            float offset = i * step;
            bool axis = i == 0;
            GlColor4ub(axis ? (byte)235 : (byte)91, axis ? (byte)82 : (byte)108, axis ? (byte)82 : (byte)129, axis ? (byte)230 : (byte)105);
            GlVertex3f(_center[0] - extent, y, _center[2] + offset);
            GlVertex3f(_center[0] + extent, y, _center[2] + offset);
            GlColor4ub(axis ? (byte)86 : (byte)91, axis ? (byte)151 : (byte)108, axis ? (byte)255 : (byte)129, axis ? (byte)230 : (byte)105);
            GlVertex3f(_center[0] + offset, y, _center[2] - extent);
            GlVertex3f(_center[0] + offset, y, _center[2] + extent);
        }
        GlEnd();
    }

    private void DrawModel()
    {
        if (_model == null) return;
        GlDisable(GlBlend);
        GlEnable(GlAlphaTest);
        GlAlphaFunc(GlGreaterEqual, 0.5f);
        int vertexOffset = 0;
        foreach (ArdViewerMesh mesh in _model.Meshes)
        {
            bool hasTexture = mesh.Texture >= 0 && mesh.Texture < _textureIds.Length;
            if (hasTexture)
            {
                GlEnable(GlTexture2D);
                GlBindTexture(GlTexture2D, (uint)_textureIds[mesh.Texture]);
                int wrap = mesh.ClampTexture || !_model.Textures[mesh.Texture].Repeat ? GlClampToEdge : GlRepeat;
                GlTexParameteri(GlTexture2D, GlTextureWrapS, wrap);
                GlTexParameteri(GlTexture2D, GlTextureWrapT, wrap);
            }
            else GlDisable(GlTexture2D);
            GlBegin(GlTriangles);
            foreach (int index in mesh.Indices)
            {
                int vertex = vertexOffset + index;
                GlColor4ub(_colors[vertex * 4], _colors[vertex * 4 + 1], _colors[vertex * 4 + 2], _colors[vertex * 4 + 3]);
                if (hasTexture) GlTexCoord2f(_uvs[vertex * 2], _uvs[vertex * 2 + 1]);
                GlVertex3f(_positions[vertex * 3], _positions[vertex * 3 + 1], _positions[vertex * 3 + 2]);
            }
            GlEnd();
            vertexOffset += mesh.Positions.Length / 3;
        }
        GlDisable(GlAlphaTest);
        GlEnable(GlBlend);
    }

    private void DrawBones()
    {
        if (_model == null || _jointWorldMatrices.Length != _model.Joints.Count) return;

        GlDisable(GlTexture2D);
        GlDisable(GlDepthTest);
        GlLineWidth(2);
        GlColor4ub(94, 222, 255, 255);
        GlBegin(GlLines);
        for (int i = 0; i < _model.Joints.Count; i++)
        {
            int parent = _model.Joints[i].ParentIndex;
            if (parent < 0 || parent >= i) continue;
            Matrix4x4 parentWorld = _jointWorldMatrices[parent];
            Matrix4x4 jointWorld = _jointWorldMatrices[i];
            GlVertex3f(parentWorld.M41, parentWorld.M42, parentWorld.M43);
            GlVertex3f(jointWorld.M41, jointWorld.M42, jointWorld.M43);
        }
        GlEnd();

        GlPointSize(8);
        GlColor4ub(255, 192, 72, 255);
        GlBegin(GlPoints);
        foreach (Matrix4x4 jointWorld in _jointWorldMatrices)
            GlVertex3f(jointWorld.M41, jointWorld.M42, jointWorld.M43);
        GlEnd();
        GlEnable(GlDepthTest);
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button is not (MouseButtons.Left or MouseButtons.Middle or MouseButtons.Right)) return;
        Focus();
        _dragging = true;
        _dragButton = e.Button;
        _panning = _dragButton is MouseButtons.Middle or MouseButtons.Right ||
            ModifierKeys.HasFlag(Keys.Shift);
        _lastMouse = e.Location;
        Capture = true;
    }

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        int dx = e.X - _lastMouse.X, dy = e.Y - _lastMouse.Y;
        _lastMouse = e.Location;
        bool pan = _panning ||
            (_dragButton is MouseButtons.Right or MouseButtons.Middle &&
                Control.MouseButtons.HasFlag(_dragButton)) ||
            ModifierKeys.HasFlag(Keys.Shift);
        if (pan)
        {
            float scale = _distance / Math.Max(1, ClientSize.Height) * .8f;
            float[] right = { MathF.Cos(_theta), 0, -MathF.Sin(_theta) };
            float[] up = { -MathF.Sin(_theta) * MathF.Cos(_phi), MathF.Sin(_phi), -MathF.Cos(_theta) * MathF.Cos(_phi) };
            for (int i = 0; i < 3; i++) _pan[i] += (-dx * right[i] + dy * up[i]) * scale;
        }
        else
        {
            _theta -= dx * .008f;
            _phi = Math.Clamp(_phi - dy * .008f, .08f, 3.06f);
        }
        Invalidate();
    }

    private void HandleMouseWheel(object? sender, MouseEventArgs e)
    {
        _distance = Math.Clamp(_distance * MathF.Exp(-e.Delta / 120f * .12f), _radius * .2f, _radius * 100);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ready && _textureIds.Length > 0)
        {
            WglMakeCurrent(_deviceContext, _renderContext);
            GlDeleteTextures(_textureIds.Length, _textureIds);
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort Size, Version;
        public int Flags;
        public byte PixelType, ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift,
            AlphaBits, AlphaShift, AccumBits, AccumRedBits, AccumGreenBits, AccumBlueBits, AccumAlphaBits,
            DepthBits, StencilBits, AuxBuffers, LayerType, Reserved;
        public int LayerMask, VisibleMask, DamageMask;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr hdc, ref PixelFormatDescriptor pfd);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PixelFormatDescriptor pfd);
    [DllImport("gdi32.dll")] private static extern bool SwapBuffers(IntPtr hdc);
    [DllImport("opengl32.dll", EntryPoint = "wglCreateContext")] private static extern IntPtr WglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll", EntryPoint = "wglMakeCurrent")] private static extern bool WglMakeCurrent(IntPtr hdc, IntPtr context);
    [DllImport("opengl32.dll", EntryPoint = "wglDeleteContext")] private static extern bool WglDeleteContext(IntPtr context);
    [DllImport("opengl32.dll", EntryPoint = "glEnable")] private static extern void GlEnable(uint cap);
    [DllImport("opengl32.dll", EntryPoint = "glDisable")] private static extern void GlDisable(uint cap);
    [DllImport("opengl32.dll", EntryPoint = "glCullFace")] private static extern void GlCullFaceMode(uint mode);
    [DllImport("opengl32.dll", EntryPoint = "glBlendFunc")] private static extern void GlBlendFunc(uint src, uint dst);
    [DllImport("opengl32.dll", EntryPoint = "glAlphaFunc")] private static extern void GlAlphaFunc(uint function, float reference);
    [DllImport("opengl32.dll", EntryPoint = "glColorMaterial")] private static extern void GlColorMaterialFunc(uint face, uint mode);
    [DllImport("opengl32.dll", EntryPoint = "glPixelStorei")] private static extern void GlPixelStorei(uint pname, int value);
    [DllImport("opengl32.dll", EntryPoint = "glGenTextures")] private static extern void GlGenTextures(int count, [Out] int[] ids);
    [DllImport("opengl32.dll", EntryPoint = "glDeleteTextures")] private static extern void GlDeleteTextures(int count, [In] int[] ids);
    [DllImport("opengl32.dll", EntryPoint = "glBindTexture")] private static extern void GlBindTexture(uint target, uint id);
    [DllImport("opengl32.dll", EntryPoint = "glTexParameteri")] private static extern void GlTexParameteri(uint target, int name, int value);
    [DllImport("opengl32.dll", EntryPoint = "glTexImage2D")] private static extern void GlTexImage2D(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, [In] byte[] pixels);
    [DllImport("opengl32.dll", EntryPoint = "glViewport")] private static extern void GlViewport(int x, int y, int width, int height);
    [DllImport("opengl32.dll", EntryPoint = "glClearColor")] private static extern void GlClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll", EntryPoint = "glClear")] private static extern void GlClear(uint mask);
    [DllImport("opengl32.dll", EntryPoint = "glMatrixMode")] private static extern void GlMatrixMode(uint mode);
    [DllImport("opengl32.dll", EntryPoint = "glLoadIdentity")] private static extern void GlLoadIdentity();
    [DllImport("opengl32.dll", EntryPoint = "glFrustum")] private static extern void GlFrustum(double left, double right, double bottom, double top, double near, double far);
    [DllImport("opengl32.dll", EntryPoint = "glLoadMatrixf")] private static extern void GlLoadMatrixf([In] float[] matrix);
    [DllImport("opengl32.dll", EntryPoint = "glBegin")] private static extern void GlBegin(uint mode);
    [DllImport("opengl32.dll", EntryPoint = "glEnd")] private static extern void GlEnd();
    [DllImport("opengl32.dll", EntryPoint = "glLineWidth")] private static extern void GlLineWidth(float width);
    [DllImport("opengl32.dll", EntryPoint = "glPointSize")] private static extern void GlPointSize(float size);
    [DllImport("opengl32.dll", EntryPoint = "glColor4ub")] private static extern void GlColor4ub(byte r, byte g, byte b, byte a);
    [DllImport("opengl32.dll", EntryPoint = "glVertex3f")] private static extern void GlVertex3f(float x, float y, float z);
    [DllImport("opengl32.dll", EntryPoint = "glTexCoord2f")] private static extern void GlTexCoord2f(float u, float v);
}

internal static class NativeGltfExporter
{
    internal static string Create(
        ArdViewerModel model,
        string outputDirectory,
        int msetSectionIndex = -1,
        float msetFrame = 0)
    {
        using var binary = new MemoryStream();
        var views = new List<Dictionary<string, object>>();
        var accessors = new List<Dictionary<string, object>>();
        var meshes = new List<object>();
        var meshUsesSkin = new List<bool>();
        var samplers = new List<object>();
        var textures = new List<object>();
        var materials = new List<object>();
        var materialVariants = new Dictionary<(int Texture, bool Clamp), int>();
        int untexturedMaterial = -1;
        List<float[]>? msetLocals = msetSectionIndex >= 0 &&
            msetSectionIndex < model.MsetSections.Count &&
            model.MsetSections[msetSectionIndex].JointLocalMatrices.Count == model.Joints.Count
                ? ArdViewer.BuildMsetJointLocals(model.Joints, model.MsetSections[msetSectionIndex], msetFrame)
                : null;

        int GetMaterialIndex(ArdViewerMesh source)
        {
            int textureIndex = source.Texture;
            if (textureIndex < 0 || textureIndex >= model.Textures.Count)
            {
                if (untexturedMaterial >= 0) return untexturedMaterial;
                untexturedMaterial = materials.Count;
                materials.Add(new
                {
                    name = "Untextured",
                    pbrMetallicRoughness = new { baseColorFactor = new[] { 1, 1, 1, 1 }, metallicFactor = 0, roughnessFactor = 1 },
                    alphaMode = "MASK",
                    alphaCutoff = 0.5
                });
                return untexturedMaterial;
            }

            ArdViewerTexture texture = model.Textures[textureIndex];
            bool clamp = source.ClampTexture || !texture.Repeat;
            var key = (textureIndex, clamp);
            if (materialVariants.TryGetValue(key, out int existing)) return existing;
            int samplerIndex = samplers.Count;
            samplers.Add(new
            {
                magFilter = 9729, minFilter = 9729,
                wrapS = clamp ? 33071 : 10497,
                wrapT = clamp ? 33071 : 10497
            });
            int textureVariant = textures.Count;
            textures.Add(new { sampler = samplerIndex, source = textureIndex });
            int materialIndex = materials.Count;
            materials.Add(new
            {
                name = $"Material {textureIndex + 1}" + (clamp && texture.Repeat ? " (clamp)" : ""),
                pbrMetallicRoughness = new { baseColorTexture = new { index = textureVariant }, metallicFactor = 0, roughnessFactor = 1 },
                alphaMode = "MASK",
                alphaCutoff = 0.5
            });
            materialVariants.Add(key, materialIndex);
            return materialIndex;
        }

        int AddAccessor(byte[] bytes, int componentType, string type, int count, int target,
            bool normalized = false, float[]? min = null, float[]? max = null)
        {
            while (binary.Length % 4 != 0) binary.WriteByte(0);
            int offset = checked((int)binary.Length);
            binary.Write(bytes);
            int viewIndex = views.Count;
            var view = new Dictionary<string, object>
            {
                ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = bytes.Length
            };
            if (target != 0) view["target"] = target;
            views.Add(view);
            var accessor = new Dictionary<string, object>
            {
                ["bufferView"] = viewIndex, ["componentType"] = componentType, ["type"] = type, ["count"] = count
            };
            if (normalized) accessor["normalized"] = true;
            if (min != null) accessor["min"] = min;
            if (max != null) accessor["max"] = max;
            accessors.Add(accessor);
            return accessors.Count - 1;
        }

        bool hasSkeleton = model.Joints.Count > 1;
        foreach (ArdViewerMesh source in model.Meshes)
        {
            float[] positions = source.Positions;
            bool useSkin = hasSkeleton && source.JointIds.Length == source.Positions.Length / 3;
            (byte[] Joints, int JointComponentType, float[] Weights)? skinData = null;
            if (useSkin)
            {
                var vertexJoints = new ushort[source.JointIds.Length * 4];
                var weights = new float[source.JointIds.Length * 4];
                for (int vertex = 0; vertex < source.JointIds.Length; vertex++)
                {
                    vertexJoints[vertex * 4] = checked((ushort)source.JointIds[vertex]);
                    weights[vertex * 4] = 1;
                }
                byte[] jointBytes;
                int jointComponentType;
                if (model.Joints.Count <= byte.MaxValue + 1)
                {
                    jointBytes = vertexJoints.Select(value => checked((byte)value)).ToArray();
                    jointComponentType = 5121;
                }
                else
                {
                    jointBytes = ToBytes(vertexJoints);
                    jointComponentType = 5123;
                }
                skinData = (jointBytes, jointComponentType, weights);
            }
            float[] uv = (float[])source.Uvs.Clone();
            if (model.Format == "MDLS")
                for (int i = 1; i < uv.Length; i += 2) uv[i] = 1 - uv[i];
            float[] min = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            float[] max = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
            for (int i = 0; i < positions.Length; i += 3)
                for (int axis = 0; axis < 3; axis++)
                {
                    min[axis] = Math.Min(min[axis], positions[i + axis]);
                    max[axis] = Math.Max(max[axis], positions[i + axis]);
                }
            int positionAccessor = AddAccessor(ToBytes(positions), 5126, "VEC3",
                positions.Length / 3, 34962, min: min, max: max);
            var attributes = new Dictionary<string, int> { ["POSITION"] = positionAccessor };
            if (uv.Length == positions.Length / 3 * 2)
                attributes["TEXCOORD_0"] = AddAccessor(ToBytes(uv), 5126, "VEC2", uv.Length / 2, 34962);
            byte[] colors = Convert.FromBase64String(source.Colors);
            if (colors.Length == positions.Length / 3 * 4)
                attributes["COLOR_0"] = AddAccessor(colors, 5121, "VEC4", colors.Length / 4, 34962, normalized: true);
            byte[] indexBytes;
            int indexType;
            if (positions.Length / 3 <= ushort.MaxValue)
            {
                var shorts = source.Indices.Select(i => checked((ushort)i)).ToArray();
                indexBytes = ToBytes(shorts);
                indexType = 5123;
            }
            else
            {
                indexBytes = ToBytes(source.Indices);
                indexType = 5125;
            }
            int indexAccessor = AddAccessor(indexBytes, indexType, "SCALAR", source.Indices.Length, 34963);
            if (skinData is { } attributesForSkin)
            {
                attributes["JOINTS_0"] = AddAccessor(attributesForSkin.Joints, attributesForSkin.JointComponentType,
                    "VEC4", source.JointIds.Length, 34962);
                attributes["WEIGHTS_0"] = AddAccessor(ToBytes(attributesForSkin.Weights), 5126,
                    "VEC4", source.JointIds.Length, 34962);
            }
            int material = GetMaterialIndex(source);
            meshes.Add(new Dictionary<string, object>
            {
                ["name"] = source.Name,
                ["primitives"] = new[] { new { attributes, indices = indexAccessor, material, mode = 4 } }
            });
            meshUsesSkin.Add(skinData.HasValue);
        }

        bool hasSkin = meshUsesSkin.Any(value => value);
        var nodes = new List<Dictionary<string, object>>();
        for (int i = 0; i < meshes.Count; i++)
        {
            var node = new Dictionary<string, object>
            {
                ["mesh"] = i,
                ["name"] = model.Meshes[i].Name
            };
            if (meshUsesSkin[i]) node["skin"] = 0;
            nodes.Add(node);
        }

        var sceneNodes = Enumerable.Range(0, meshes.Count).ToList();
        var skins = new List<object>();
        if (hasSkin)
        {
            int boneNodeOffset = nodes.Count;
            int[] boneNodeIndices = Enumerable.Range(boneNodeOffset, model.Joints.Count).ToArray();
            for (int i = 0; i < model.Joints.Count; i++)
            {
                ArdViewerJoint joint = model.Joints[i];
                float[] localMatrix = msetLocals != null
                    ? msetLocals[i]
                    : joint.LocalMatrix;
                var node = new Dictionary<string, object>
                {
                    ["name"] = joint.Name,
                    ["matrix"] = localMatrix
                };
                int[] children = model.Joints.Select((candidate, childIndex) => (candidate, childIndex))
                    .Where(pair => pair.candidate.ParentIndex == i)
                    .Select(pair => boneNodeIndices[pair.childIndex])
                    .ToArray();
                if (children.Length > 0) node["children"] = children;
                nodes.Add(node);
                if (joint.ParentIndex < 0) sceneNodes.Add(boneNodeIndices[i]);
            }

            var inverseBindMatrices = model.Joints.SelectMany(joint => joint.InverseBindMatrix).ToArray();
            int inverseBindAccessor = AddAccessor(ToBytes(inverseBindMatrices), 5126, "MAT4",
                model.Joints.Count, 0);
            int skeletonRoot = model.Joints.FindIndex(joint => joint.ParentIndex < 0);
            skins.Add(new
            {
                name = $"{model.Name} armature",
                joints = boneNodeIndices,
                skeleton = skeletonRoot >= 0 ? boneNodeIndices[skeletonRoot] : boneNodeIndices[0],
                inverseBindMatrices = inverseBindAccessor
            });
        }

        byte[] data = binary.ToArray();
        string uri = "data:application/octet-stream;base64," + Convert.ToBase64String(data);
        var images = model.Textures.Select((texture, index) =>
        {
            string fileName = $"texture_{index + 1:D3}.png";
            File.WriteAllBytes(Path.Combine(outputDirectory, fileName), BitmapPngBytes(texture));
            return new { name = texture.Name, uri = fileName };
        }).ToArray();
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "ARD Extractor native viewer" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = sceneNodes } },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = materials,
            ["images"] = images,
            ["textures"] = textures,
            ["samplers"] = samplers,
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new[] { new { byteLength = data.Length, uri } }
        };
        if (skins.Count > 0) gltf["skins"] = skins;
        return JsonSerializer.Serialize(gltf);
    }

    private static byte[] ToBytes(float[] values)
    {
        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] ToBytes(int[] values)
    {
        byte[] bytes = new byte[values.Length * sizeof(int)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] ToBytes(ushort[] values)
    {
        byte[] bytes = new byte[values.Length * sizeof(ushort)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] BitmapPngBytes(ArdViewerTexture texture)
    {
        using Bitmap bitmap = MakeTextureBitmap(texture);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static Bitmap MakeTextureBitmap(ArdViewerTexture texture)
    {
        byte[] rgba = Convert.FromBase64String(texture.Rgba);
        var bitmap = new Bitmap(texture.Width, texture.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, texture.Width, texture.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[rgba.Length];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                bgra[i] = rgba[i + 2];
                bgra[i + 1] = rgba[i + 1];
                bgra[i + 2] = rgba[i];
                bgra[i + 3] = rgba[i + 3];
            }
            for (int y = 0; y < texture.Height; y++)
                Marshal.Copy(bgra, y * texture.Width * 4, data.Scan0 + y * data.Stride, texture.Width * 4);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
}
