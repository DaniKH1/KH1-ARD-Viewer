using System.Numerics;
using System.Text;
using System.Text.Json;
using Kh1Import;

namespace Kh1Import;

internal sealed class ArdViewerModel
{
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public string Archive { get; set; } = "";
    public List<ArdViewerTexture> Textures { get; set; } = new();
    public List<ArdViewerMesh> Meshes { get; set; } = new();
    public List<ArdViewerJoint> Joints { get; set; } = new();
    public List<ArdViewerMsetSection> MsetSections { get; set; } = new();
}

internal sealed class ArdViewerJoint
{
    public string Name { get; set; } = "";
    public int ParentIndex { get; set; } = -1;
    public float[] TransformChannels { get; set; } = Array.Empty<float>();
    public float[] LocalMatrix { get; set; } = Array.Empty<float>();
    public float[] InverseBindMatrix { get; set; } = Array.Empty<float>();
}

internal sealed class ArdViewerTexture
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Rgba { get; set; } = "";
    public bool Repeat { get; set; }
}

internal sealed class ArdViewerMesh
{
    public string Name { get; set; } = "";
    public float[] Positions { get; set; } = Array.Empty<float>();
    public int[] JointIds { get; set; } = Array.Empty<int>();
    public float[] Uvs { get; set; } = Array.Empty<float>();
    public bool ClampTexture { get; set; }
    public string Colors { get; set; } = "";
    public int[] Indices { get; set; } = Array.Empty<int>();
    public int Texture { get; set; } = -1;
}

internal static class ArdViewer
{
    private const int EntryCount = 32;
    private const int ResourceListIndex = 5;
    private const int NameSize = 0x20;
    private const int Mobj = 0x4A424F4D;

    internal static List<ArdViewerModel> Read(string path)
    {
        return Read(File.ReadAllBytes(path));
    }

    internal static List<ArdViewerModel> Read(byte[] data, string archiveName = "")
    {
        int[] offsets = ReadOuterOffsets(data);
        List<string> resources = ReadResources(data, offsets);
        var models = new List<ArdViewerModel>();
        var names = GetModelSlotNames(resources);

        foreach (var blob in FindMdls(data, offsets))
        {
            try
            {
                byte[] slot = data.AsSpan(blob.Offset, blob.Length).ToArray();
                MdlsModel parsed = MdlsParser.Parse(slot);
                ArdViewerModel model = ConvertMdls(parsed, ModelSlotName(names, blob.Slot));
                foreach (ArdViewerMsetSection section in Kh1MsetParser.ReadEmbedded(slot, parsed.Joints.Count, blob.Slot))
                {
                    try
                    {
                        section.JointLocalMatrices = BuildMsetJointLocals(model.Joints, section, 0);
                        model.MsetSections.Add(section);
                    }
                    catch (InvalidDataException error)
                    {
                        Console.Error.WriteLine($"Skipped invalid MSET pose in MDLS slot {blob.Slot}: {error.Message}");
                    }
                }
                if (model.Meshes.Count != 0) models.Add(model);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Skipped MDLS slot {blob.Slot}: {error.Message}");
            }
        }

        int mfaIndex = 0;
        foreach (var blob in FindMfas(data, offsets))
        {
            string name = FindMfaName(resources, mfaIndex++);
            try
            {
                MfaModel parsed = MfaParser.Parse(data, blob.Offset, blob.Length);
                ArdViewerModel model = ConvertMfa(parsed, name);
                if (model.Meshes.Count != 0) models.Add(model);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Skipped MFA {name}: {error.Message}");
            }
        }

        foreach (ArdViewerModel model in models) model.Archive = archiveName;
        return models;
    }

    private static int[] ReadOuterOffsets(byte[] data)
    {
        const int headerSize = 4 + (EntryCount + 1) * 4;
        if (data.Length < headerSize || I32(data, 0) != EntryCount)
            throw new InvalidOperationException("Not a KH1 ARD archive (expected a 32-entry offset table).");

        var offsets = new int[EntryCount + 1];
        for (int i = 0; i <= EntryCount; i++) offsets[i] = I32(data, 4 + i * 4);
        if (offsets[0] < headerSize)
            throw new InvalidOperationException("ARD offset table overlaps its header.");
        for (int i = 0; i < EntryCount; i++)
            if (offsets[i] < 0 || offsets[i] > offsets[i + 1])
                throw new InvalidOperationException("ARD contains invalid or non-ascending entry offsets.");
        return offsets;
    }

    private static List<string> ReadResources(byte[] data, int[] offsets)
    {
        int start = offsets[ResourceListIndex];
        int end = Math.Min(offsets[ResourceListIndex + 1], data.Length);
        int length = end - start;
        if (start < 0 || start > data.Length || length < 0)
            throw new InvalidOperationException("ARD resource-list entry is invalid.");
        if (length == 0) return new List<string>();
        if (length % NameSize != 0)
            throw new InvalidOperationException("ARD resource-list entry is invalid.");

        var resources = new List<string>(length / NameSize);
        for (int offset = start; offset < end; offset += NameSize)
        {
            int count = 0;
            while (count < NameSize && data[offset + count] != 0) count++;
            resources.Add(Encoding.ASCII.GetString(data, offset, count));
        }
        return resources;
    }

    private static List<(int Offset, int Length, int Slot)> FindMdls(byte[] data, int[] offsets)
    {
        var result = new List<(int, int, int)>();
        int start = offsets[0];
        int end = Math.Min(offsets[1], data.Length);
        if (start < 0 || end - start < 8) return result;

        int count = I32(data, start);
        if (count <= 0 || count > 500 || start + 4L + (count + 1L) * 4 > end) return result;
        var local = new int[count + 1];
        int previous = -1;
        for (int i = 0; i <= count; i++)
        {
            int value = I32(data, start + 4 + i * 4);
            if (value < 0 || value < previous || start + (long)value > end) return result;
            local[i] = value;
            previous = value;
        }

        for (int i = 0; i < count; i++)
        {
            int modelStart = start + local[i];
            if (modelStart + 0x84 <= end && I32(data, modelStart + 0x80) == Mobj)
                result.Add((modelStart, local[i + 1] - local[i], i));
        }
        return result;
    }

    private static List<(int Offset, int Length)> FindMfas(byte[] data, int[] offsets)
    {
        var result = new List<(int, int)>();
        for (int entry = 0; entry < EntryCount; entry++)
        {
            int start = Math.Max(0, offsets[entry]);
            int end = Math.Min(offsets[entry + 1], data.Length);
            for (int offset = start; offset + 12 <= end; offset += 4)
            {
                if (I32(data, offset) != 3) continue;
                int nested = I32(data, offset + 4);
                int length = I32(data, offset + 8);
                int remaining = end - offset;
                if (nested != 0x80 || length < 0x100 || length > remaining || nested > remaining - 32) continue;
                if (I32(data, offset + nested) != 2) continue;
                int regionOffset = I32(data, offset + nested + 24);
                int regionLength = I32(data, offset + nested + 28);
                if (regionOffset < 0 || regionLength < 192 ||
                    regionOffset > length - nested || regionLength > length - nested - regionOffset) continue;
                result.Add((offset, length));
                offset += Math.Max(0, length - 4);
            }
        }
        return result;
    }

    private static ArdViewerModel ConvertMdls(MdlsModel source, string name)
    {
        var model = new ArdViewerModel { Name = name, Format = "MDLS" };
        foreach (MdlsImage image in source.Images)
            model.Textures.Add(new ArdViewerTexture
            {
                Name = $"{name}_texture_{model.Textures.Count + 1:D2}",
                Width = image.Width,
                Height = image.Height,
                Rgba = Convert.ToBase64String(FlipRows(image.Rgba, image.Width, image.Height)),
                Repeat = true
            });

        Matrix4x4[] joints = BuildJointWorld(source.Joints);
        bool rigged = source.Joints.Count > 1;
        Matrix4x4[] jointLocals = source.Joints.Select(BuildJointLocal).ToArray();
        Matrix4x4 basis = Matrix4x4.CreateScale(-0.01f, 0.01f, 0.01f);
        Matrix4x4.Invert(basis, out Matrix4x4 inverseBasis);
        for (int i = 0; i < source.Joints.Count; i++)
        {
            Matrix4x4 convertedWorld = inverseBasis * joints[i] * basis;
            if (!Matrix4x4.Invert(convertedWorld, out Matrix4x4 inverseBind))
                throw new InvalidDataException($"MDLS joint {i} has a singular bind transform.");
            int parent = source.Joints[i].ParentId == 0x3ff ? -1 : (int)source.Joints[i].ParentId;
            model.Joints.Add(new ArdViewerJoint
            {
                Name = $"{name} / joint {i}",
                ParentIndex = parent >= 0 && parent < i ? parent : -1,
                TransformChannels =
                [
                    source.Joints[i].ScaleX, source.Joints[i].ScaleY, source.Joints[i].ScaleZ,
                    source.Joints[i].RotateX, source.Joints[i].RotateY, source.Joints[i].RotateZ,
                    source.Joints[i].TranslateX, source.Joints[i].TranslateY, source.Joints[i].TranslateZ
                ],
                LocalMatrix = ToMatrixArray(inverseBasis * jointLocals[i] * basis),
                InverseBindMatrix = ToMatrixArray(inverseBind)
            });
        }

        for (int meshIndex = 0; meshIndex < source.Meshes.Count; meshIndex++)
        {
            MdlsMesh mesh = source.Meshes[meshIndex];
            var positions = new List<float>(mesh.Vertices.Count * 3);
            var uvs = new List<float>(mesh.Vertices.Count * 2);
            var colors = new List<byte>(mesh.Vertices.Count * 4);
            var jointIds = new int[mesh.Vertices.Count];
            int vertexIndex = 0;
            foreach (MdlsVertex vertex in mesh.Vertices)
            {
                var point = new Vector3(vertex.TranslateX, vertex.TranslateY, vertex.TranslateZ);
                jointIds[vertexIndex] = vertex.JointId >= 0 && vertex.JointId < source.Joints.Count
                    ? vertex.JointId
                    : 0;
                if (rigged && vertex.JointId >= 0 && vertex.JointId < joints.Length)
                    point = Vector3.Transform(point, joints[vertex.JointId]);
                positions.Add(-point.X * 0.01f);
                positions.Add(point.Y * 0.01f);
                positions.Add(point.Z * 0.01f);
                uvs.Add(vertex.TexCoordU);
                uvs.Add(vertex.TexCoordV);
                colors.AddRange(new byte[] { 255, 255, 255, 255 });
                vertexIndex++;
            }
            var indices = new List<int>(mesh.Faces.Count * 3);
            foreach (int[] face in mesh.Faces)
            {
                if (face.Length < 3) continue;
                indices.Add(face[0]);
                indices.Add(face[2]);
                indices.Add(face[1]);
            }
            if (indices.Count == 0) continue;
            model.Meshes.Add(new ArdViewerMesh
            {
                Name = $"{name} / mesh {meshIndex}",
                Positions = positions.ToArray(),
                Colors = Convert.ToBase64String(colors.ToArray()),
                Uvs = uvs.ToArray(),
                ClampTexture = HasSlightlyOutOfBoundsUvs(uvs),
                Indices = indices.ToArray(),
                JointIds = jointIds,
                Texture = mesh.TextureIndex >= 0 && mesh.TextureIndex < model.Textures.Count ? mesh.TextureIndex : -1
            });
        }

        return model;
    }

    private static Matrix4x4 BuildJointLocal(MdlsJoint joint) =>
        BuildJointLocal(joint.ScaleX, joint.ScaleY, joint.ScaleZ,
            joint.RotateX, joint.RotateY, joint.RotateZ,
            joint.TranslateX, joint.TranslateY, joint.TranslateZ);

    private static Matrix4x4 BuildJointLocal(
        float scaleX, float scaleY, float scaleZ,
        float rotateX, float rotateY, float rotateZ,
        float translateX, float translateY, float translateZ) =>
        Matrix4x4.CreateScale(scaleX, scaleY, scaleZ) *
        Matrix4x4.CreateRotationX(rotateX) *
        Matrix4x4.CreateRotationY(rotateY) *
        Matrix4x4.CreateRotationZ(rotateZ) *
        Matrix4x4.CreateTranslation(translateX, translateY, translateZ);

    internal static List<float[]> BuildMsetJointLocals(
        IReadOnlyList<ArdViewerJoint> joints,
        ArdViewerMsetSection section,
        float frame) => Kh1MsetPose.Evaluate(joints, section, frame);

    private static float[] ToMatrixArray(Matrix4x4 matrix) =>
    [
        matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44
    ];

    private static ArdViewerModel ConvertMfa(MfaModel source, string name)
    {
        var model = new ArdViewerModel { Name = name, Format = "MFA" };
        foreach (MfaTexture texture in source.Textures)
            model.Textures.Add(new ArdViewerTexture
            {
                Name = $"{name}_texture_{model.Textures.Count + 1:D2}",
                Width = texture.Width,
                Height = texture.Height,
                Rgba = Convert.ToBase64String(texture.Rgba),
                Repeat = false
            });

        for (int meshIndex = 0; meshIndex < source.Submeshes.Count; meshIndex++)
        {
            MfaSubmesh mesh = source.Submeshes[meshIndex];
            if (mesh.Faces.Count == 0) continue;
            var positions = new List<float>(mesh.Vertices.Count * 3);
            var uvs = new List<float>(mesh.Vertices.Count * 2);
            var colors = new List<byte>(mesh.Vertices.Count * 4);
            foreach (MfaVertex vertex in mesh.Vertices)
            {
                positions.Add(-vertex.X * 0.01f);
                positions.Add(-vertex.Z * 0.01f);
                positions.Add(vertex.Y * 0.01f);
                uvs.Add(vertex.U);
                uvs.Add(vertex.V);
                colors.Add(vertex.R);
                colors.Add(vertex.G);
                colors.Add(vertex.B);
                colors.Add(vertex.A);
            }
            var indices = new List<int>(mesh.Faces.Count * 3);
            foreach (int[] face in mesh.Faces)
            {
                if (face.Length < 3) continue;
                indices.Add(face[0]);
                indices.Add(face[1]);
                indices.Add(face[2]);
            }
            if (indices.Count == 0) continue;

            model.Meshes.Add(new ArdViewerMesh
            {
                Name = $"{name} / submesh {meshIndex}",
                Positions = positions.ToArray(),
                Uvs = uvs.ToArray(),
                ClampTexture = HasSlightlyOutOfBoundsUvs(uvs),
                Colors = Convert.ToBase64String(colors.ToArray()),
                Indices = indices.ToArray(),
                Texture = mesh.Texture == null ? -1 : source.Textures.IndexOf(mesh.Texture)
            });
        }
        return model;
    }

    private static Matrix4x4[] BuildJointWorld(List<MdlsJoint> joints)
    {
        var world = new Matrix4x4[joints.Count];
        for (int i = 0; i < joints.Count; i++)
        {
            MdlsJoint joint = joints[i];
            Matrix4x4 local = BuildJointLocal(joint);
            int parent = joint.ParentId == 0x3ff ? -1 : (int)joint.ParentId;
            world[i] = parent >= 0 && parent < i ? local * world[parent] : local;
        }
        return world;
    }

    private static byte[] FlipRows(byte[] rgba, int width, int height)
    {
        var flipped = new byte[rgba.Length];
        int row = checked(width * 4);
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(rgba, y * row, flipped, (height - y - 1) * row, row);
        return flipped;
    }

    internal static bool HasSlightlyOutOfBoundsUvs(IReadOnlyList<float> uvs)
    {
        const float tolerance = 0.05f;
        bool outsideUnitRange = false;
        foreach (float coordinate in uvs)
        {
            if (!float.IsFinite(coordinate) || coordinate < -tolerance || coordinate > 1 + tolerance)
                return false;
            if (coordinate < 0 || coordinate > 1)
                outsideUnitRange = true;
        }

        return outsideUnitRange;
    }

    private static List<string> GetModelSlotNames(List<string> resources)
    {
        var names = new List<string>();
        foreach (string resource in resources)
            if (!resource.EndsWith(".mset", StringComparison.OrdinalIgnoreCase))
                names.Add(Path.GetFileNameWithoutExtension(resource));
        return names;
    }

    private static string ModelSlotName(List<string> names, int slot) =>
        slot >= 0 && slot < names.Count ? names[slot] : $"model_slot_{slot}";

    private static string FindMfaName(List<string> resources, int mfaIndex)
    {
        int found = 0;
        foreach (string resource in resources)
            if (resource.EndsWith(".mfa", StringComparison.OrdinalIgnoreCase) && found++ == mfaIndex)
                return Path.GetFileNameWithoutExtension(resource);
        return $"mfa_{mfaIndex}";
    }

    private static int I32(byte[] data, int offset) =>
        data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24;

    internal static string WriteHtml(string title, List<ArdViewerModel> models)
    {
        string json = SerializeModels(models);
        return WebGlViewer.Html.Replace("__ARD_TITLE__", System.Net.WebUtility.HtmlEncode(title))
            .Replace("__ARD_DATA__", json);
    }

    internal static string SerializeModels(List<ArdViewerModel> models) =>
        JsonSerializer.Serialize(models, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
