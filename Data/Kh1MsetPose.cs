using System.Numerics;

namespace Kh1Import;

// KH1's motion flags select FK, helper orientation, or an IK chain. Helper
// indices and joint indices belong to different tables. Solve in game space,
// then convert the resulting locals once for both the preview and glTF export.
internal static class Kh1MsetPose
{
    private const uint Skip = 0x4000000, Fixed = 0x2000000, FromRoot = 0x1000000, KeepPosition = 0x800000;

    internal static List<float[]> Evaluate(IReadOnlyList<ArdViewerJoint> joints,
        ArdViewerMsetSection section, float frame)
    {
        int count = joints.Count;
        float[][] values = joints.Select(joint => (float[])joint.TransformChannels.Clone()).ToArray();
        foreach (var channel in section.Channels) values[channel.Joint][channel.Channel - 1] = channel.Value;
        Matrix4x4[] locals = values.Select(value => Local(value, false)).ToArray();
        foreach (var curve in section.Curves) values[curve.Joint][curve.Channel - 1] = curve.Sample(frame);

        float[][] helperValues = section.Helpers.Select(helper => (float[])helper.TransformChannels.Clone()).ToArray();
        foreach (var curve in section.HelperCurves) helperValues[curve.Joint][curve.Channel - 1] = curve.Sample(frame);
        Matrix4x4[] helpers = new Matrix4x4[helperValues.Length];
        for (int i = 0; i < helpers.Length; i++)
            helpers[i] = i == 0 ? Matrix4x4.Identity : Local(helperValues[i], true) * helpers[section.Helpers[i].Parent];

        var world = new Matrix4x4[count];
        var rotation = new Matrix4x4[count];
        var scales = new Vector3[count];
        // Initial bind state is also the state of skipped bones and the starting
        // position of a keep-position bone when inspecting a clip independently.
        for (int i = 0; i < count; i++)
        {
            int parent = joints[i].ParentIndex;
            float[] bind = joints[i].TransformChannels;
            Matrix4x4 parentWorld = parent >= 0 ? world[parent] : Matrix4x4.Identity;
            rotation[i] = Local(bind, false) * (parent >= 0 ? rotation[parent] : Matrix4x4.Identity);
            scales[i] = Scale(bind) * (parent >= 0 ? scales[parent] : Vector3.One);
            world[i] = WithPosition(Matrix4x4.CreateScale(scales[i]) * rotation[i],
                Vector3.Transform(Translation(bind), parentWorld));
            rotation[i].Translation = Vector3.Zero;
        }

        for (int i = 0; i < count; i++)
        {
            uint flags = Flags(i);
            if ((flags & Skip) != 0) continue;
            int parent = joints[i].ParentIndex;
            Matrix4x4 parentWorld = parent >= 0 ? world[parent] : Matrix4x4.Identity;
            Matrix4x4 parentRotation = parent >= 0 ? rotation[parent] : Matrix4x4.Identity;
            if (!Matrix4x4.Invert(parentWorld, out Matrix4x4 inverseParent))
                throw new InvalidDataException($"Singular animated parent of joint {i}.");
            scales[i] = Scale(values[i]) * (parent >= 0 ? scales[parent] : Vector3.One);
            int chain = (int)((flags >> 21) & 3);
            if (chain != 0)
            {
                int end = i + (chain == 1 ? 1 : 2);
                if (end >= count) throw new InvalidDataException($"Truncated IK chain at joint {i}.");
                Vector3 target = Vector3.Transform(HelperPosition(PositionHelper(Flags(end))), inverseParent);
                Vector3 origin = Translation(values[i]);
                Vector3? pole = (flags & 3) == 3
                    ? Vector3.Transform(HelperPosition(OwnHelper(flags)), inverseParent) : null;
                float upperLength = joints[i + 1].TransformChannels[6] * values[i][0];
                float lowerLength = chain == 1 ? 0 : joints[end].TransformChannels[6] * values[i][0] * values[i + 1][0];
                (Matrix4x4 upper, Matrix4x4 lower) = Limb(origin, target, upperLength, lowerLength,
                    chain == 3, values[i][3], chain == 1 ? 0 : values[i + 1][3], pole, chain != 1);
                locals[i] = upper;
                world[i] = Matrix4x4.CreateScale(Scale(values[i])) * upper * parentWorld;
                rotation[i] = UnitRows(world[i], out scales[i]);
                if (chain != 1)
                {
                    Vector3 ownScale = Scale(values[i]);
                    var inverseScale = Matrix4x4.CreateScale(Reciprocal(ownScale));
                    Matrix4x4 lowerLocal = Matrix4x4.CreateScale(ownScale * Scale(values[i + 1])) * lower * inverseScale;
                    world[i + 1] = WithPosition(lowerLocal * world[i],
                        Vector3.Transform(Translation(joints[i + 1].TransformChannels), world[i]));
                    rotation[i + 1] = UnitRows(world[i + 1], out scales[i + 1]);
                }
                world[end].Translation = Vector3.Transform(Translation(joints[end].TransformChannels), world[end - 1]);
                rotation[i].Translation = Vector3.Zero;
                if (chain != 1)
                {
                    rotation[i + 1].Translation = Vector3.Zero;
                    i++;
                }
                continue;
            }

            int positionHelper = PositionHelper(flags);
            Vector3 position = (flags & KeepPosition) != 0 ? world[i].Translation
                : positionHelper != 0 ? HelperPosition(positionHelper)
                : Vector3.Transform(Translation(values[i]), parentWorld);
            switch (flags & 3)
            {
                case 0:
                    if ((flags & Fixed) == 0 || (flags & FromRoot) != 0) locals[i] = Local(values[i], false);
                    rotation[i] = locals[i] * ((flags & FromRoot) != 0 ? Matrix4x4.Identity : parentRotation);
                    break;
                case 1:
                    Vector3 localPosition = Vector3.Transform(position, inverseParent);
                    Vector3 target = Vector3.Transform(HelperPosition(OwnHelper(flags)), inverseParent);
                    Vector3 direction = target - localPosition;
                    Matrix4x4 aim = Aim(direction, Vector3.UnitY);
                    locals[i] = WithPosition(Matrix4x4.CreateRotationX(values[i][3]) * aim, localPosition);
                    rotation[i] = locals[i] * parentRotation;
                    break;
                case 2:
                    rotation[i] = UnitRows(Helper(OwnHelper(flags)), out _);
                    break;
            }
            world[i] = WithPosition(Matrix4x4.CreateScale(scales[i]) * rotation[i], position);
            rotation[i].Translation = Vector3.Zero;
        }

        Matrix4x4 basis = Matrix4x4.CreateScale(-0.01f, 0.01f, 0.01f);
        Matrix4x4.Invert(basis, out Matrix4x4 inverseBasis);
        var result = new List<float[]>(count);
        for (int i = 0; i < count; i++)
        {
            int parent = joints[i].ParentIndex;
            Matrix4x4 local = world[i];
            if (parent >= 0)
            {
                if (!Matrix4x4.Invert(world[parent], out Matrix4x4 inverse))
                    throw new InvalidDataException($"Singular animated parent of joint {i}.");
                local *= inverse;
            }
            Matrix4x4 m = inverseBasis * local * basis;
            result.Add([m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
                m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44]);
        }
        return result;

        uint Flags(int joint) => section.JointFlags.Length == count ? section.JointFlags[joint] : 0;
        Matrix4x4 Helper(int number) => number > 0 && number <= helpers.Length ? helpers[number - 1]
            : throw new InvalidDataException($"Invalid animation helper {number}.");
        Vector3 HelperPosition(int number) => Helper(number).Translation;
    }

    private static int OwnHelper(uint flags) => (int)((((flags >> 16) & 0x800) | (flags & 0x7fc)) >> 2);
    private static int PositionHelper(uint flags) => (int)((((flags >> 8) & 0x100000) | (flags & 0xff800)) >> 11);
    private static Vector3 Scale(float[] v) => new(v[0], v[1], v[2]);
    private static Vector3 Translation(float[] v) => new(v[6], v[7], v[8]);
    private static Vector3 Reciprocal(Vector3 v) => new(1 / Nonzero(v.X), 1 / Nonzero(v.Y), 1 / Nonzero(v.Z));
    private static float Nonzero(float value) => value == 0 ? 1e-6f : value;
    private static Matrix4x4 Local(float[] v, bool scale) =>
        (scale ? Matrix4x4.CreateScale(Scale(v)) : Matrix4x4.Identity) *
        Matrix4x4.CreateRotationX(v[3]) * Matrix4x4.CreateRotationY(v[4]) * Matrix4x4.CreateRotationZ(v[5]) *
        Matrix4x4.CreateTranslation(Translation(v));
    private static Matrix4x4 WithPosition(Matrix4x4 m, Vector3 p) { m.Translation = p; return m; }
    private static Vector3 Normalize(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.Zero;
    private static Matrix4x4 UnitRows(Matrix4x4 m, out Vector3 scale)
    {
        Vector3 x = new(m.M11, m.M12, m.M13), y = new(m.M21, m.M22, m.M23), z = new(m.M31, m.M32, m.M33);
        scale = new(x.Length(), y.Length(), z.Length());
        x /= Nonzero(scale.X); y /= Nonzero(scale.Y); z /= Nonzero(scale.Z);
        return WithPosition(Basis(x, y, z), m.Translation);
    }
    private static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z) => new(
        x.X,x.Y,x.Z,0, y.X,y.Y,y.Z,0, z.X,z.Y,z.Z,0, 0,0,0,1);
    private static Matrix4x4 Aim(Vector3 direction, Vector3 up)
    {
        Vector3 x = Normalize(direction);
        if (x == Vector3.Zero) x = Vector3.UnitX;
        Vector3 z = Normalize(Vector3.Cross(x, up));
        if (z == Vector3.Zero) z = Normalize(Vector3.Cross(x, Vector3.UnitZ));
        return Basis(x, Normalize(Vector3.Cross(z, x)), z);
    }
    private static (Matrix4x4 Upper, Matrix4x4 Lower) Limb(Vector3 origin, Vector3 target,
        float upperLength, float lowerLength, bool flipped, float upperTwist, float lowerTwist,
        Vector3? pole, bool twoBones)
    {
        Vector3 delta = target - origin;
        Vector3 direction = Normalize(delta);
        if (direction == Vector3.Zero) direction = Vector3.UnitX;
        Vector3 side = pole.HasValue ? pole.Value - origin : new(-direction.Y, direction.X, 0);
        Vector3 up = Normalize(Vector3.Cross(direction, side));
        if (up == Vector3.Zero) up = Normalize(Vector3.Cross(direction, Vector3.UnitZ));
        if (up == Vector3.Zero) up = Vector3.UnitY;
        side = Normalize(Vector3.Cross(up, direction));
        Matrix4x4 basis = Matrix4x4.CreateRotationX(upperTwist) * Basis(direction, side, up);
        if (!twoBones) return (WithPosition(basis, origin), Matrix4x4.Identity);
        float distance = delta.Length();
        float cosine = Math.Clamp((distance * distance - upperLength * upperLength - lowerLength * lowerLength) /
            Nonzero(2 * upperLength * lowerLength), -1, 1);
        float sine = MathF.Sqrt(Math.Max(0, 1 - cosine * cosine)) * (flipped ? -1 : 1);
        Matrix4x4 lower = Matrix4x4.CreateRotationX(lowerTwist) *
            new Matrix4x4(cosine,sine,0,0, -sine,cosine,0,0, 0,0,1,0, upperLength,0,0,1);
        float hip = MathF.Atan2(-sine * lowerLength, cosine * lowerLength + upperLength);
        return (WithPosition(Matrix4x4.CreateRotationZ(hip) * basis, origin), lower);
    }
}
