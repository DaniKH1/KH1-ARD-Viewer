using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace Kh1Import;

internal sealed class ArdViewerMsetSection
{
    public int Offset { get; set; }
    [JsonIgnore]
    public uint ReferenceValue { get; set; }
    public int JointCount { get; set; }
    public int FrameEnd { get; set; }
    public float FramesPerSecond { get; set; }
    public List<ArdViewerMsetChannel> Channels { get; set; } = new();
    public List<ArdViewerMsetCurve> Curves { get; set; } = new();
    public List<ArdViewerMsetCurve> HelperCurves { get; set; } = new();
    public List<ArdViewerMsetHelper> Helpers { get; set; } = new();
    public uint[] JointFlags { get; set; } = Array.Empty<uint>();
    public List<float[]> JointLocalMatrices { get; set; } = new();
    public int UnsupportedCurveCount { get; set; }
    [JsonIgnore]
    public byte[] RawData { get; set; } = Array.Empty<byte>();
    internal bool HasPose => Channels.Count != 0 || Curves.Count != 0 || Helpers.Count != 0;
    internal bool HasAnimation => Curves.Count != 0 || HelperCurves.Count != 0;
    internal int Length { get; set; }
}

internal sealed class ArdViewerMsetHelper
{
    public int Parent { get; set; }
    public float[] TransformChannels { get; set; } = Array.Empty<float>();
}

internal sealed class ArdViewerMsetChannel
{
    public int Joint { get; set; }
    public int Channel { get; set; }
    public float Value { get; set; }
}

internal sealed class ArdViewerMsetCurve
{
    public int Joint { get; set; }
    public int Channel { get; set; }
    public List<ArdViewerMsetKey> Keys { get; set; } = new();
    public int Before { get; set; }
    public int After { get; set; }

    internal float Sample(float frame)
    {
        if (Keys.Count == 0) return 0;
        // KH1 stores signed 24-bit times in 1/256-frame units, above the
        // interpolation byte. Tangents are derivatives in those same units.
        float time = MathF.Truncate(frame * 256);
        ArdViewerMsetKey first = Keys[0], last = Keys[^1];
        if (Keys.Count == 1) return first.Value;
        float span = last.Time - first.Time;
        if (time < first.Time || time > last.Time)
        {
            bool before = time < first.Time;
            int mode = before ? Before : After;
            ArdViewerMsetKey edge = before ? first : last;
            if (mode == 1)
            {
                ArdViewerMsetKey a = before ? first : Keys[^2];
                ArdViewerMsetKey b = before ? Keys[1] : last;
                float slope = edge.Interpolation == 1 && b.Time != a.Time
                    ? (b.Value - a.Value) / (b.Time - a.Time)
                    : before ? edge.InTangent : edge.OutTangent;
                return edge.Value + (time - edge.Time) * slope;
            }
            if (mode != 2 || span <= 0) return edge.Value;
            time = before
                ? time + MathF.Ceiling((first.Time - time) / span) * span
                : time - MathF.Ceiling((time - last.Time) / span) * span;
        }
        if (time <= first.Time) return first.Value;

        for (int i = 1; i < Keys.Count; i++)
        {
            ArdViewerMsetKey previous = Keys[i - 1];
            ArdViewerMsetKey next = Keys[i];
            if (time > next.Time) continue;
            if (time == next.Time) return next.Value;

            float duration = next.Time - previous.Time;
            if (duration <= 0) return next.Value;
            if (previous.Interpolation == 0) return previous.Value;
            float t = Math.Clamp((time - previous.Time) / duration, 0, 1);
            if (previous.Interpolation == 1)
                return previous.Value + t * (next.Value - previous.Value);
            float t2 = t * t;
            float t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * previous.Value +
                (t3 - 2 * t2 + t) * duration * previous.OutTangent +
                (-2 * t3 + 3 * t2) * next.Value +
                (t3 - t2) * duration * next.InTangent;
        }

        return Keys[^1].Value;
    }
}

internal sealed class ArdViewerMsetKey
{
    public int Time { get; set; }
    public int Interpolation { get; set; }
    public float Value { get; set; }
    public float InTangent { get; set; }
    public float OutTangent { get; set; }
}

internal static class Kh1MsetParser
{
    private const uint Magic = 0x4E544D4D;
    private const uint Terminator = 0xFFFFFFFF;
    private const int CurveRecordSize = 6;
    private const int KeyRecordSize = 16;

    internal static List<ArdViewerMsetSection> ReadEmbedded(byte[] modelData, int modelJointCount, int slot)
    {
        var sections = new List<ArdViewerMsetSection>();
        ReadOnlySpan<byte> data = modelData;
        for (int offset = 0; offset <= data.Length - 4; offset += 4)
        {
            if (ReadUInt32(data, offset) != Magic) continue;

            if (TryReadSection(data, offset, modelJointCount, out List<ArdViewerMsetSection> parsedSections, out string error))
            {
                sections.AddRange(parsedSections);
                foreach (ArdViewerMsetSection section in parsedSections)
                    if (section.UnsupportedCurveCount != 0)
                        Console.Error.WriteLine(
                            $"Ignored {section.UnsupportedCurveCount} unsupported MSET curves in MDLS slot {slot} at 0x{offset:X}.");
                if (error.Length != 0)
                    Console.Error.WriteLine($"Skipped invalid MSET references in MDLS slot {slot} at 0x{offset:X}: {error}");
                offset += parsedSections[0].Length - 4;
            }
            else
            {
                Console.Error.WriteLine($"Skipped MMTN in MDLS slot {slot} at 0x{offset:X}: {error}");
            }
        }

        return sections;
    }

    private static bool TryReadSection(
        ReadOnlySpan<byte> data,
        int offset,
        int modelJointCount,
        out List<ArdViewerMsetSection> sections,
        out string error)
    {
        sections = new List<ArdViewerMsetSection>();
        error = "";
        if (data.Length - offset < 0x38)
            return Fail("truncated MMTN header", out error);

        uint sectionLengthValue = ReadUInt32(data, offset + 4);
        if (sectionLengthValue < 0x38 || sectionLengthValue > data.Length - offset)
            return Fail("invalid section length", out error);
        int sectionLength = (int)sectionLengthValue;
        ReadOnlySpan<byte> block = data.Slice(offset, sectionLength);

        uint tableOffsetValue = ReadUInt32(block, 0x30);
        uint tableEndValue = ReadUInt32(block, 0x34);
        if (tableOffsetValue < 0x38 || tableEndValue < tableOffsetValue || tableEndValue > sectionLength)
            return Fail("invalid reference-table bounds", out error);

        int tableOffset = (int)tableOffsetValue;
        int tableEnd = (int)tableEndValue;
        int cursor = tableOffset;
        var references = new List<uint>();
        bool terminated = false;
        while (cursor <= tableEnd - 4)
        {
            uint reference = ReadUInt32(block, cursor);
            cursor += 4;
            if (reference == Terminator)
            {
                terminated = true;
                break;
            }
            references.Add(reference);
        }
        if (!terminated)
            return Fail("reference table has no terminator", out error);

        if (references.Count == 0)
            return Fail("reference table contains no animation entries", out error);

        var invalidReferences = new List<string>();
        foreach (uint reference in references)
        {
            ulong referenceOffsetValue = (ulong)tableOffset + reference;
            if (referenceOffsetValue + 0x40 > (ulong)sectionLength)
            {
                invalidReferences.Add($"0x{reference:X}: truncated animation descriptor");
                continue;
            }

            int referenceOffset = (int)referenceOffsetValue;
            uint frameEndValue = ReadUInt32(block, referenceOffset + 4);
            float framesPerSecond = BitConverter.Int32BitsToSingle((int)ReadUInt32(block, referenceOffset + 8));
            uint jointCountValue = ReadUInt32(block, referenceOffset + 0x10);
            uint flagsOffset = ReadUInt32(block, referenceOffset + 0x14);
            uint helperCount = ReadUInt32(block, referenceOffset + 0x2c);
            uint helperOffset = ReadUInt32(block, referenceOffset + 0x30);
            uint channelCountValue = ReadUInt32(block, referenceOffset + 0x34);
            uint channelOffsetValue = ReadUInt32(block, referenceOffset + 0x38);
            uint animationEndOffsetValue = ReadUInt32(block, referenceOffset + 0x3c);
            if (jointCountValue > int.MaxValue || jointCountValue != modelJointCount)
            {
                invalidReferences.Add($"0x{reference:X}: descriptor joint count {jointCountValue} does not match model joint count {modelJointCount}");
                continue;
            }
            if (frameEndValue > ushort.MaxValue || !float.IsFinite(framesPerSecond) ||
                framesPerSecond <= 0 || framesPerSecond > 1000)
            {
                invalidReferences.Add($"0x{reference:X}: invalid animation frame range or rate");
                continue;
            }
            if (channelCountValue > sectionLength / 8)
            {
                invalidReferences.Add($"0x{reference:X}: channel count exceeds section bounds");
                continue;
            }

            if (!Fits(block, referenceOffset, flagsOffset, jointCountValue, 4) ||
                !Fits(block, referenceOffset, helperOffset, helperCount, 40))
            {
                invalidReferences.Add($"0x{reference:X}: joint flags or helpers exceed section bounds");
                continue;
            }
            var flags = new uint[(int)jointCountValue];
            for (int i = 0; i < flags.Length; i++)
                flags[i] = ReadUInt32(block, referenceOffset + (int)flagsOffset + i * 4);
            var helpers = new List<ArdViewerMsetHelper>();
            bool invalidHelper = false;
            for (int i = 0; i < (int)helperCount; i++)
            {
                int start = referenceOffset + (int)helperOffset + i * 40;
                int parent = (int)ReadUInt32(block, start + 12);
                var values = new float[9];
                for (int axis = 0; axis < 9; axis++)
                    values[axis] = BitConverter.Int32BitsToSingle((int)ReadUInt32(block,
                        start + (axis < 3 ? axis * 4 : 16 + (axis - 3) * 4)));
                if (values.Any(value => !float.IsFinite(value)) || i > 0 && (parent < 0 || parent >= i))
                {
                    invalidHelper = true;
                    break;
                }
                helpers.Add(new ArdViewerMsetHelper { Parent = parent, TransformChannels = values });
            }
            if (invalidHelper)
            {
                invalidReferences.Add($"0x{reference:X}: invalid helper transform or parent");
                continue;
            }

            var channels = new List<ArdViewerMsetChannel>((int)channelCountValue);
            if (channelCountValue != 0)
            {
                if (channelOffsetValue > sectionLength)
                {
                    invalidReferences.Add($"0x{reference:X}: transform offset exceeds section bounds");
                    continue;
                }
                ulong channelStartValue = (ulong)tableOffset + reference + channelOffsetValue;
                ulong channelEndValue = channelStartValue + (ulong)channelCountValue * 8;
                if (channelStartValue < (ulong)tableOffset || channelEndValue > (ulong)sectionLength)
                {
                    invalidReferences.Add($"0x{reference:X}: transform records exceed section bounds");
                    continue;
                }

                int channelStart = (int)channelStartValue;
                bool invalidChannel = false;
                for (int i = 0; i < (int)channelCountValue; i++)
                {
                    int recordOffset = channelStart + i * 8;
                    int joint = ReadUInt16(block, recordOffset);
                    int channel = ReadUInt16(block, recordOffset + 2);
                    float value = BitConverter.Int32BitsToSingle((int)ReadUInt32(block, recordOffset + 4));
                    if (joint >= jointCountValue)
                    {
                        invalidReferences.Add($"0x{reference:X}: transform record references joint {joint}, outside the declared joint count");
                        invalidChannel = true;
                        break;
                    }
                    if (channel is < 1 or > 9)
                    {
                        invalidReferences.Add($"0x{reference:X}: transform record has unsupported channel {channel}");
                        invalidChannel = true;
                        break;
                    }
                    if (!float.IsFinite(value))
                    {
                        invalidReferences.Add($"0x{reference:X}: transform record contains a non-finite value");
                        invalidChannel = true;
                        break;
                    }

                    channels.Add(new ArdViewerMsetChannel { Joint = joint, Channel = channel, Value = value });
                }
                if (invalidChannel) continue;
            }

            uint firstCurveCount = ReadUInt32(block, referenceOffset + 0x18);
            uint firstCurveOffset = ReadUInt32(block, referenceOffset + 0x1c);
            uint secondCurveCount = ReadUInt32(block, referenceOffset + 0x20);
            uint secondCurveOffset = ReadUInt32(block, referenceOffset + 0x24);
            uint keyOffsetValue = ReadUInt32(block, referenceOffset + 0x28);
            if (firstCurveCount > sectionLength / CurveRecordSize ||
                secondCurveCount > sectionLength / CurveRecordSize)
            {
                invalidReferences.Add($"0x{reference:X}: curve count exceeds section bounds");
                continue;
            }
            bool hasCurves = firstCurveCount != 0 || secondCurveCount != 0;
            uint keyDataEndOffset = channelCountValue != 0 ? channelOffsetValue : animationEndOffsetValue;
            if (helperCount != 0 && helperOffset >= keyOffsetValue)
                keyDataEndOffset = Math.Min(keyDataEndOffset, helperOffset);
            if (hasCurves && (keyOffsetValue > keyDataEndOffset ||
                keyDataEndOffset > sectionLength - referenceOffset))
            {
                invalidReferences.Add($"0x{reference:X}: invalid curve-key or transform offset");
                continue;
            }

            ulong keyLimitValue = hasCurves && keyDataEndOffset >= keyOffsetValue
                ? (keyDataEndOffset - keyOffsetValue) / KeyRecordSize
                : 0;
            var curves = new List<ArdViewerMsetCurve>();
            var helperCurves = new List<ArdViewerMsetCurve>();
            int unsupportedCurveCount = 0;
            if (!TryReadCurves(block, referenceOffset, (int)jointCountValue,
                firstCurveCount, firstCurveOffset, keyOffsetValue, keyLimitValue,
                curves, ref unsupportedCurveCount, out string curveError) ||
                !TryReadCurves(block, referenceOffset, (int)helperCount,
                secondCurveCount, secondCurveOffset, keyOffsetValue, keyLimitValue,
                helperCurves, ref unsupportedCurveCount, out curveError))
            {
                invalidReferences.Add($"0x{reference:X}: {curveError}");
                continue;
            }

            sections.Add(new ArdViewerMsetSection
            {
                Offset = offset,
                ReferenceValue = reference,
                JointCount = (int)jointCountValue,
                Channels = channels,
                Curves = curves,
                HelperCurves = helperCurves,
                Helpers = helpers,
                JointFlags = flags,
                FrameEnd = (int)frameEndValue,
                FramesPerSecond = framesPerSecond,
                UnsupportedCurveCount = unsupportedCurveCount,
                Length = sectionLength,
                RawData = block.ToArray()
            });
        }

        if (invalidReferences.Count != 0)
            error = string.Join("; ", invalidReferences);
        if (sections.Count != 0) return true;

        error = invalidReferences.Count != 0
            ? string.Join("; ", invalidReferences)
            : "no valid animation entries";
        return false;
    }

    private static bool TryReadCurves(
        ReadOnlySpan<byte> block,
        int referenceOffset,
        int jointCount,
        uint curveCount,
        uint curveOffset,
        uint keyOffset,
        ulong keyLimit,
        List<ArdViewerMsetCurve> curves,
        ref int unsupportedCurveCount,
        out string error)
    {
        error = "";
        if (curveCount == 0) return true;

        ulong curveStartValue = (ulong)referenceOffset + curveOffset;
        ulong curveEndValue = curveStartValue + (ulong)curveCount * CurveRecordSize;
        if (curveStartValue < (ulong)referenceOffset || curveEndValue > (ulong)block.Length)
            return Fail("curve records exceed section bounds", out error);

        for (int i = 0; i < (int)curveCount; i++)
        {
            int recordOffset = (int)curveStartValue + i * CurveRecordSize;
            int joint = ReadUInt16(block, recordOffset);
            int channelCode = block[recordOffset + 2];
            int channel = channelCode & 0x0f;
            int keyCount = block[recordOffset + 3];
            int firstKey = ReadUInt16(block, recordOffset + 4);
            if (joint >= jointCount || channel is < 1 or > 9 || keyCount == 0 ||
                (ulong)firstKey + (ulong)keyCount > keyLimit)
                return Fail($"curve {i} has invalid joint, channel, or key range", out error);

            var keys = new List<ArdViewerMsetKey>(keyCount);
            bool unsupported = false;
            int previousTime = int.MinValue;
            for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                ulong keyRecordOffsetValue = (ulong)referenceOffset + keyOffset +
                    ((ulong)firstKey + (uint)keyIndex) * KeyRecordSize;
                if (keyRecordOffsetValue + KeyRecordSize > (ulong)block.Length)
                    return Fail($"curve {i} key records exceed section bounds", out error);

                int keyRecordOffset = (int)keyRecordOffsetValue;
                int keyHeader = (int)ReadUInt32(block, keyRecordOffset);
                int interpolation = keyHeader & 0xff;
                int time = keyHeader >> 8;
                float value = BitConverter.Int32BitsToSingle((int)ReadUInt32(block, keyRecordOffset + 4));
                float inTangent = BitConverter.Int32BitsToSingle((int)ReadUInt32(block, keyRecordOffset + 8));
                float outTangent = BitConverter.Int32BitsToSingle((int)ReadUInt32(block, keyRecordOffset + 12));
                if (((channelCode >> 4) & 3) == 3 || (channelCode >> 6) == 3)
                {
                    unsupported = true;
                    break;
                }
                // Curves can contain a support key beyond the clip's playback end.
                if (time < previousTime ||
                    !float.IsFinite(value) || !float.IsFinite(inTangent) || !float.IsFinite(outTangent))
                    return Fail($"curve {i} has invalid keyframe data", out error);

                keys.Add(new ArdViewerMsetKey
                {
                    Time = time,
                    Interpolation = interpolation,
                    Value = value,
                    InTangent = inTangent,
                    OutTangent = outTangent
                });
                previousTime = time;
            }

            if (unsupported)
            {
                unsupportedCurveCount++;
                continue;
            }

            curves.Add(new ArdViewerMsetCurve
            {
                Joint = joint, Channel = channel, Keys = keys,
                Before = (channelCode >> 4) & 3, After = channelCode >> 6
            });
        }

        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private static bool Fits(ReadOnlySpan<byte> block, int start, uint offset, uint count, int size) =>
        count == 0 || offset >= 0x40 && (ulong)start + offset + (ulong)count * (uint)size <= (ulong)block.Length;

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    private static int ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

}
