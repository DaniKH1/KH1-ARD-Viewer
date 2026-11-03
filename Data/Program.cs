using System.Diagnostics;
using Kh1Import;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
            return RunNativeViewer(null);

        if (args.Length > 0 && args[0].Equals("--native-viewer", StringComparison.OrdinalIgnoreCase))
        {
            string? archivePath = args.Length > 1 ? args[1] : null;
            if (archivePath != null && !File.Exists(archivePath))
            {
                MessageBox.Show($"File not found:\n{archivePath}", "KH1 ARD Viewer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }

            return RunNativeViewer(archivePath);
        }

        if (args.Length > 0 && args[0].Equals("--viewer", StringComparison.OrdinalIgnoreCase))
            return RunWebViewer(args);

        if (args.Length < 1 || !File.Exists(args[0]))
        {
            MessageBox.Show("Please provide a valid .ard file.", "ARD Extractor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        try
        {
            string outputDirectory = args.Length > 1
                ? args[1]
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])) ?? ".", "Extract");
            Directory.CreateDirectory(outputDirectory);
            byte[] data = File.ReadAllBytes(args[0]);
            List<int> offsets = ArdParser.FindMdlsOffsets(data);
            int written = 0;
            foreach (int offset in offsets)
            {
                try
                {
                    int length = MdlsParser.ComputeLength(data, offset);
                    byte[] slice = data.AsSpan(offset, length).ToArray();
                    File.WriteAllBytes(Path.Combine(outputDirectory, $"{++written:D4}.mdls"), slice);
                }
                catch (Exception error)
                {
                    Debug.WriteLine($"Skipped model at 0x{offset:X}: {error.Message}");
                }
            }

            MessageBox.Show($"Extracted {written} model(s) to:\n{outputDirectory}",
                "Extraction complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Could not extract ARD file",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int RunNativeViewer(string? archivePath)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new NativeViewerForm(archivePath));
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Could not start KH1 ARD Viewer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int RunWebViewer(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            MessageBox.Show("Please provide a valid .ard file.", "KH1 ARD Viewer",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        string archivePath = args[1];
        string outputPath = args.Length > 2 && !args[2].Equals("--no-open", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(args[2])
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".",
                Path.GetFileNameWithoutExtension(archivePath) + ".viewer.html");
        bool openViewer = !args.Contains("--no-open", StringComparer.OrdinalIgnoreCase);
        try
        {
            List<ArdViewerModel> models = ArdViewer.Read(archivePath);
            File.WriteAllText(outputPath,
                ArdViewer.WriteHtml(Path.GetFileName(archivePath), models), new System.Text.UTF8Encoding(false));
            if (openViewer) ArdViewerServer.Run(Path.GetFileName(archivePath), models, true);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Could not create ARD viewer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
