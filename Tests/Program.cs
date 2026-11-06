using System.Numerics;
using System.Text.Json;
using Kh1Import;

static void Near(float actual, float expected, string label, float tolerance = 0.0001f)
{
    if (!float.IsFinite(actual) || Math.Abs(actual - expected) > tolerance)
        throw new Exception($"{label}: expected {expected}, got {actual}");
}

static Matrix4x4 Matrix(float[] v) => new(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],
    v[8],v[9],v[10],v[11],v[12],v[13],v[14],v[15]);
static Matrix4x4[] Worlds(ArdViewerModel model, ArdViewerMsetSection section, float frame)
{
    var locals = ArdViewer.BuildMsetJointLocals(model.Joints, section, frame);
    var worlds = new Matrix4x4[locals.Count];
    for (int i = 0; i < worlds.Length; i++)
    {
        int parent = model.Joints[i].ParentIndex;
        worlds[i] = Matrix(locals[i]) * (parent >= 0 ? worlds[parent] : Matrix4x4.Identity);
    }
    return worlds;
}

var curve = new ArdViewerMsetCurve
{
    Keys = [new() { Time = 128, Value = 2, Interpolation = 0 }, new() { Time = 384, Value = 6 }]
};
Near(curve.Sample(1), 2, "hold interpolation");
Near(curve.Sample(1.5f), 6, "exact hold endpoint");
curve.Keys[0].Interpolation = 1;
Near(curve.Sample(1), 4, "linear interpolation at fractional frame");
curve.Before = curve.After = 1;
curve.Keys[1].Interpolation = 1;
Near(curve.Sample(0), 0, "linear before");
Near(curve.Sample(2), 8, "linear after");
curve.Before = curve.After = 2;
Near(curve.Sample(0), 4, "repeat before");
Near(curve.Sample(2), 4, "repeat after");
curve.Keys[0].Interpolation = 2;
curve.Keys[0].OutTangent = 1f / 256;
curve.Keys[1].InTangent = 0;
Near(curve.Sample(1), 4.125f, "Hermite tangent units");
curve.Keys[0].Time = -128;
Near(curve.Sample(-0.5f), 2, "signed key time");
Console.WriteLine("PASS: hold, linear, Hermite, fractional/signed times, extrapolation and repeat.");

// A small synthetic MMTN with a helper index greater than the joint count.
// Encoded bytes exercise the parser, independently of the sampler's objects.
var binary = new byte[0x180];
void U32(int offset, uint value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(offset), value);
void U16(int offset, ushort value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(offset), value);
void F32(int offset, float value) => U32(offset, (uint)BitConverter.SingleToInt32Bits(value));
U32(0, 0x4e544d4d); U32(4, (uint)binary.Length);
U32(0x30, 0x40); U32(0x34, 0x48); U32(0x40, 0x10); U32(0x44, uint.MaxValue);
const int descriptor = 0x50;
U32(descriptor + 4, 2); F32(descriptor + 8, 60); U32(descriptor + 0x10, 1);
U32(descriptor + 0x14, 0x40);
U32(descriptor + 0x18, 1); U32(descriptor + 0x1c, 0x44);
U32(descriptor + 0x20, 1); U32(descriptor + 0x24, 0x4c);
U32(descriptor + 0x28, 0x60);
U32(descriptor + 0x2c, 3); U32(descriptor + 0x30, 0xb0); U32(descriptor + 0x3c, 0x128);
binary[0x96] = 9; binary[0x97] = 3;
U16(0x9c, 2); binary[0x9e] = 6; binary[0x9f] = 2; U16(0xa0, 3);
int[] times = [-128, 128, 768, 0, 256];
for (int i = 0; i < times.Length; i++)
{
    int start = 0xb0 + i * 16;
    U32(start, unchecked((uint)((times[i] << 8) | (i == 0 ? 0 : 1)))); F32(start + 4, i);
}
for (int i = 0; i < 3; i++)
    for (int j = 0; j < 3; j++) F32(0x100 + i * 40 + j * 4, 1);
var parsed = Kh1MsetParser.ReadEmbedded(binary, 1, 0);
if (parsed.Count != 1 || parsed[0].Curves.Count != 1 || parsed[0].HelperCurves.Count != 1 ||
    parsed[0].HelperCurves[0].Joint != 2 || parsed[0].Curves[0].Keys[0].Time != -128)
    throw new Exception("Packed keys or separate helper channel table decoded incorrectly.");
Near(parsed[0].Curves[0].Sample(0), 0, "parsed hold key");
Near(parsed[0].Curves[0].Sample(1.75f), 1.5f, "support key beyond clip duration");
Console.WriteLine("PASS: packed MMTN keys, helper index space and support keys beyond clip end.");

static ArdViewerJoint Joint(int parent, float x = 0) => new()
{
    ParentIndex = parent, TransformChannels = [1,1,1, 0,0,0, x,0,0]
};
parsed[0].JointFlags[0] = 0x200180e; // Copy helper 2's orientation and position.
var helperLocal = Matrix(ArdViewer.BuildMsetJointLocals([Joint(-1)], parsed[0], 0)[0]);
Near(helperLocal.M11, MathF.Cos(3), "helper orientation");
Near(helperLocal.M12, -MathF.Sin(3), "helper orientation in viewer basis");
if (!parsed[0].HasAnimation) throw new Exception("Helper animation was not playable.");
var limbModel = new ArdViewerModel { Joints = [Joint(-1), Joint(0, 1), Joint(1, 1)] };
var limbSection = new ArdViewerMsetSection
{
    JointFlags = [0x400000, 0, 0x1000],
    Helpers = [new() { TransformChannels = [1,1,1,0,0,0,0,0,0] },
        new() { Parent = 0, TransformChannels = [1,1,1,0,0,0,1,1,0] }]
};
var limbWorlds = Worlds(limbModel, limbSection, 0);
Near(limbWorlds[2].Translation.X, -0.01f, "IK target X");
Near(limbWorlds[2].Translation.Y, 0.01f, "IK target Y");
Near(limbWorlds[1].Translation.X, -0.01f, "IK elbow X");
Near(limbWorlds[1].Translation.Y, 0, "IK elbow Y");
Console.WriteLine("PASS: helper-driven orientation and two-bone IK target/elbow positions.");

var helperOnlyJoint = Joint(-1);
helperOnlyJoint.LocalMatrix = helperOnlyJoint.InverseBindMatrix = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
var helperOnlySection = new ArdViewerMsetSection
{
    FrameEnd = 120, FramesPerSecond = 60, JointFlags = [0x100a],
    Helpers = [new() { TransformChannels = [1,1,1,0,0,0,0,0,0] },
        new() { Parent = 0, TransformChannels = [1,1,1,0,0,0,0,0,0] }],
    HelperCurves = [new() { Joint = 1, Channel = 8, Keys = [
        new() { Time = 0, Value = 0, Interpolation = 1 },
        new() { Time = 60 * 256, Value = 10, Interpolation = 1 }] }]
};
helperOnlySection.JointLocalMatrices = ArdViewer.BuildMsetJointLocals([helperOnlyJoint], helperOnlySection, 0);
PlaybackRegression.Check(new ArdViewerModel
{
    Joints = [helperOnlyJoint], MsetSections = [helperOnlySection],
    Meshes = [new() { Positions = [1,0,0], JointIds = [0] }]
}, 0);
Console.WriteLine("PASS: helper-only Play, timer tick, vertex movement and Pause.");

if (args.Length == 3 && args[0] == "--playback-scan")
{
    PlaybackRegression.AuditDirectory(args[1], args[2]);
}
else if (args.Length == 2 && args[0] == "--reference")
{
    using var fixture = JsonDocument.Parse(File.ReadAllText(args[1]));
    int samples = 0;
    foreach (var archive in fixture.RootElement.EnumerateArray())
    {
        var models = ArdViewer.Read(archive.GetProperty("path").GetString()!);
        foreach (var expected in archive.GetProperty("models").EnumerateArray())
        {
            string name = expected.GetProperty("name").GetString()!;
            var model = models.Single(m => m.Name == name);
            if (name == "nm_6105")
            {
                PlaybackRegression.Check(model, 1);
                Console.WriteLine("PASS: nm_6105 helper-only animation advances and moves viewport vertices.");
            }
            var poses = expected.GetProperty("poses");
            foreach (var pose in poses.EnumerateArray())
            {
                int index = pose.GetProperty("clip").GetInt32();
                float frame = pose.GetProperty("frame").GetSingle();
                var worlds = Worlds(model, model.MsetSections[index], frame);
                var expectedWorlds = pose.GetProperty("worlds").EnumerateArray().ToArray();
                for (int i = 0; i < worlds.Length; i++)
                {
                    Matrix4x4 w = worlds[i];
                    float[] values = [w.M11,w.M12,w.M13,w.M14,w.M21,w.M22,w.M23,w.M24,
                        w.M31,w.M32,w.M33,w.M34,w.M41,w.M42,w.M43,w.M44];
                    var wanted = expectedWorlds[i].EnumerateArray().Select(v => v.GetSingle()).ToArray();
                    for (int j = 0; j < 16; j++)
                        Near(values[j], wanted[j], $"{name} clip {index} frame {frame} joint {i} component {j}", 0.002f);
                }
                samples++;
            }
            Console.WriteLine($"PASS: {name}, {model.MsetSections.Count} clips, {poses.GetArrayLength()} reference poses.");
        }
    }
    Console.WriteLine($"PASS: {samples} poses match independent KH1 reference evaluation.");
}
else if (args.Length == 2 && args[0] == "--scan")
{
    int models = 0, clips = 0, poses = 0;
    foreach (string path in Directory.EnumerateFiles(args[1], "*.ard"))
    {
        foreach (var model in ArdViewer.Read(path))
        {
            models++;
            foreach (var section in model.MsetSections)
            {
                clips++;
                foreach (float frame in new[] { 0f, 0.5f, section.FrameEnd * 0.37f, section.FrameEnd * 0.8f })
                {
                    var worlds = Worlds(model, section, frame);
                    foreach (var world in worlds)
                    {
                        if (!float.IsFinite(world.GetDeterminant()) || !float.IsFinite(world.Translation.Length()))
                            throw new Exception($"Non-finite pose: {path}, {model.Name}, {frame}");
                    }
                    poses++;
                }
            }
        }
        Console.WriteLine($"Scanned {Path.GetFileName(path)} ({models} models, {clips} clips, {poses} poses total)");
    }
}
