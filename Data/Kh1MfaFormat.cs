#nullable disable
#if true // Active MFA V2 reader, based on HyperCrown getMfaVerticesAndFaces/getMfaTextures.
using System;
using System.Collections.Generic;

// Reader for Kingdom Hearts 1's ".mfa" format. Reverse-engineered from
// HyperCrown's decompiled getMfaVerticesAndFaces (F:\KH1 Modding\HyperCrown\
// decompiled\-Module-.cs) -- unlike .moa, .mfa is NOT a wrapper around the
// MOBJ format .mdls uses: it's a genuinely different, jointless vertex format
// ("VertexGlobal" in HyperCrown's own naming), closer in spirit to Kh1BinFormat's
// room-geometry triangle strips (same "sign of a per-vertex w decides winding"
// trick) than to Kh1MdlsFormat's skinned MOBJ meshes.
//
// Confirmed against real bytes (not just the decompiled pseudocode) for the
// outer/nested wrapper layout: every sample file opened with tw01 modding
// samples starts with tag=3, an offset field pointing to byte 128, and a
// nested tag=2 block there whose own fields resolve to where the actual
// vertex/face data begins. The per-submesh vertex+triangle loop is
// transcribed from very deeply nested decompiled pointer arithmetic and
// tested against 9 real .mfa files: parses without throwing, plausible vertex
// counts, plausible bounding boxes, and even correctly found a second submesh
// in one file via FindNextUnpackTag's resync scan (the least confidently-traced
// part of the original algorithm, deliberately NOT a literal port of it) --
// not visually confirmed in Unity yet though, so axis order/signs on X/Y/Z
// are a best guess pending that.
//
// The block right after position is genuinely NOT UV, confirmed three
// independent ways: (1) raw bytes are [R,G,B,A] with A *always* exactly 0x80
// (128, the same "fully opaque" alpha sentinel the CLUT uses) across every
// vertex on every sample file -- real UV's V would need to vary across a
// texture's full range, not have its high byte pinned constant; (2) it reads
// as real, frequently-repeated RGB tuples across different vertices, exactly
// what flat-shaded triangles sharing a tint color produce; (3) HyperCrown's
// own decompiled getMfaVerticesAndFaces (in VertexGlobal, offsets 36/40 --
// the actual U/V fields) writes those fields from the *following* format=1,
// sub=1 block. Its entries are two signed int16 values scaled by 1/4096.
// The format=1/sub=1 tag is then followed by the GS texture-transfer command;
// it is not itself the transfer header. See MfaVertex.R/G/B/A for the first
// block and MfaVertex.U/V for the later, real UV block.
namespace Kh1Import
{
    public class MfaVertex
    {
        public float X, Y, Z;
        public float U, V;
        // Per-vertex tint (multiplies the CLUT-derived texture color) -- see the
        // block-formerly-known-as-UV comment in Parse() for how this was found.
        public byte R = 255, G = 255, B = 255, A = 255;
    }

    public class MfaSubmesh
    {
        public List<MfaVertex> Vertices = new List<MfaVertex>();
        public List<int[]> Faces = new List<int[]>(); // 3 indices into Vertices
        public MfaTexture Texture; // null if this submesh has none
        // GS texture slot selected by the draw. -1 means not resolved yet.
        public int TextureSlot = -1;
    }

    // Decoded from a raw PS2 GS image-transfer command sequence (GIFtag +
    // BITBLTBUF + TRXPOS + TRXREG + TRXDIR, then the pixel data itself, then --
    // for indexed formats -- a second such sequence for the CLUT). Rgba is
    // always fully-expanded RGBA32, row-major, top-to-bottom, regardless of
    // the source bit depth.
    public class MfaTexture
    {
        public int Width, Height;
        public int Dpsm; // raw PS2 GS pixel-storage-mode value this was decoded from (0=PSMCT32, 19=PSMT8, 20=PSMT4)
        public byte[] Rgba;
    }

    public class MfaModel
    {
        public List<MfaSubmesh> Submeshes = new List<MfaSubmesh>();
        public string Name;
        // Every texture transfer found in this MFA's GS command stream. This
        // is deliberately independent from Submesh.Texture: a VIF resync can
        // skip over image uploads that are not immediately adjacent to a
        // vertex strip.
        public List<MfaTexture> Textures = new List<MfaTexture>();
    }

    public static class MfaParser
    {
        private static byte U8(byte[] d, int o) => d[o];
        private static int I32(byte[] d, int o) => d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);
        private static short I16(byte[] d, int o) => (short)(d[o] | (d[o + 1] << 8));
        private static uint U32(byte[] d, int o) => (uint)I32(d, o);
        private static ulong U64(byte[] d, int o) => (ulong)U32(d, o) | ((ulong)U32(d, o + 4) << 32);
        private static float F32(byte[] d, int o) => BitConverter.ToSingle(d, o);

        public static MfaModel Parse(byte[] d, int baseOffset = 0, int length = -1)
        {
            if (baseOffset < 0 || baseOffset > d.Length)
                throw new Exception(".mfa base offset is outside the input data.");
            int fileLen = length < 0 ? d.Length - baseOffset : length;
            if (fileLen < 12 || fileLen > d.Length - baseOffset)
                throw new Exception(".mfa length is outside the input data.");
            if (I32(d, baseOffset) != 3)
                throw new Exception("Not a valid .mfa file (missing tag 3 at offset 0).");

            // Outer wrapper: [tag=3][offset][endOrLength] -- same "min(fileLen,B)-A"
            // shape confirmed for .moa's getMoaVerticesAndFaces, empirically
            // verified here too (offset consistently 128, landing exactly on a
            // nested tag=2 block).
            int a = I32(d, baseOffset + 4);
            int b = I32(d, baseOffset + 8);
            int len1 = Math.Min(fileLen, b) - a;
            if (len1 <= 0)
                throw new Exception(".mfa outer wrapper resolved to an empty block.");

            int nested = baseOffset + a;
            if (I32(d, nested) != 2)
                throw new Exception("Not a valid .mfa file (missing tag 2 in nested block).");

            // Nested tag=2 header: two (offset, length) style fields at relative
            // 16/24 and 20/28 -- the second pair (p4_1, p3_1) is the one that
            // matters: it points at the actual vertex/face region.
            int p4_1 = I32(d, nested + 24);
            int p3_1 = I32(d, nested + 28);

            int regionStart = nested + p4_1;
            int regionSize = p3_1;
            if (regionSize < 192 || fileLen < (regionStart - baseOffset) + regionSize)
                throw new Exception(".mfa vertex region failed its bounds check.");

            var model = new MfaModel();
            int cursor = 192; // relative to regionStart
            // Temporary compatibility while TEX0 selection is decoded below:
            // legacy MFA draws often omit a local upload and reuse GS state.
            MfaTexture activeTexture = null;

            while (cursor + 4 <= regionSize)
            {
                int pos = regionStart + cursor;
                int count1 = U8(d, pos + 2);
                if (count1 == 0) count1 = 256;
                byte cmd1 = U8(d, pos + 3);
                if ((cmd1 & 0x60) != 0x60 || count1 != 1) break;
                if (((cmd1 >> 2) & 3) != 3 || (cmd1 & 3) != 0) break;
                cursor += 24;

                if (cursor + 4 > regionSize) break;
                pos = regionStart + cursor;
                int vertCount = U8(d, pos + 2);
                if (vertCount == 0) vertCount = 256;
                byte cmd2 = U8(d, pos + 3);
                if ((cmd2 & 0x60) != 0x60) break;
                if (((cmd2 >> 2) & 3) != 3 || (cmd2 & 3) != 0) break;

                int vertBase = cursor + 4;
                if (regionStart + vertBase + vertCount * 16 > regionStart + regionSize) break;

                var sub = new MfaSubmesh { Texture = activeTexture };
                for (int i = 0; i < vertCount; i++)
                {
                    int vpos = regionStart + vertBase + i * 16;
                    // Source is 4 floats (X, Z, Y-ish, W); W's sign flips winding,
                    // same convention Kh1BinFormat.ProcessTriangleStrips uses.
                    // Axis order/signs here are a best guess (matched against the
                    // decompiled dest-struct offsets, not against a rendered
                    // result) -- adjust like any other importer's flip constants
                    // if geometry comes in mirrored/rotated wrong.
                    float s0 = F32(d, vpos + 0);
                    float s1 = F32(d, vpos + 4);
                    float s2 = F32(d, vpos + 8);
                    float s3 = F32(d, vpos + 12);
                    sub.Vertices.Add(new MfaVertex { X = s0, Y = s2, Z = s1 });

                    // Backward-looking (i-2,i-1,i) -- same triangle-strip convention
                    // Kh1BinFormat.ProcessTriangleStrips uses, and unlike a
                    // forward-looking (i-1,i,i+1) reading of the same decompiled
                    // logic (the original bug here: i+1 doesn't exist yet on the
                    // strip's last vertex, an out-of-bounds index Unity's
                    // SetTriangles rejected outright).
                    if (i >= 2 && s3 != 0f)
                    {
                        bool flip = s3 <= 0f;
                        int i0 = i - 2, i1 = i - 1, i2 = i;
                        sub.Faces.Add(flip ? new[] { i0, i2, i1 } : new[] { i0, i1, i2 });
                    }
                }
                cursor = vertCount * 16 + vertBase;

                if (cursor + 4 > regionSize) { model.Submeshes.Add(sub); break; }
                pos = regionStart + cursor;
                int uvCount = U8(d, pos + 2);
                if (uvCount == 0) uvCount = 256;
                byte cmd3 = U8(d, pos + 3);
                if ((cmd3 & 0x60) == 0x60 && uvCount == vertCount && ((cmd3 >> 2) & 3) == 3 && (cmd3 & 3) == 2)
                {
                    // NOT a UV block -- a previous pass assumed 2x int16 (U,V) per
                    // vertex here because the tag shape (count==vertCount, 4
                    // bytes/entry) matches that just as well, but the actual bytes
                    // are [R,G,B,A] with A *always* exactly 0x80 (128) -- the same
                    // "opaque" sentinel the CLUT's own alpha channel uses (see the
                    // `a==0?0:min(255,a*2-1)` convention below; 128*2-1=255). That's a
                    // vertex-color block: confirmed against real bytes on `di_6640`
                    // (an all-(128,128,128,128) neutral tint, matching its flat
                    // untinted feather texture) and `pc_6120` (real, varying,
                    // frequently-repeated RGB tuples across vertices -- exactly the
                    // pattern flat-shaded triangles sharing a tint color produce).
                    // The real UV block comes immediately after this one, as the
                    // following format=1/sub=1 branch decodes.
                    int colorBase = cursor + 4;
                    for (int i = 0; i < vertCount; i++)
                    {
                        int cpos = regionStart + colorBase + i * 4;
                        sub.Vertices[i].R = d[cpos + 0];
                        sub.Vertices[i].G = d[cpos + 1];
                        sub.Vertices[i].B = d[cpos + 2];
                        byte alpha = d[cpos + 3];
                        sub.Vertices[i].A = alpha == 0 ? (byte)0 : (byte)Math.Min(255, alpha * 2 - 1);
                    }
                    cursor = vertCount * 4 + colorBase;
                }

                // Texture, if this submesh has one: a raw PS2 GS image-transfer
                // command sequence (GIFtag+BITBLTBUF+TRXPOS+TRXREG+TRXDIR, pixel
                // data, and for indexed formats a second such sequence for the
                // CLUT) sitting right where a texture-tag (format=1,sub=1) would
                // be. Confirmed against real bytes: pixel count worked out to
                // *exactly* width*height for a PSMCT32 (dpsm=0) sample. Indexed
                // formats (dpsm=19/8bpp, dpsm=20/4bpp) are ported from the same
                // decompiled logic; 8-bit palette indices also need the PS2 CLUT's
                // per-32-entry middle-octet swap applied below.
                if (cursor + 4 <= regionSize)
                {
                    pos = regionStart + cursor;
                    int texCount = U8(d, pos + 2);
                    if (texCount == 0) texCount = 256;
                    byte cmd4 = U8(d, pos + 3);
                    if ((cmd4 & 0x60) == 0x60 && texCount == vertCount && ((cmd4 >> 2) & 3) == 1 && (cmd4 & 3) == 1)
                    {
                        // Exact HyperCrown behavior (getMfaVerticesAndFaces,
                        // lines 41385-41395): the payload of this VIF UNPACK is
                        // two signed 16-bit texture coordinates per vertex. The
                        // following tag (the one TryReadTexture starts at) is the
                        // GS image-transfer command.
                        int uvBase = cursor + 4;
                        if (uvBase + vertCount * 4 > regionSize) break;
                        for (int i = 0; i < vertCount; i++)
                        {
                            int uvPos = regionStart + uvBase + i * 4;
                            sub.Vertices[i].U = I16(d, uvPos) * (1f / 4096f);
                            sub.Vertices[i].V = I16(d, uvPos + 2) * (1f / 4096f);
                        }

                        int afterTexture = TryReadTexture(d, regionStart, cursor, texCount, regionSize, out MfaTexture tex);
                        if (afterTexture > 0)
                        {
                            activeTexture = tex;
                            sub.Texture = activeTexture;
                            model.Textures.Add(tex);
                            cursor = afterTexture;
                        }
                    }
                }

                model.Submeshes.Add(sub);

                // Least-confidently-traced part of the original: rather than
                // replicate its exact tag-chasing branch-by-branch, resynchronize
                // by scanning forward for the next "count==1, format-3/sub-0"
                // header tag (the same shape every submesh starts with above).
                int next = FindNextUnpackTag(d, regionStart, cursor, regionSize);
                if (next < 0) break;
                cursor = next;
            }

            // Geometry parsing intentionally resynchronizes between VIF draws.
            // That can jump across GS image transfers. HyperCrown's separate
            // getMfaTextures pass scans the whole raw MFA stream, not merely
            // the geometry region, so do the same here. This is required for
            // files such as pc_6030 that upload several texture banks.
            int scanEnd = baseOffset + fileLen;
            for (int scan = baseOffset; scan + 4 <= scanEnd; scan += 4)
            {
                int tag = scan;
                int count = U8(d, tag + 2);
                if (count == 0) count = 256;
                byte command = U8(d, tag + 3);
                if ((command & 0x60) != 0x60 || ((command >> 2) & 3) != 1 || (command & 3) != 1)
                    continue;

                int afterTexture = TryReadTexture(d, 0, scan, count, scanEnd, out MfaTexture texture);
                if (afterTexture <= 0 || texture == null) continue;

                bool duplicate = false;
                foreach (MfaTexture existing in model.Textures)
                {
                    if (existing.Width != texture.Width || existing.Height != texture.Height || existing.Rgba.Length != texture.Rgba.Length)
                        continue;
                    bool same = true;
                    for (int p = 0; p < texture.Rgba.Length; p += Math.Max(4, texture.Rgba.Length / 32))
                        if (existing.Rgba[p] != texture.Rgba[p]) { same = false; break; }
                    if (same) { duplicate = true; break; }
                }
                if (!duplicate) model.Textures.Add(texture);
            }

            // Some rooms start with a draw whose sole texture was uploaded in
            // an earlier GS packet. Preserve the existing fallback until TEX0
            // state is used for every draw.
            MfaTexture soleTexture = null;
            bool hasMultipleTextures = false;
            foreach (MfaSubmesh sub in model.Submeshes)
            {
                if (sub.Texture == null) continue;
                if (soleTexture == null) soleTexture = sub.Texture;
                else if (!ReferenceEquals(soleTexture, sub.Texture)) { hasMultipleTextures = true; break; }
            }
            if (soleTexture != null && !hasMultipleTextures)
                foreach (MfaSubmesh sub in model.Submeshes)
                    if (sub.Texture == null) sub.Texture = soleTexture;

            return model;
        }

        // Reads one texture starting at the confirmed (format=1,sub=1) tag at
        // `cursor` (relative to regionStart, `count` = that tag's own count
        // field). Returns the new cursor position (relative to regionStart)
        // just past everything consumed, or -1 if anything failed a bounds or
        // consistency check along the way (caller then just leaves this
        // submesh untextured and moves on via FindNextUnpackTag as usual).
        //
        // All the field offsets/shifts below (GIFtag EOP at bit58, NLOOP at
        // bits0-14 or NREG at bits60-63 depending on which GIFtag it is,
        // BITBLTBUF's DPSM at bits24-29, TRXREG's RRW/RRH at bits0-11/32-43)
        // are real PS2 GS register layouts, not just pattern-matched against
        // the decompiled pseudocode -- independent cross-reference against
        // known GS hardware semantics, on top of the pixel-count-matches-
        // width*height confirmation below.
        private static int TryReadTexture(byte[] d, int regionStart, int cursor, int count, int regionSize, out MfaTexture tex)
        {
            tex = null;
            try
            {
                int c = count * 4 + cursor + 4;
                if (c + 4 > regionSize || (U8(d, regionStart + c + 3) & 0x7F) != 17) return -1;

                int num16 = c + 4;
                if (num16 + 4 > regionSize) return -1;
                short num17 = I16(d, regionStart + num16);
                if ((U8(d, regionStart + num16 + 3) & 0x7F) != 80) return -1;

                int num18 = num16 + 4;
                if (num18 + 64 > regionSize) return -1; // GIFtag(16) + BITBLTBUF(16) + TRXPOS(16) + TRXREG(16)
                ulong giftag1 = U64(d, regionStart + num18);
                if ((int)((giftag1 >> 58) & 3) != 0 || (int)((giftag1 >> 60) & 0xF) != num17 - 1) return -1;

                ulong bitbltbuf = U64(d, regionStart + num18 + 16);
                int dpsm = (int)((bitbltbuf >> 24) & 0x3F);
                ulong trxreg = U64(d, regionStart + num18 + 48);
                int width = (int)(trxreg & 0xFFF);
                int height = (int)((trxreg >> 32) & 0xFFF);
                if (width <= 0 || height <= 0) return -1;

                int cursor3 = num17 * 16 + num18;
                if (cursor3 + 4 > regionSize || (U8(d, regionStart + cursor3 + 3) & 0x7F) != 80) return -1;

                if (cursor3 + 4 > regionSize) return -1;
                short num32 = I16(d, regionStart + cursor3);
                if ((U8(d, regionStart + cursor3 + 3) & 0x7F) != 80) return -1;

                int num33 = cursor3 + 4;
                if (num33 + 16 > regionSize) return -1;
                ulong giftag2 = U64(d, regionStart + num33);
                int qwordCount = (int)(giftag2 & 0x7FFF);
                if ((int)((giftag2 >> 58) & 3) != 2 || qwordCount != num32 - 1) return -1;

                int pixelDataStart = num33 + 16;
                int pixelBytes = qwordCount * 16;
                if (pixelDataStart + pixelBytes > regionSize) return -1;
                int afterPixels = pixelDataStart + pixelBytes;

                byte[] rgba;
                int outWidth = width, outHeight = height;
                if (dpsm == 0)
                {
                    // Not actually direct 32-bit color: every real sample decodes to a
                    // solid-gray/noise mess if treated that way. What's really here is a
                    // 4- or 8-bit indexed texture pre-swizzled into a PSMCT32-shaped
                    // "carrier" purely so the game could upload it over the faster 32-bit
                    // GIF path -- a documented PS2 asset-pipeline trick (see
                    // playstation2-linux.com's "Texture Swizzling" whitepaper, ezSwizzle's
                    // Figure 4: an 8-bit-to-32-bit carrier halves both width and height, a
                    // 4-bit-to-32-bit one halves width and quarters height -- so recovering
                    // the true image undoes whichever of those applies).
                    //
                    // The real CLUT (missed on the first pass -- what looked like a fixed,
                    // per-texture-identical "render state footer" right after the pixel
                    // data is actually just this reload's own constant HEADER bytes; the
                    // real, per-texture-varying color payload sits *after* that, further
                    // out than initially checked) is a second complete
                    // BITBLTBUF+TRXPOS+TRXREG+TRXDIR transfer (reloading the palette bank)
                    // immediately followed by the color-list transfer proper -- same
                    // two-tag shape as the vertex/UV/texture chain above, just one level
                    // further removed. Confirmed against real bytes: for
                    // `di_6640_sub0` this resolves to a 256-entry palette shading dark to
                    // light purple, and sampling it against a genuine higher-res color
                    // reference of the same texture (a remaster asset the user provided)
                    // lines up almost exactly (e.g. entry 8 = (60,43,88) vs a sampled
                    // reference pixel of (60,43,88)).
                    //
                    // 8-bit textures (Unswizzle8Bpp below) are confirmed clean this way
                    // against 7 real samples across 5 files. The one 4-bit sample tested
                    // (`pc_6120`'s main body texture) gets real color now instead of
                    // solid noise, but still shows a faint repeating-tile seam after
                    // UnSwizzle4Bpp -- either genuinely how that fabric-like texture
                    // looks, or this format's width/height doubling factors (guessed
                    // from ezSwizzle's Figure 4, not confirmed the way 8-bit was) are
                    // slightly off for this specific aspect ratio. Worth a second look
                    // against a 4-bit reference if one turns up.
                    int setupPos = afterPixels;
                    if (setupPos + 4 > regionSize || (U8(d, regionStart + setupPos + 3) & 0x7F) != 80) return -1;
                    short setupCount = I16(d, regionStart + setupPos);

                    int setupGiftagPos = setupPos + 4;
                    if (setupGiftagPos + 16 > regionSize) return -1;
                    ulong setupGiftag = U64(d, regionStart + setupGiftagPos);
                    if ((int)((setupGiftag >> 58) & 3) != 0) return -1; // PACKED (register reload)

                    int clutTagPos = setupGiftagPos + setupCount * 16;
                    if (clutTagPos + 4 > regionSize || (U8(d, regionStart + clutTagPos + 3) & 0x7F) != 80) return -1;
                    short colorCount = I16(d, regionStart + clutTagPos);

                    int clutGiftagPos = clutTagPos + 4;
                    if (clutGiftagPos + 16 > regionSize) return -1;
                    ulong clutGiftag = U64(d, regionStart + clutGiftagPos);
                    int clutLoop = (int)(clutGiftag & 0x7FFF);
                    if ((int)((clutGiftag >> 58) & 3) != 2 || clutLoop != colorCount - 1) return -1;

                    int clutColorStart = clutGiftagPos + 16;
                    int clutEntries = Math.Min(clutLoop * 4, 256);
                    if (clutColorStart + clutEntries * 4 > regionSize) return -1;

                    var clut = new byte[256 * 4]; // unfilled entries stay (0,0,0,0)
                    for (int i = 0; i < clutEntries; i++)
                    {
                        int p = regionStart + clutColorStart + i * 4;
                        byte a = d[p + 3];
                        clut[i * 4 + 0] = d[p]; clut[i * 4 + 1] = d[p + 1]; clut[i * 4 + 2] = d[p + 2];
                        clut[i * 4 + 3] = a == 0 ? (byte)0 : (byte)Math.Min(255, a * 2 - 1);
                    }

                    // clutEntries tells us the source bit depth: a 16-color palette means
                    // this was 4-bit, a (up to) 256-color one means 8-bit.
                    bool is4Bit = clutEntries <= 16;
                    int trueWidth = width * 2, trueHeight = is4Bit ? height * 4 : height * 2;
                    byte[] raw = new byte[pixelBytes];
                    Array.Copy(d, regionStart + pixelDataStart, raw, 0, pixelBytes);
                    byte[] indices = is4Bit
                        // A 4-bit carrier is not already a PSMT4 surface.
                        // HyperCrown first uploads it as PSMCT32, then reads the
                        // same VRAM pages as PSMT4.  The old generic 4bpp
                        // unswizzler skipped that reinterpretation, which is why
                        // pc_6120's red tiles were recognisable only as noise.
                        ? Unswizzle4ViaGsMemory(raw, width, height, trueWidth, trueHeight)
                        : Unswizzle8Bpp(raw, trueWidth, trueHeight);

                    outWidth = trueWidth;
                    outHeight = trueHeight;
                    rgba = new byte[trueWidth * trueHeight * 4];
                    for (int i = 0; i < indices.Length; i++)
                    {
                        int paletteIndex = is4Bit ? indices[i] : FixPsmT8ClutIndex(indices[i]);
                        int cp = paletteIndex * 4;
                        rgba[i * 4 + 0] = clut[cp + 0]; rgba[i * 4 + 1] = clut[cp + 1];
                        rgba[i * 4 + 2] = clut[cp + 2]; rgba[i * 4 + 3] = clut[cp + 3];
                    }

                    tex = new MfaTexture { Width = outWidth, Height = outHeight, Dpsm = dpsm, Rgba = rgba };
                    return clutColorStart + clutEntries * 4;
                }
                else if (dpsm == 19 || dpsm == 20) // PSMT8 (16 px/qword) / PSMT4 (32 px/qword): indexed, needs a CLUT
                {
                    int indexCount = dpsm == 19 ? qwordCount * 16 : qwordCount * 32;
                    if (indexCount != width * height) return -1;
                    var indices = new byte[indexCount];
                    int baseAbs = regionStart + pixelDataStart;
                    if (dpsm == 19)
                    {
                        for (int i = 0; i < indexCount; i++) indices[i] = d[baseAbs + i];
                    }
                    else
                    {
                        for (int i = 0; i < indexCount; i++)
                        {
                            byte b = d[baseAbs + (i >> 1)];
                            indices[i] = (byte)((i & 1) != 0 ? (b >> 4) & 0xF : b & 0xF);
                        }
                    }

                    // CLUT: a second, near-identical GIFtag+BITBLTBUF+... transfer
                    // immediately following the pixel data, this time carrying up
                    // to 256 RGBA color entries directly (no further indexing).
                    int c2 = afterPixels;
                    if (c2 + 4 > regionSize) return -1;
                    short clutCount = I16(d, regionStart + c2);
                    if ((U8(d, regionStart + c2 + 3) & 0x7F) != 80) return -1;

                    int clutBase = c2 + 4;
                    if (clutBase + 16 > regionSize) return -1;
                    ulong giftag3 = U64(d, regionStart + clutBase);
                    int clutLoop = (int)(giftag3 & 0x7FFF);
                    int clutFlag = (int)((giftag3 >> 58) & 3);

                    // PSMT4 uploads such as pc_6030-0 first send a PACKED
                    // five-register GS setup (BITBLTBUF/TRXPOS/TRXREG/...)
                    // and only then the IMAGE GIFtag containing 16 palette
                    // colours. The original direct path below is still used
                    // by PSMT8 streams that begin with the IMAGE GIFtag.
                    if (clutFlag == 0)
                    {
                        int imageTag = clutBase + clutCount * 16;
                        if (imageTag + 20 > regionSize || (U8(d, regionStart + imageTag + 3) & 0x7F) != 80) return -1;
                        clutCount = I16(d, regionStart + imageTag);
                        clutBase = imageTag + 4;
                        giftag3 = U64(d, regionStart + clutBase);
                        clutLoop = (int)(giftag3 & 0x7FFF);
                        clutFlag = (int)((giftag3 >> 58) & 3);
                    }
                    if (clutFlag != 2 || clutLoop != clutCount - 1) return -1;

                    int clutColorStart = clutBase + 16;
                    int clutEntries = Math.Min(clutLoop * 4, 256);
                    if (clutColorStart + clutEntries * 4 > regionSize) return -1;

                    var clut = new byte[256 * 4]; // unfilled entries stay (0,0,0,0)
                    for (int i = 0; i < clutEntries; i++)
                    {
                        int p = regionStart + clutColorStart + i * 4;
                        byte r = d[p], g = d[p + 1], bch = d[p + 2], a = d[p + 3];
                        clut[i * 4 + 0] = r; clut[i * 4 + 1] = g; clut[i * 4 + 2] = bch;
                        // Same 7-bit-alpha convention noted elsewhere, but expressed
                        // as *2-1 here per the decompiled source -- not independently
                        // re-derived, ported as-is.
                        clut[i * 4 + 3] = a == 0 ? (byte)0 : (byte)Math.Min(255, a * 2 - 1);
                    }

                    rgba = new byte[indexCount * 4];
                    for (int i = 0; i < indexCount; i++)
                    {
                        int cp = (dpsm == 19 ? FixPsmT8ClutIndex(indices[i]) : indices[i]) * 4;
                        rgba[i * 4 + 0] = clut[cp + 0]; rgba[i * 4 + 1] = clut[cp + 1];
                        rgba[i * 4 + 2] = clut[cp + 2]; rgba[i * 4 + 3] = clut[cp + 3];
                    }

                    // Deliberately not tracking the trailing GS render-state setup
                    // that follows the CLUT (TEX0/TEXA-style registers) -- harmless
                    // to leave uncounted since FindNextUnpackTag resyncs forward
                    // from wherever this cursor ends up anyway.
                    afterPixels = clutColorStart + clutEntries * 4;
                }
                else
                {
                    return -1; // unsupported/unrecognized pixel format
                }

                tex = new MfaTexture { Width = outWidth, Height = outHeight, Dpsm = dpsm, Rgba = rgba };
                return afterPixels;
            }
            catch
            {
                return -1;
            }
        }

        private static int FixPsmT8ClutIndex(int index)
        {
            // PSMT8 CLUT order swaps entries 8-15 with 16-23 in every 32-entry block.
            int low = index & 31;
            if (low >= 8 && low < 16) return index + 8;
            if (low >= 16 && low < 24) return index - 8;
            return index;
        }

        // PS2 GS 8bpp (PSMT8) block/column de-swizzle. `width`/`height` are the
        // TRUE image dimensions (not the halved PSMCT32-carrier ones). Verbatim
        // algorithm from Surihix/PS2ImageTool's Unswizzlers.Pixel8Bpp (a released,
        // working PS2 texture extraction tool) -- not independently re-derived,
        // ported as-is since it's the one piece of this whole texture path
        // confirmed correct by eye against real samples rather than guessed.
        private static byte[] Unswizzle8Bpp(byte[] pixelArray, int width, int height)
        {
            byte[] unswizzled = new byte[pixelArray.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int blockLocation = (y & -16) * width + (x & -16) * 2;
                    int swapSelector = (((y + 2) >> 2) & 1) * 4;
                    int posY = (((y & ~3) >> 1) + (y & 1)) & 7;
                    int columnLocation = posY * width * 2 + ((x + swapSelector) & 7) * 4;
                    int byteNum = ((y >> 1) & 1) + ((x >> 2) & 2);
                    unswizzled[y * width + x] = pixelArray[blockLocation + columnLocation + byteNum];
                }
            }
            return unswizzled;
        }

        // Exact two-stage route used by HyperCrown's getMfaTextures:
        // writeTexPSMCT32() followed by readTexPSMT8(). MFA stores 8-bit
        // indices as a 32-bit upload carrier, so treating its bytes as a
        // pre-existing PSMT8 surface (the older Unswizzle8Bpp shortcut) loses
        // page/column addressing on some assets, notably pc04's red tiles.
        private static byte[] Unswizzle8ViaGsMemory(byte[] source, int carrierWidth, int carrierHeight, int width, int height)
        {
            int[] block32 = { 0,1,4,5,16,17,20,21, 2,3,6,7,18,19,22,23, 8,9,12,13,24,25,28,29, 10,11,14,15,26,27,30,31 };
            int[] columnWord32 = { 0,1,4,5,8,9,12,13, 2,3,6,7,10,11,14,15 };
            int[] block8 = block32;
            int[,] columnWord8 = {
                { 0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13, 2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15 },
                { 8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5, 10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7 }
            };
            int[] columnByte8 = { 0,0,0,0,0,0,0,0,2,2,2,2,2,2,2,2,0,0,0,0,0,0,0,0,2,2,2,2,2,2,2,2, 1,1,1,1,1,1,1,1,3,3,3,3,3,3,3,3,1,1,1,1,1,1,1,1,3,3,3,3,3,3,3,3 };
            byte[] gs = new byte[4 * 1024 * 1024];
            int sourceOffset = 0, carrierDbw = Math.Max(1, carrierWidth / 64);
            for (int y = 0; y < carrierHeight; y++) for (int x = 0; x < carrierWidth; x++)
            {
                int page = x / 64 + (y / 32) * carrierDbw, px = x & 63, py = y & 31;
                int block = block32[px / 8 + (py / 8) * 8], column = (py & 7) / 2;
                int word = columnWord32[(px & 7) + ((py & 1) * 8)];
                int offset = (page * 2048 + block * 64 + column * 16 + word) * 4;
                if (sourceOffset + 4 <= source.Length && offset + 4 <= gs.Length) Buffer.BlockCopy(source, sourceOffset, gs, offset, 4);
                sourceOffset += 4;
            }
            byte[] result = new byte[width * height];
            int dbw = Math.Max(1, width / 64) >> 1, dst = 0;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int page = x / 128 + (y / 64) * dbw, px = x & 127, py = y & 63;
                int block = block8[px / 16 + (py / 16) * 8], column = (py & 15) / 4;
                int entry = (px & 15) + ((py & 3) * 16);
                // columnWord8 repeats its 32-word pattern for the second
                // half of each four-line column; columnByte8 carries the
                // byte-within-word distinction for all 64 texels.
                int word = columnWord8[column & 1, entry & 31], b = columnByte8[entry];
                int offset = (page * 2048 + block * 64 + column * 16 + word) * 4 + b;
                result[dst++] = offset < gs.Length ? gs[offset] : (byte)0;
            }
            return result;
        }

        // Port of HyperCrown's writeTexPSMCT32 + readTexPSMT4 path. MFA's
        // four-bit textures are uploaded through a 32-bit carrier whose VRAM
        // layout must be reinterpreted; direct nibble de-swizzling is wrong.
        private static byte[] Unswizzle4ViaGsMemory(byte[] source, int carrierWidth, int carrierHeight, int width, int height)
        {
            int[] block32 = { 0,1,4,5,16,17,20,21, 2,3,6,7,18,19,22,23, 8,9,12,13,24,25,28,29, 10,11,14,15,26,27,30,31 };
            int[] columnWord32 = { 0,1,4,5,8,9,12,13, 2,3,6,7,10,11,14,15 };
            int[] block4 = { 0,2,8,10, 1,3,9,11, 4,6,12,14, 5,7,13,15, 16,18,24,26, 17,19,25,27, 20,22,28,30, 21,23,29,31 };
            int[,] columnWord4 = {
                { 0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13, 2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15, 8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5, 10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7 },
                { 8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5, 10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7, 0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13,0,1,4,5,8,9,12,13, 2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15,2,3,6,7,10,11,14,15 }
            };
            int[] columnByte4 = { 0,0,0,0,0,0,0,0,2,2,2,2,2,2,2,2,4,4,4,4,4,4,4,4,6,6,6,6,6,6,6,6, 0,0,0,0,0,0,0,0,2,2,2,2,2,2,2,2,4,4,4,4,4,4,4,4,6,6,6,6,6,6,6,6, 1,1,1,1,1,1,1,1,3,3,3,3,3,3,3,3,5,5,5,5,5,5,5,5,7,7,7,7,7,7,7,7, 1,1,1,1,1,1,1,1,3,3,3,3,3,3,3,3,5,5,5,5,5,5,5,5,7,7,7,7,7,7,7,7 };
            byte[] gs = new byte[4 * 1024 * 1024];
            int carrierDbw = Math.Max(1, carrierWidth / 64), sourceOffset = 0;
            for (int y = 0; y < carrierHeight; y++) for (int x = 0; x < carrierWidth; x++)
            {
                int page = x / 64 + (y / 32) * carrierDbw, px = x & 63, py = y & 31;
                int block = block32[px / 8 + (py / 8) * 8], column = (py & 7) / 2;
                int word = columnWord32[(px & 7) + ((py & 1) * 8)];
                int offset = (page * 2048 + block * 64 + column * 16 + word) * 4;
                if (sourceOffset + 4 <= source.Length && offset + 4 <= gs.Length) Buffer.BlockCopy(source, sourceOffset, gs, offset, 4);
                sourceOffset += 4;
            }

            var result = new byte[width * height];
            int dbw = Math.Max(1, width / 64) >> 1, dst = 0;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int page = x / 128 + (y / 128) * dbw, px = x & 127, py = y & 127;
                int block = block4[px / 32 + (py / 16) * 4], column = (py & 15) / 4;
                int entry = (px & 31) + ((py & 3) * 32);
                int word = columnWord4[column & 1, entry], byteOffset = columnByte4[entry];
                int offset = (page * 2048 + block * 64 + column * 16 + word) * 4 + (byteOffset >> 1);
                byte value = offset < gs.Length ? gs[offset] : (byte)0;
                // readTexPSMT4 selects the source nibble from CB's low bit.
                // Its `odd` flag only chooses the destination nibble while
                // packing two texels into a byte; here every result byte is an
                // already-unpacked index, so using pixel parity was incorrect.
                result[dst++] = (byte)((byteOffset & 1) != 0 ? (value >> 4) & 0xF : value & 0xF);
            }
            return result;
        }

        // PS2 GS 4bpp (PSMT4) block/column de-swizzle. `width`/`height` are the
        // TRUE image dimensions. Verbatim algorithm (credited there to Dageron /
        // L33TMasterJacob) from https://gist.github.com/Fireboyd78/1546f5c86ebce52ce05e7837c697dc72
        // -- same reasoning as Unswizzle8Bpp for porting as-is rather than
        // re-deriving. Returns one nibble value (0-15) per output byte.
        private static byte[] UnSwizzle4Bpp(byte[] buffer, int width, int height)
        {
            byte[] InterlaceMatrix = { 0x00, 0x10, 0x02, 0x12, 0x11, 0x01, 0x13, 0x03 };
            int[] Matrix = { 0, 1, -1, 0 };
            int[] TileMatrix = { 4, -4 };

            var pixels = new byte[width * height];
            var newPixels = new byte[width * height];
            var d = 0;
            var s = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < (width >> 1); x++)
                {
                    var p = buffer[s++];
                    pixels[d++] = (byte)(p & 0xF);
                    pixels[d++] = (byte)(p >> 4);
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var oddRow = (y & 1) != 0;
                    var num1 = (byte)((y / 4) & 1);
                    var num2 = (byte)((x / 4) & 1);
                    var num3 = y % 4;
                    var num4 = (x / 4) % 4;
                    if (oddRow) num4 += 4;

                    var num5 = (x * 4) % 16;
                    var num6 = (x / 16) * 32;
                    var num7 = oddRow ? (y - 1) * width : y * width;
                    var xx = x + num1 * TileMatrix[num2];
                    var yy = y + Matrix[num3];
                    var i = InterlaceMatrix[num4] + num5 + num6 + num7;
                    var j = yy * width + xx;

                    newPixels[j] = pixels[i];
                }
            }

            return newPixels;
        }

        // Scans forward from `from` (relative to regionStart) for the next byte
        // position that looks like a submesh's opening VIFcode-style tag (matches
        // the same test the main loop uses to recognize one). Returns -1 if none
        // found before regionSize.
        private static int FindNextUnpackTag(byte[] d, int regionStart, int from, int regionSize)
        {
            for (int cursor = from; cursor + 24 + 4 <= regionSize; cursor += 4)
            {
                if (!IsDrawStart(d, regionStart, cursor, regionSize)) continue;
                return cursor;
            }
            return -1;
        }

        // getMfaVerticesAndFaces does not resync on an isolated VIF command.
        // A genuine draw starts with its 1-qword setup, then a position UNPACK,
        // a colour UNPACK and an equal-length UV UNPACK. Checking that complete
        // chain prevents byte patterns inside texture data from being mistaken
        // for a following mesh.
        private static bool IsDrawStart(byte[] d, int regionStart, int cursor, int regionSize)
        {
            int end = regionStart + regionSize;
            int p = regionStart + cursor;
            if (p + 28 > end) return false;
            int setupCount = U8(d, p + 2); if (setupCount == 0) setupCount = 256;
            byte setup = U8(d, p + 3);
            if ((setup & 0x60) != 0x60 || setupCount != 1 || ((setup >> 2) & 3) != 3 || (setup & 3) != 0) return false;

            p += 24;
            int vertexCount = U8(d, p + 2); if (vertexCount == 0) vertexCount = 256;
            byte vertices = U8(d, p + 3);
            if ((vertices & 0x60) != 0x60 || ((vertices >> 2) & 3) != 3 || (vertices & 3) != 0) return false;

            p += 4 + vertexCount * 16;
            if (p + 4 > end) return false;
            int colorCount = U8(d, p + 2); if (colorCount == 0) colorCount = 256;
            byte colors = U8(d, p + 3);
            if ((colors & 0x60) != 0x60 || colorCount != vertexCount || ((colors >> 2) & 3) != 3 || (colors & 3) != 2) return false;

            p += 4 + vertexCount * 4;
            if (p + 4 > end) return false;
            int uvCount = U8(d, p + 2); if (uvCount == 0) uvCount = 256;
            byte uvs = U8(d, p + 3);
            return (uvs & 0x60) == 0x60 && uvCount == vertexCount && ((uvs >> 2) & 3) == 1 && (uvs & 3) == 1;
        }
    }
}
#endif
