using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Kh1Import;

internal static class ArdViewerServer
{
    internal static void Run(string title, List<ArdViewerModel> models, bool openBrowser)
    {
        int port = GetAvailablePort();
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        Console.WriteLine($"ARD Viewer listening at {prefix}");
        Console.WriteLine("Keep this process running while using the viewer. Press Ctrl+C to stop.");
        if (openBrowser)
            Process.Start(new ProcessStartInfo { FileName = prefix, UseShellExecute = true });

        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = listener.GetContext(); }
            catch (HttpListenerException) { break; }
            _ = Task.Run(() => Handle(context, title, models));
        }
    }

    private static async Task Handle(HttpListenerContext context, string title, List<ArdViewerModel> initialModels)
    {
        try
        {
            if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/")
            {
                await Write(context.Response, 200, "text/html; charset=utf-8", ArdViewer.WriteHtml(title, initialModels));
                return;
            }

            if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/load")
            {
                using var buffer = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(buffer);
                string archiveName = Path.GetFileName(context.Request.QueryString["name"] ?? "archive.ard");
                List<ArdViewerModel> models = ArdViewer.Read(buffer.ToArray(), archiveName);
                string json = "{\"name\":" + System.Text.Json.JsonSerializer.Serialize(archiveName) +
                    ",\"models\":" + ArdViewer.SerializeModels(models) + "}";
                await Write(context.Response, 200, "application/json; charset=utf-8", json);
                return;
            }

            if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/export")
            {
                using JsonDocument document = await JsonDocument.ParseAsync(context.Request.InputStream);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("modelName", out JsonElement nameElement) ||
                    !root.TryGetProperty("gltf", out JsonElement gltfElement) ||
                    !root.TryGetProperty("textures", out JsonElement texturesElement) ||
                    nameElement.ValueKind != JsonValueKind.String ||
                    gltfElement.ValueKind != JsonValueKind.String ||
                    texturesElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Export request must include modelName, gltf, and textures.");

                string modelName = SafeFileName(nameElement.GetString() ?? "");
                string gltf = gltfElement.GetString() ?? "";
                if (modelName.Length == 0 || gltf.Length == 0)
                    throw new InvalidDataException("Model name and glTF content cannot be empty.");

                string folderName = modelName + "_extracted";
                string relativePath = Path.Combine("Extract", folderName, modelName + ".gltf");
                string outputPath = Path.Combine(AppContext.BaseDirectory, relativePath);
                string outputDirectory = Path.GetDirectoryName(outputPath)!;
                var textureFiles = new List<(string FileName, byte[] Data)>();
                var textureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonElement textureElement in texturesElement.EnumerateArray())
                {
                    if (!textureElement.TryGetProperty("fileName", out JsonElement fileNameElement) ||
                        !textureElement.TryGetProperty("data", out JsonElement textureDataElement) ||
                        fileNameElement.ValueKind != JsonValueKind.String ||
                        textureDataElement.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("Each exported texture must include a fileName and data URI.");

                    string fileName = fileNameElement.GetString() ?? "";
                    string number = fileName.StartsWith("texture_", StringComparison.Ordinal) &&
                        fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        ? fileName[8..^4] : "";
                    if (Path.GetFileName(fileName) != fileName || number.Length < 3 ||
                        !number.All(char.IsAsciiDigit) || !textureNames.Add(fileName))
                        throw new InvalidDataException("Exported texture filename is invalid.");

                    const string dataPrefix = "data:image/png;base64,";
                    string dataUri = textureDataElement.GetString() ?? "";
                    if (!dataUri.StartsWith(dataPrefix, StringComparison.Ordinal))
                        throw new InvalidDataException("Exported textures must be PNG data URIs.");

                    byte[] imageBytes = Convert.FromBase64String(dataUri[dataPrefix.Length..]);
                    byte[] pngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                    if (imageBytes.Length < pngSignature.Length ||
                        !imageBytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
                        throw new InvalidDataException("Exported texture data is not a valid PNG.");
                    textureFiles.Add((fileName, imageBytes));
                }

                Directory.CreateDirectory(outputDirectory);
                foreach ((string fileName, byte[] imageBytes) in textureFiles)
                    await File.WriteAllBytesAsync(Path.Combine(outputDirectory, fileName), imageBytes);
                await File.WriteAllTextAsync(outputPath, gltf, new UTF8Encoding(false));
                string json = JsonSerializer.Serialize(new { path = relativePath });
                await Write(context.Response, 200, "application/json; charset=utf-8", json);
                return;
            }

            await Write(context.Response, 404, "text/plain; charset=utf-8", "Not found");
        }
        catch (Exception error)
        {
            if (context.Response.OutputStream.CanWrite)
                await Write(context.Response, 400, "text/plain; charset=utf-8", error.Message);
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        char[] chars = name.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        return new string(chars).Trim(' ', '.');
    }

    private static async Task Write(HttpListenerResponse response, int status, string contentType, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        response.StatusCode = status;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static int GetAvailablePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}
