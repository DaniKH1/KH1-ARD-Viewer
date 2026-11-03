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
    private readonly NativeActionButton _export = new();
    private readonly Dictionary<ArdViewerModel, TreeNode> _nodes = new();
    private readonly List<ArdViewerModel> _allModels = new();
    private System.Drawing.Icon? _applicationIcon;
    private bool _isLoading;
    private string? _lastFolder;
    private string? _settingsWarning;
    private ArdViewerModel? _selected;

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
        _models.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is ArdViewerModel model) SelectModel(model);
        };
        _models.AfterExpand += (_, e) => UpdateArchiveNodeLabel(e.Node);
        _models.AfterCollapse += (_, e) => UpdateArchiveNodeLabel(e.Node);
        _models.NodeMouseClick += (_, e) =>
        {
            if (e.Node?.Tag is not string || e.Node.Nodes.Count == 0) return;
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
        if (node == null || node.Tag is not string || node.Nodes.Count == 0) return;
        node.Text = $"{(node.IsExpanded ? "▾" : "▸")} {Path.GetFileName(node.Name)}";
    }

    private void BuildViewport(TableLayoutPanel parent)
    {
        var stage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 17, 22) };
        stage.Dock = DockStyle.Fill;
        parent.Controls.Add(stage, 1, 0);
        _viewport.Dock = DockStyle.Fill;
        stage.Controls.Add(_viewport);

        _stats.Size = new Size(116, 82);
        _stats.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _stats.Location = new Point(Math.Max(0, stage.ClientSize.Width - _stats.Width - 12), 12);
        _stats.BackColor = Color.FromArgb(24, 29, 37);
        _stats.ForeColor = Color.FromArgb(197, 207, 221);
        _stats.Padding = new Padding(8);
        _stats.BorderStyle = BorderStyle.FixedSingle;
        _stats.TextAlign = ContentAlignment.MiddleLeft;
        _stats.Visible = false;
        stage.Controls.Add(_stats);
        _stats.BringToFront();
        stage.Resize += (_, _) => _stats.Location = new Point(Math.Max(0, stage.ClientSize.Width - _stats.Width - 12), 12);

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

        NativeActionButton reset = new()
        {
            Text = "Reset camera",
            Size = new Size(162, 38),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        StyleButton(reset);
        reset.Location = new Point(stage.ClientSize.Width - reset.Width - 14, stage.ClientSize.Height - reset.Height - 14);
        reset.Click += (_, _) => _viewport.ResetCamera();
        stage.Controls.Add(reset);
        reset.BringToFront();
        stage.Resize += (_, _) => reset.Location = new Point(stage.ClientSize.Width - reset.Width - 14, stage.ClientSize.Height - reset.Height - 14);
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
                AddArchive(path, loaded);
                loadedCount++;
                _loadProgress.Text = $"{loadedCount} / {paths.Length} Files Loaded";
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

    private void AddArchive(string path, List<ArdViewerModel> loaded)
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
            var node = new TreeNode($"{model.Format} / {model.Name}") { Tag = model };
            archiveNode.Nodes.Add(node);
            _nodes[model] = node;
            _allModels.Add(model);
        }
        archiveNode.Text = $"▸ {archiveName}";
        _models.Nodes.Add(archiveNode);
    }

    private void SelectModel(ArdViewerModel model)
    {
        _selected = model;
        _viewport.SetModel(model);
        _export.Enabled = true;
        _export.Visible = true;
        _stats.Visible = true;
        int vertices = model.Meshes.Sum(mesh => mesh.Positions.Length / 3);
        int triangles = model.Meshes.Sum(mesh => mesh.Indices.Length / 3);
        _stats.Text = $"Meshes: {model.Meshes.Count}\r\nVertices: {vertices:N0}\r\nTriangles: {triangles:N0}\r\nTextures: {model.Textures.Count}";
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
        using var preview = new Form
        {
            Text = $"{texture.Name} · {texture.Width} × {texture.Height}",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            ClientSize = new Size(620, 620),
            BackColor = Color.FromArgb(16, 19, 25),
            KeyPreview = true
        };
        var image = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = MakeBitmap(texture) };
        preview.Controls.Add(image);
        preview.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) preview.Close(); };
        preview.ShowDialog(this);
        image.Image.Dispose();
    }

    private void ExportSelected()
    {
        if (_selected == null) return;
        try
        {
            string safeName = string.Concat(_selected.Name.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c)).Trim(' ', '.');
            if (safeName.Length == 0) safeName = "model";
            string directory = Path.Combine(AppContext.BaseDirectory, "Extract", safeName + "_extracted");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, safeName + ".gltf");
            File.WriteAllText(path, NativeGltfExporter.Create(_selected, directory), new UTF8Encoding(false));
            _status.Text = $"Exported: {path}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Could not export model",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (Control control in _textures.Controls)
        {
            if (control is Panel panel)
                foreach (Control child in panel.Controls)
                    if (child is PictureBox picture) picture.Image?.Dispose();
        }
        _applicationIcon?.Dispose();
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
    private const uint GlProjection = 0x1701;
    private const uint GlModelview = 0x1700;
    private const uint GlTriangles = 0x0004;
    private const uint GlLines = 0x0001;
    private const uint GlTexture2D = 0x0DE1;
    private const uint GlRgba = 0x1908;
    private const uint GlUnsignedByte = 0x1401;
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlLinear = 0x2601;
    private const int GlRepeat = 0x2901;
    private const int GlClamp = 0x2900;
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
    private float[] _uvs = Array.Empty<float>();
    private byte[] _colors = Array.Empty<byte>();
    private int[] _indices = Array.Empty<int>();
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
        if (_ready) UploadModel();
        Invalidate();
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
            GlTexParameteri(GlTexture2D, GlTextureWrapS, texture.Repeat ? GlRepeat : GlClamp);
            GlTexParameteri(GlTexture2D, GlTextureWrapT, texture.Repeat ? GlRepeat : GlClamp);
            GlTexImage2D(GlTexture2D, 0, (int)GlRgba, texture.Width, texture.Height, 0,
                GlRgba, GlUnsignedByte, rgba);
        }

        int vertexCount = _model.Meshes.Sum(mesh => mesh.Positions.Length / 3);
        _positions = new float[vertexCount * 3];
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
        float near = Math.Max(.001f, _distance / 1000f), far = _distance + _radius * 20;
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
        int vertexOffset = 0;
        foreach (ArdViewerMesh mesh in _model.Meshes)
        {
            bool hasTexture = mesh.Texture >= 0 && mesh.Texture < _textureIds.Length;
            if (hasTexture)
            {
                GlEnable(GlTexture2D);
                GlBindTexture(GlTexture2D, (uint)_textureIds[mesh.Texture]);
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
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button is not (MouseButtons.Left or MouseButtons.Middle or MouseButtons.Right)) return;
        Focus();
        _dragging = true;
        _panning = e.Button == MouseButtons.Middle || ModifierKeys.HasFlag(Keys.Shift);
        _lastMouse = e.Location;
        Capture = true;
    }

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        int dx = e.X - _lastMouse.X, dy = e.Y - _lastMouse.Y;
        _lastMouse = e.Location;
        if (_panning)
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
    [DllImport("opengl32.dll", EntryPoint = "glColor4ub")] private static extern void GlColor4ub(byte r, byte g, byte b, byte a);
    [DllImport("opengl32.dll", EntryPoint = "glVertex3f")] private static extern void GlVertex3f(float x, float y, float z);
    [DllImport("opengl32.dll", EntryPoint = "glTexCoord2f")] private static extern void GlTexCoord2f(float u, float v);
}

internal static class NativeGltfExporter
{
    internal static string Create(ArdViewerModel model, string outputDirectory)
    {
        using var binary = new MemoryStream();
        var views = new List<Dictionary<string, object>>();
        var accessors = new List<Dictionary<string, object>>();
        var meshes = new List<object>();

        int AddAccessor(byte[] bytes, int componentType, string type, int count, int target,
            bool normalized = false, float[]? min = null, float[]? max = null)
        {
            while (binary.Length % 4 != 0) binary.WriteByte(0);
            int offset = checked((int)binary.Length);
            binary.Write(bytes);
            int viewIndex = views.Count;
            views.Add(new Dictionary<string, object>
            {
                ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = bytes.Length, ["target"] = target
            });
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

        foreach (ArdViewerMesh source in model.Meshes)
        {
            float[] uv = (float[])source.Uvs.Clone();
            if (model.Format == "MDLS")
                for (int i = 1; i < uv.Length; i += 2) uv[i] = 1 - uv[i];
            float[] min = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            float[] max = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
            for (int i = 0; i < source.Positions.Length; i += 3)
                for (int axis = 0; axis < 3; axis++)
                {
                    min[axis] = Math.Min(min[axis], source.Positions[i + axis]);
                    max[axis] = Math.Max(max[axis], source.Positions[i + axis]);
                }
            int positionAccessor = AddAccessor(ToBytes(source.Positions), 5126, "VEC3",
                source.Positions.Length / 3, 34962, min: min, max: max);
            var attributes = new Dictionary<string, int> { ["POSITION"] = positionAccessor };
            if (uv.Length == source.Positions.Length / 3 * 2)
                attributes["TEXCOORD_0"] = AddAccessor(ToBytes(uv), 5126, "VEC2", uv.Length / 2, 34962);
            byte[] colors = Convert.FromBase64String(source.Colors);
            if (colors.Length == source.Positions.Length / 3 * 4)
                attributes["COLOR_0"] = AddAccessor(colors, 5121, "VEC4", colors.Length / 4, 34962, normalized: true);
            byte[] indexBytes;
            int indexType;
            if (source.Positions.Length / 3 <= ushort.MaxValue)
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
            int material = source.Texture >= 0 && source.Texture < model.Textures.Count
                ? source.Texture : model.Textures.Count;
            meshes.Add(new
            {
                name = source.Name,
                primitives = new[] { new { attributes, indices = indexAccessor, material, mode = 4 } }
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
        var samplers = model.Textures.Select(texture => new
        {
            magFilter = 9729, minFilter = 9729,
            wrapS = texture.Repeat ? 10497 : 33071,
            wrapT = texture.Repeat ? 10497 : 33071
        }).ToArray();
        var textures = model.Textures.Select((_, i) => new { sampler = i, source = i }).ToArray();
        var materials = model.Textures.Select((_, i) => new
        {
            name = $"Material {i + 1}",
            pbrMetallicRoughness = new { baseColorTexture = new { index = i }, metallicFactor = 0, roughnessFactor = 1 },
            alphaMode = "BLEND"
        }).Cast<object>().Append(new
        {
            name = "Untextured",
            pbrMetallicRoughness = new { baseColorFactor = new[] { 1, 1, 1, 1 }, metallicFactor = 0, roughnessFactor = 1 }
        }).ToArray();

        return JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0", generator = "ARD Extractor native viewer" },
            scene = 0,
            scenes = new[] { new { nodes = meshes.Select((_, i) => i).ToArray() } },
            nodes = meshes.Select((mesh, i) => new { mesh = i, name = model.Meshes[i].Name }).ToArray(),
            meshes, materials, images, textures, samplers, accessors, bufferViews = views,
            buffers = new[] { new { byteLength = data.Length, uri } }
        });
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
