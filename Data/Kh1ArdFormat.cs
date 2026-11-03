using System.Collections.Generic;

// Reader for Kingdom Hearts 1's ".ard" room-object archive. Reverse-engineered
// by diffing ARD_Extractor.exe's output against the source .ard byte-for-byte
// (no existing tool or doc covers this level of detail -- OpenKh's own Ard.cs
// only reads the outer table's resource-name entry, index 5).
//
// The outer container is the 32-entry offset table Ard.cs already reads
// (count + (count+1) absolute offsets). Entry 0 -- the ~1-2MB blob holding all
// of a room's compiled models -- is NOT a flat concatenation: it's its own
// nested container with the exact same shape (count + (count+1) offsets, this
// time LOCAL to entry 0's own start), one sub-entry per resource-list model
// slot. Each sub-entry is either a 256-byte empty stub (no data for that slot)
// or a real .mdls model starting with 0x80 bytes of header before its own
// "MOBJ" magic -- same layout Kh1MdlsFormat.cs already expects from a
// standalone .mdls file, just found at a different starting offset. The outer
// per-sub-entry slot is often padded well past the model's real end (ARD_Extractor
// trims to the model's true internal length before writing; MdlsParser.Parse
// doesn't need that trim since it never reads past what its own header fields
// say to).
//
// .moa/.mfa sub-entries (if any live in this same nested container under a
// different sub-format) are NOT detected here -- only ones that pass the MOBJ
// magic check are reported.
namespace Kh1Import
{
    public static class ArdParser
    {
        private const int MagicCode = 0x4A424F4D; // "MOBJ"
        private const int MdlsHeaderStart = 0x80;

        private static int I32(byte[] d, int o) => d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);

        // Returns, for every sub-entry of entry 0 that looks like a valid .mdls
        // model, the offset within `d` where MdlsParser.Parse(d, offset) should
        // start reading it.
        public static List<int> FindMdlsOffsets(byte[] d)
        {
            var result = new List<int>();
            if (d.Length < 8) return result;

            int outerCount = I32(d, 0);
            if (outerCount <= 0 || 4 + (outerCount + 1) * 4 > d.Length) return result;

            int entry0Start = I32(d, 4);
            if (entry0Start < 0 || entry0Start + 4 > d.Length) return result;

            int subCount = I32(d, entry0Start);
            if (subCount <= 0 || entry0Start + 4 + (subCount + 1) * 4 > d.Length) return result;

            for (int i = 0; i < subCount; i++)
            {
                int subStart = entry0Start + I32(d, entry0Start + 4 + i * 4);
                if (subStart < 0 || subStart + MdlsHeaderStart + 4 > d.Length) continue;
                if (I32(d, subStart + MdlsHeaderStart) == MagicCode)
                    result.Add(subStart);
            }
            return result;
        }
    }
}
