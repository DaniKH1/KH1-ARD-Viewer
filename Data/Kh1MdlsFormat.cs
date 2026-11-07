using System;
using System.Collections.Generic;

// Reader for Kingdom Hearts 1's ".mdls" character/object/weapon model format
// ("MOBJ" magic). Ported from OpenKh.Kh1's Mdls.cs (the only existing
// implementation) into the same dependency-free, manual-byte-offset style as
// Kh1BinFormat.cs, instead of pulling in OpenKh.Common/Xe.BinaryMapper.
//
// Files extracted straight out of a KH1 .ard's resource-list blob (e.g. via
// the "ARD_Extractor" tool) are valid standalone .mdls files, so this reader
// doesn't need to know anything about .ard containers itself.
//
// Single-weight skinning only -- multiweight vertices (mesh sub-packet type
// 2) throw, same as upstream (never implemented there either). .moa/.mfa are
// a different, undocumented format and aren't handled here.
namespace Kh1Import
{
    public class MdlsJoint
    {
        public float ScaleX, ScaleY, ScaleZ;
        public float RotateX, RotateY, RotateZ;
        public float TranslateX, TranslateY, TranslateZ;
        public uint HierarchyBitfield;
        // Low 10 bits of HierarchyBitfield; upstream's own comment admits the
        // sibling/child bits packed into the rest of the field were never
        // decoded ("not smart enough to correctly read this") -- only parent
        // is used here too, same as upstream.
        public uint ParentId => HierarchyBitfield & 0x3FF;
    }

    public class MdlsVertex
    {
        public float NormalX, NormalY, NormalZ;
        public float TranslateX, TranslateY, TranslateZ;
        public float TexCoordU, TexCoordV;
        public int JointId;
    }

    public class MdlsMesh
    {
        public int TextureIndex;
        public List<MdlsVertex> Vertices = new List<MdlsVertex>();
        public List<int[]> Faces = new List<int[]>(); // 3 indices into Vertices, winding already resolved
    }

    public class MdlsImage
    {
        public int Width, Height;
        public byte[] Rgba = Array.Empty<byte>(); // decoded RGBA32, row 0 = top (same convention as Kh1TextureBlock.Pixels)
    }

    public class MdlsModel
    {
        public List<MdlsJoint> Joints = new List<MdlsJoint>();
        public List<MdlsMesh> Meshes = new List<MdlsMesh>();
        public List<MdlsImage> Images = new List<MdlsImage>();
    }

    public static class MdlsParser
    {
        private const int HeaderStart = 0x80;
        private const int MagicCode = 0x4A424F4D; // "MOBJ"
        private const int ClutSize = 256 * 4;

        private static byte U8(byte[] d, int o) => d[o];
        private static int I32(byte[] d, int o) => d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);
        private static uint U32(byte[] d, int o) => (uint)I32(d, o);
        private static float F32(byte[] d, int o) => BitConverter.ToSingle(d, o);

        // baseOffset: where this model's own byte 0 sits within `d` -- 0 for a
        // standalone .mdls file, or a sub-entry's start offset when parsing one
        // out of a .ard container's nested model table (see Kh1ArdFormat.cs).
        // Every field offset in the format is relative to baseOffset+InitialAddress,
        // so a single change here is enough to support both cases.
        public static MdlsModel Parse(byte[] d, int baseOffset = 0)
        {
            int InitialAddress = baseOffset + HeaderStart;

            if (I32(d, InitialAddress) != MagicCode)
                throw new Exception("Not a valid .mdls file (missing MOBJ magic at 0x80).");

            // MdlsHeader, 10 int32 fields + 4 reserved ints, starting right after
            // the magic (4 bytes) + dataSize (4 bytes) at InitialAddress+8.
            int hdrOffs = InitialAddress + 8;
            int textureInfoOffset = I32(d, hdrOffs + 0x00);
            int textureInfoSize = I32(d, hdrOffs + 0x04);
            int textureDataOffset = I32(d, hdrOffs + 0x08);
            int modelOffset = I32(d, hdrOffs + 0x18);
            int unkOffset = I32(d, hdrOffs + 0x20); // marks the end of the last mesh's packet data

            var model = new MdlsModel();

            // MdlsModelHeader (16 bytes): JointCount, JointInfoOffset, BoneDataOffset(unused), MeshCount
            int modelHdrOffs = InitialAddress + modelOffset;
            int jointCount = I32(d, modelHdrOffs + 0x00);
            int jointInfoOffset = I32(d, modelHdrOffs + 0x04);
            int meshCount = I32(d, modelHdrOffs + 0x0C);

            // MdlsMeshHeader (16 bytes each): JointCount(unused), TextureIndex, TextureIndex2(unused), MeshPacketOffset
            int meshHeaderOffs = modelHdrOffs + 0x10;
            var meshPacketOffsets = new int[meshCount];
            var meshTextureIndex = new int[meshCount];
            for (int i = 0; i < meshCount; i++)
            {
                int o = meshHeaderOffs + i * 16;
                meshTextureIndex[i] = I32(d, o + 0x4);
                meshPacketOffsets[i] = I32(d, o + 0xC);
            }

            // MdlsJoint (48 bytes each): Scale xyz, Index(unused), Rotate xyz, Padding,
            // Translate xyz, HierarchyBitfield.
            int jointOffs = InitialAddress + modelOffset + jointInfoOffset;
            for (int i = 0; i < jointCount; i++)
            {
                int o = jointOffs + i * 48;
                model.Joints.Add(new MdlsJoint
                {
                    ScaleX = F32(d, o + 0x00),
                    ScaleY = F32(d, o + 0x04),
                    ScaleZ = F32(d, o + 0x08),
                    RotateX = F32(d, o + 0x10),
                    RotateY = F32(d, o + 0x14),
                    RotateZ = F32(d, o + 0x18),
                    TranslateX = F32(d, o + 0x20),
                    TranslateY = F32(d, o + 0x24),
                    TranslateZ = F32(d, o + 0x28),
                    HierarchyBitfield = U32(d, o + 0x2C),
                });
            }

            // Mesh packets: each mesh's data runs from its own MeshPacketOffset up to
            // the next mesh's (or Header.UnkOffset for the last one).
            for (int i = 0; i < meshCount; i++)
            {
                int meshDataOffset = InitialAddress + modelOffset + meshPacketOffsets[i];
                int nextOffset = (i + 1 < meshCount)
                    ? InitialAddress + modelOffset + meshPacketOffsets[i + 1]
                    : InitialAddress + unkOffset;

                var mesh = new MdlsMesh { TextureIndex = meshTextureIndex[i] };
                UnpackMeshPacket(d, meshDataOffset, nextOffset - meshDataOffset, mesh);
                model.Meshes.Add(mesh);
            }

            // Textures: imageCount MdlsImageInfo headers (16 bytes each), then that
            // many index-data blocks (Width*Height bytes each, one byte per texel),
            // then that many 1024-byte CLUTs -- all three sections are read back to
            // back with a running cursor, not fixed per-image offsets, since block
            // sizes vary by image.
            int imageCount = textureInfoSize / 0x10;
            var widths = new int[imageCount];
            var heights = new int[imageCount];
            for (int i = 0; i < imageCount; i++)
            {
                int o = InitialAddress + textureInfoOffset + i * 0x10;
                widths[i] = U16(d, o + 0x4);
                heights[i] = U16(d, o + 0x6);
            }

            int cursor = InitialAddress + textureDataOffset;
            var indexData = new byte[imageCount][];
            for (int i = 0; i < imageCount; i++)
            {
                int len = widths[i] * heights[i];
                indexData[i] = new byte[len];
                Array.Copy(d, cursor, indexData[i], 0, len);
                cursor += len;
            }
            for (int i = 0; i < imageCount; i++)
            {
                byte[] clut = new byte[ClutSize];
                Array.Copy(d, cursor, clut, 0, ClutSize);
                cursor += ClutSize;

                model.Images.Add(new MdlsImage
                {
                    Width = widths[i],
                    Height = heights[i],
                    Rgba = DecodeImage(indexData[i], clut, widths[i], heights[i]),
                });
            }

            return model;
        }

        // The true end-of-content offset for a model at `baseOffset` -- i.e. one
        // past the last CLUT byte. Only needed when slicing a byte-exact
        // standalone .mdls out of a larger buffer (e.g. a .ard's padded
        // sub-entry slot, see Kh1ArdFormat.cs's header); Parse() itself never
        // needs this since it only ever reads exactly what its own header
        // fields say to.
        public static int ComputeLength(byte[] d, int baseOffset = 0)
        {
            int InitialAddress = baseOffset + HeaderStart;
            if (I32(d, InitialAddress) != MagicCode)
                throw new Exception("Not a valid .mdls file (missing MOBJ magic at 0x80).");

            int hdrOffs = InitialAddress + 8;
            int textureInfoOffset = I32(d, hdrOffs + 0x00);
            int textureInfoSize = I32(d, hdrOffs + 0x04);
            int textureDataOffset = I32(d, hdrOffs + 0x08);

            int imageCount = textureInfoSize / 0x10;
            int cursor = InitialAddress + textureDataOffset;
            for (int i = 0; i < imageCount; i++)
            {
                int o = InitialAddress + textureInfoOffset + i * 0x10;
                int width = U16(d, o + 0x4), height = U16(d, o + 0x6);
                cursor += width * height;
            }
            cursor += imageCount * ClutSize;

            // Empirically, ARD_Extractor.exe's own output always runs exactly
            // 0x80 bytes past the last CLUT byte -- confirmed on all 23 models
            // in the tw01.ard test set, no exceptions, regardless of size. Some
            // fixed trailer/footer this reader doesn't otherwise touch, cause
            // unconfirmed (nothing in Parse() ever reads past the last CLUT, and
            // that's sufficient for every field this reader exposes -- this
            // constant only matters for reproducing ARD_Extractor.exe's exact
            // byte-for-byte cut point).
            cursor += 0x80;

            return cursor - baseOffset;
        }

        private static ushort U16(byte[] d, int o) => (ushort)(d[o] | (d[o + 1] << 8));

        // PS2 8bpp indexed textures store their CLUT in a hardware-swizzled order
        // where each block of 32 palette entries has entries [8,16) and [16,24)
        // swapped -- the exact same quirk Kh1TextureBlock.FillFromTexture corrects
        // for (there expressed as a bitmask flip of bits 3/4; here as the
        // equivalent range check, ported straight from upstream's IndexFix).
        private static byte IndexFix(byte pixelIndex)
        {
            int low5 = pixelIndex & 31;
            if (low5 >= 8 && low5 < 16) pixelIndex += 8;
            else if (low5 >= 16 && low5 < 24) pixelIndex -= 8;
            return pixelIndex;
        }

        private static byte[] DecodeImage(byte[] indexData, byte[] clut, int width, int height)
        {
            var rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                byte index = IndexFix(indexData[i]);
                int c = index * 4;
                int p = i * 4;
                rgba[p + 0] = clut[c + 0];
                rgba[p + 1] = clut[c + 1];
                rgba[p + 2] = clut[c + 2];
                // Same 7-bit-alpha-doubling convention as Kh1TextureBlock.FillFromTexture.
                rgba[p + 3] = (byte)Math.Min(255, clut[c + 3] * 2);
            }
            return rgba;
        }

        private static void UnpackMeshPacket(byte[] d, int offs, int length, MdlsMesh mesh)
        {
            int end = offs + length;
            int pos = offs;
            var weightMatrix = new int[16];

            while (pos < end)
            {
                // MdlsMeshSubPacketHeader, 16 bytes -- only its size (in the overall
                // loop, implicitly) matters here; none of its fields are used.
                pos += 16;

                while (I32(d, pos) != 0x00008000)
                {
                    int subPacketType = I32(d, pos);
                    if (subPacketType == 0)
                    {
                        // MdlsPacketMatrixPointer (16 bytes): PacketType, JointId, TableIndex0, TableIndex1
                        int jointId = I32(d, pos + 4);
                        int tableIndex0 = I32(d, pos + 8);
                        int tableIndex1 = I32(d, pos + 12);
                        pos += 16;
                        if (tableIndex1 == 0)
                        {
                            weightMatrix[tableIndex0] = jointId;
                            pos += 0x70; // reserved, all zero
                        }
                        // else: multiweight table entry -- not implemented upstream either;
                        // the following 0x70 reserved bytes are only skipped in the
                        // single-weight case above, matching upstream's own gap.
                    }
                    else if (subPacketType == 1)
                    {
                        // MdlsPacketVertices header (32 bytes): PacketType, DataSize,
                        // VertexCount, Unknown(winding), VertexCount2, Unknown2-4.
                        int vertexCount = I32(d, pos + 8);
                        int winding = I32(d, pos + 12);
                        pos += 32;

                        int stripCount = 0;
                        for (int i = 0; i < vertexCount; i++)
                        {
                            var vertex = new MdlsVertex
                            {
                                NormalX = F32(d, pos + 0x00),
                                NormalY = F32(d, pos + 0x04),
                                NormalZ = F32(d, pos + 0x08),
                                TranslateX = F32(d, pos + 0x10),
                                TranslateY = F32(d, pos + 0x14),
                                TranslateZ = F32(d, pos + 0x18),
                                TexCoordU = F32(d, pos + 0x20),
                                TexCoordV = F32(d, pos + 0x24),
                            };
                            uint matrixId = U32(d, pos + 0x0C);
                            vertex.JointId = weightMatrix[matrixId];
                            pos += 48;

                            int vertexIndex = mesh.Vertices.Count;
                            mesh.Vertices.Add(vertex);
                            stripCount++;

                            if (stripCount >= 3)
                            {
                                // Alternating triangle-strip winding, direction set by the
                                // strip's own Unknown/winding flag -- mirrors upstream exactly.
                                bool evenParity = stripCount % 2 == 0;
                                bool ccw = winding == 0;
                                if (ccw == evenParity)
                                    mesh.Faces.Add(new[] { vertexIndex, vertexIndex - 1, vertexIndex - 2 });
                                else
                                    mesh.Faces.Add(new[] { vertexIndex, vertexIndex - 2, vertexIndex - 1 });
                            }
                        }
                    }
                    else
                    {
                        throw new Exception($"Unknown or unimplemented mdls mesh sub-packet type {subPacketType} at 0x{pos:X}.");
                    }
                }
                pos += 0x20;
            }
        }
    }
}
