using System.Buffers.Binary;
using System.Numerics;

namespace Kh1Import;

internal sealed class ArdViewerMsetSection
{
    public int Offset { get; set; }
    public int JointCount { get; set; }
    public int FrameEnd { get; set; }
    public float FramesPerSecond { get; set; }
    public List<ArdViewerMsetChannel> Channels { get; set; } = new();
    public List<ArdViewerMsetCurve> Curves { get; set; } = new();
    public List<float[]> JointLocalMatrices { get; set; } = new();
    public int UnsupportedCurveCount { get; set; }
    internal int Length { get; set; }
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

    internal float Sample(float frame)
    {
        if (Keys.Count == 0) return 0;
        if (frame <= Keys[0].Frame) return Keys[0].Value;

        for (int i = 1; i < Keys.Count; i++)
        {
            ArdViewerMsetKey previous = Keys[i - 1];
            ArdViewerMsetKey next = Keys[i];
            if (frame > next.Frame) continue;

            float duration = next.Frame - previous.Frame;
            if (duration <= 0) return next.Value;
            float t = Math.Clamp((frame - previous.Frame) / duration, 0, 1);
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
    public int Frame { get; set; }
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
    private const int MinimumSupportedKeyType = 1;
    private const int MaximumSupportedKeyType = 4;

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
            int unsupportedCurveCount = 0;
            if (!TryReadCurves(block, referenceOffset, (int)jointCountValue, (int)frameEndValue,
                firstCurveCount, firstCurveOffset, keyOffsetValue, keyLimitValue,
                curves, ref unsupportedCurveCount, out string curveError) ||
                !TryReadCurves(block, referenceOffset, (int)jointCountValue, (int)frameEndValue,
                secondCurveCount, secondCurveOffset, keyOffsetValue, keyLimitValue,
                curves, ref unsupportedCurveCount, out curveError))
            {
                invalidReferences.Add($"0x{reference:X}: {curveError}");
                continue;
            }

            sections.Add(new ArdViewerMsetSection
            {
                Offset = offset,
                JointCount = (int)jointCountValue,
                Channels = channels,
                Curves = curves,
                FrameEnd = (int)frameEndValue,
                FramesPerSecond = framesPerSecond,
                UnsupportedCurveCount = unsupportedCurveCount,
                Length = sectionLength
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
        int frameEnd,
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
            int previousFrame = -1;
            for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                ulong keyRecordOffsetValue = (ulong)referenceOffset + keyOffset +
                    ((ulong)firstKey + (uint)keyIndex) * KeyRecordSize;
                if (keyRecordOffsetValue + KeyRecordSize > (ulong)block.Length)
                    return Fail($"curve {i} key records exceed section bounds", out error);

                int keyRecordOffset = (int)keyRecordOffsetValue;
                int keyType = ReadUInt16(block, keyRecordOffset);
                int frame = ReadUInt16(block, keyRecordOffset + 2);
                float value = BitConverter.Int32BitsToSingle((int)ReadUInt32(block, keyRecordOffset + 4));
                float inTangent = BitConverter.Int32BitsToSingle((int)ReadUInt32(block, keyRecordOffset + 8));
                float outTangent = BitConverter.Int32BitsToSingle((int)ReadUInt32(block, keyRecordOffset + 12));
                if (keyType < MinimumSupportedKeyType || keyType > MaximumSupportedKeyType)
                {
                    unsupported = true;
                    break;
                }
                if (frame > frameEnd || frame < previousFrame ||
                    !float.IsFinite(value) || !float.IsFinite(inTangent) || !float.IsFinite(outTangent))
                    return Fail($"curve {i} has invalid keyframe data", out error);

                keys.Add(new ArdViewerMsetKey
                {
                    Frame = frame,
                    Value = value,
                    InTangent = inTangent,
                    OutTangent = outTangent
                });
                previousFrame = frame;
            }

            if (unsupported)
            {
                unsupportedCurveCount++;
                continue;
            }

            curves.Add(new ArdViewerMsetCurve { Joint = joint, Channel = channel, Keys = keys });
        }

        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    private static int ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

}
