﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using WindViewer.Editor;
using WindViewer.Editor.Renderer;

namespace WindViewer.FileFormats
{
    public class JStudioModel : BaseArchiveFile
    {
        public enum ArrayTypes
        {
            PositionMatrixIndex, Tex0MatrixIndex, Tex1MatrixIndex, Tex2MatrixIndex, Tex3MatrixIndex,
            Tex4MatrixIndex, Tex5MatrixIndex, Tex6MatrixIndex, Tex7MatrixIndex,
            Position, Normal, Color0, Color1, Tex0, Tex1, Tex2, Tex3, Tex4, Tex5, Tex6, Tex7,
            PositionMatrixArray, NormalMatrixArray, TextureMatrixArray, LitMatrixArray, NormalBinormalTangent,
            MaxAttr, NullAttr = 0xFF,
        }

        public enum DataTypes
        {
            Unsigned8 = 0x0,
            Signed8 = 0x1,
            Unsigned16 = 0x2,
            Signed16 = 0x3,
            Float32 = 0x4,
            RGBA8 = 0x5,
        }

        public enum TextureFormats
        {
            RGB565 = 0x0,
            RGB888 = 0x1,
            RGBX8 = 0x2,
            RGBA4 = 0x3,
            RGBA6 = 0x4,
            RGBA8 = 0x5,
        }

        public enum PrimitiveTypes
        {
            Points = 0xB8,
            Lines = 0xA8,
            LineStrip = 0xB0,
            Triangles = 0x80,
            TriangleStrip = 0x98,
            TriangleFan = 0xA0,
            Quads = 0x80,
        }

        //Temp in case I fuck up
        private byte[] _origDataCache;

        private List<BaseChunk> _chunkList;

        public override void Load(byte[] data)
        {
            _origDataCache = data;
            _chunkList = new List<BaseChunk>();

            int dataOffset = 0;

            Header header = new Header();
            header.Load(data, ref dataOffset);

            //STEP 1: We're going to load all of the data out of memory straight into the chunks that
            //hold them. These should be relatively close to accurate copies of the file format, but
            //the Wiki is probably a better place to draw that information from.
            for (int i = 0; i < header.ChunkCount; i++)
            {
                BaseChunk baseChunk = null;

                //Read the first four bytes to get the tag.
                string tagName = FSHelpers.ReadString(data, dataOffset, 4);

                switch (tagName)
                {
                    case "INF1":
                        baseChunk = new Inf1Chunk();
                        break;
                    case "VTX1":
                        baseChunk = new Vtx1Chunk();
                        break;
                    case "EVP1":
                        baseChunk = new Evp1Chunk();
                        break;
                    case "DRW1":
                        baseChunk = new Drw1Chunk();
                        break;
                    case "JNT1":
                        baseChunk = new Jnt1Chunk();
                        break;
                    case "SHP1":
                        baseChunk = new Shp1Chunk();
                        break;
                    case "TEX1":
                    case "MAT3":
                    case "ANK1":
                    default:
                        Console.WriteLine("Found unknown chunk {0}!", tagName);
                        baseChunk = new BaseChunk();
                        break;
                }

                baseChunk.Load(data, ref dataOffset);
                _chunkList.Add(baseChunk);
            }

            //STEP 2: Once all of the data is loaded, we're going to pull different data from
            //different chunks to transform the data into something
            var vertData = BuildVertexArraysFromFile(data);


            //Haaaaaaaack there goes my lung. Generate a vbo, bind and upload data.
            GL.GenBuffers(1, out _glVbo);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _glVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vertData.Count * 32), vertData.ToArray(), BufferUsageHint.StaticDraw);

            J3DRenderer.Draw += J3DRendererOnDraw;
            J3DRenderer.Bind += J3DRendererOnBind;
        }

        private struct PrimitiveList
        {
            public int VertexStart;
            public int VertexCount;
        }

        private List<PrimitiveList> _renderList = new List<PrimitiveList>();

        private void J3DRendererOnBind()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, _glVbo);

        }

        private void J3DRendererOnDraw()
        {
            //GL.BindBuffer(BufferTarget.ArrayBuffer, _glVbo);
            //GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 10);

            foreach (PrimitiveList primitive in _renderList)
            {
                GL.DrawArrays(PrimitiveType.TriangleStrip, primitive.VertexStart, primitive.VertexCount);
            }
        }

        private int _glVbo;

        public T GetChunkByType<T>() where T : class
        {
            foreach (BaseChunk file in _chunkList)
            {
                if (file is T)
                    return file as T;
            }

            return default(T);
        }

        public struct VertexFormatLayout
        {
            public Vector3 Position;
            public Vector4 Color;
            public Vector2 TexCoord;
        }


        private List<VertexFormatLayout> BuildVertexArraysFromFile(byte[] data)
        {
            List<Vector3> vertList = new List<Vector3>();
            List<Vector4> colorList = new List<Vector4>();
            List<Vector2> tcList = new List<Vector2>();

            Vtx1Chunk vtxChunk = GetChunkByType<Vtx1Chunk>();

            foreach (Vtx1Chunk.VertexFormat format in vtxChunk.VertexFormats)
            {
                if (format.ArrayType == ArrayTypes.NullAttr)
                    continue;

                uint entryCount = format.ArrayCount;
                uint dataLength = 0;
                float[] formatData = null;

                //Massive hack incoming... we're just going to read way behond what we should read
                //because it's all indexed and fuck it I'm tired.

                switch (format.DataType)
                {
                    //Convert fixed precision to float
                    case DataTypes.Signed16:
                        dataLength = (uint)(vtxChunk.ChunkSize - vtxChunk.VertexDataOffsets[((int)Vtx1Chunk.VertexDataTypes.Tex0)]) / 2;
                        formatData = new float[dataLength];

                        float scale = (float)Math.Pow(0.5, format.DecimalPoint);
                        for (int i = 0; i < dataLength; i++)
                        {
                            formatData[i] =
                                FSHelpers.Read16(vtxChunk._dataCopy,
                                    vtxChunk.VertexDataOffsets[((int)Vtx1Chunk.VertexDataTypes.Tex0)] + (i * 2)) * scale;
                        }
                        break;

                    case DataTypes.Float32:
                        dataLength = (uint)(vtxChunk.ChunkSize - vtxChunk.VertexDataOffsets[((int)Vtx1Chunk.VertexDataTypes.Position)]) / 4;
                        formatData = new float[dataLength];

                        for (int i = 0; i < dataLength; i++)
                        {
                            formatData[i] = FSHelpers.ReadFloat(vtxChunk._dataCopy,
                                vtxChunk.VertexDataOffsets[((int)Vtx1Chunk.VertexDataTypes.Position)] + (i * 4));
                        }
                        break;

                    case DataTypes.RGBA8:
                        dataLength = (uint)(vtxChunk.ChunkSize - vtxChunk.VertexDataOffsets[((int)Vtx1Chunk.VertexDataTypes.Color0)]);
                        formatData = new float[dataLength];

                        for (int i = 0; i < dataLength; i++)
                        {
                            formatData[i] =
                                FSHelpers.Read8(vtxChunk._dataCopy,
                                    vtxChunk.VertexDataOffsets[((int)Vtx1Chunk.VertexDataTypes.Color0)] + i) / 255f;
                        }
                        break;
                }


                //Now that we've pulled the data and converted it to floats, we're going to turn it into something useful.
                switch (format.ArrayType)
                {
                    case ArrayTypes.Position:
                        for (int i = 0, j = 0; i < (formatData.Length / 3); i++, j += 3)
                            vertList.Add(new Vector3(formatData[j], formatData[j + 1], formatData[j + 2]));
                        break;

                    case ArrayTypes.Color0:
                        for (int i = 0, j = 0; i < (formatData.Length / 4); i++, j += 4)
                            colorList.Add(new Vector4(formatData[j], formatData[j + 1], formatData[j + 2], formatData[j + 3]));
                        break;

                    case ArrayTypes.Tex0:
                        for (int i = 0, j = 0; i < (formatData.Length / 2); i++, j += 2)
                            tcList.Add(new Vector2(formatData[j], formatData[j + 1]));
                        break;
                }

                Console.WriteLine("BLURRGRGH.");

            }

            List<VertexFormatLayout> finalData = new List<VertexFormatLayout>();
            Shp1Chunk shp1Chunk = GetChunkByType<Shp1Chunk>();


            Shp1Chunk.Batch[] batches = new Shp1Chunk.Batch[shp1Chunk.SectionCount];
            Shp1Chunk.BatchPacketLocation[] packetLocations = new Shp1Chunk.BatchPacketLocation[shp1Chunk.SectionCount];

            //Read the batches
            int batchDataOffset = (int)shp1Chunk.BatchOffset;
            for (int i = 0; i < batches.Length; i++)
            {
                batches[i] = new Shp1Chunk.Batch();
                batches[i].Load(shp1Chunk._dataCopy, ref batchDataOffset);
            }

            //Read the Packet Locations
            int packetDataOffset = (int) shp1Chunk.PacketLocationOffset;
            for (int i = 0; i < packetLocations.Length; i++)
            {
                packetLocations[i] = new Shp1Chunk.BatchPacketLocation();
                packetLocations[i].Size = (uint)FSHelpers.Read32(shp1Chunk._dataCopy, packetDataOffset + 0x0);
                packetLocations[i].Offset = (uint)FSHelpers.Read32(shp1Chunk._dataCopy, packetDataOffset + 0x4);
                packetDataOffset += 0x8;
            }

            //Now, let's try to get our data.
            for (int i = 0; i < batches.Length; i++)
            {
                Shp1Chunk.Batch batch = batches[i];
                Shp1Chunk.BatchPacketLocation packetLoc = packetLocations[batch.PacketIndex];

                for (int p = 0; p < batch.PacketCount; p++)
                {
                    int packetBytesRead = 0;

                    while (packetBytesRead < packetLoc.Size)
                    {
                        Shp1Chunk.BatchPrimitive primitive = new Shp1Chunk.BatchPrimitive();
                        primitive.Load(shp1Chunk._dataCopy, (int)(shp1Chunk.PrimitiveDataOffset + packetLoc.Offset + packetBytesRead));

                        PrimitiveList pmEntry = new PrimitiveList();
                        pmEntry.VertexCount = primitive.VertexCount;
                        pmEntry.VertexStart = finalData.Count;

                        //Immediately following the primitive is BatchPrimitive.VertexCount * (numElements * elementSize) bytes
                        int primitiveDataOffset = (int)(shp1Chunk.PrimitiveDataOffset + packetLoc.Offset + 3); //3 bytes for the BatchPrimitive
                        for (int v = 0; v < primitive.VertexCount; v++)
                        {
                            VertexFormatLayout newVert = new VertexFormatLayout();
                            for (int u = 0; u < 3; u++)
                            {
                                //Hack Hack, this is overflowing... maybe off by one error? 
                                if (primitiveDataOffset > shp1Chunk._dataCopy.Length - 2)
                                    continue;

                                ushort index = (ushort)FSHelpers.Read16(shp1Chunk._dataCopy, primitiveDataOffset);
                                primitiveDataOffset += 2;

                                switch (u)
                                {
                                    case 0:
                                        if (index < vertList.Count)
                                            newVert.Position = vertList[index];
                                        break;
                                    case 1:
                                        if (index < colorList.Count)
                                            newVert.Color = colorList[index];
                                        break;
                                    case 2:
                                        if (index < tcList.Count)
                                            newVert.TexCoord = tcList[index];
                                        break;

                                }
                            }

                            finalData.Add(newVert);
                        }

                        packetBytesRead += (primitive.VertexCount*6) + 3;
                        _renderList.Add(pmEntry);
                    }
                }
            }


            /*for (int i = 0; i < shp1Chunk.SectionCount; i++)
            {
                Shp1Chunk.Batch batch = new Shp1Chunk.Batch();
                batch.Load(shp1Chunk._dataCopy, ref batchDataOffset);
                int packetDataOffset = (int)shp1Chunk.PacketLocationOffset;

                for (int p = 0; p < batch.PacketCount; p++)
                {
                    Shp1Chunk.BatchPacketLocation bP = new Shp1Chunk.BatchPacketLocation();
                    

                    //And then get the data from it or something.
                    int packetReadCount = 0;
                    int primitiveOffset = 0;
                    while (packetReadCount < bP.Size)
                    {
                        Shp1Chunk.BatchPrimitive primitive = new Shp1Chunk.BatchPrimitive();
                        primitive.Load(shp1Chunk._dataCopy, (int)(shp1Chunk.PrimitiveDataOffset + bP.Offset + primitiveOffset));

                        PrimitiveList pmEntry = new PrimitiveList();
                        pmEntry.VertexCount = primitive.VertexCount;
                        pmEntry.VertexStart = finalData.Count;

                        List<ushort> primitiveIndexes = new List<ushort>();
                        //Immediately following the primitive is BatchPrimitive.VertexCount * (numElements * elementSize) bytes
                        int primitiveDataOffset = (int)(shp1Chunk.PrimitiveDataOffset + packetReadCount + bP.Offset + 3); //3 bytes for the BatchPrimitive
                        for (int v = 0; v < primitive.VertexCount; v++)
                        {
                            VertexFormatLayout newVert = new VertexFormatLayout();
                            for (int u = 0; u < 3; u++)
                            {
                                ushort index = (ushort)FSHelpers.Read16(shp1Chunk._dataCopy, primitiveDataOffset);
                                primitiveIndexes.Add(index);
                                primitiveDataOffset += 2;


                                //Console.WriteLine(index);
                                switch (u)
                                {
                                    case 0:
                                        if (index < vertList.Count)
                                            newVert.Position = vertList[index];
                                        break;
                                    case 1:
                                        if (index < colorList.Count)
                                            newVert.Color = colorList[index];
                                        break;
                                    case 2:
                                        if (index < tcList.Count)
                                            newVert.TexCoord = tcList[index];
                                        break;

                                }
                            }

                            finalData.Add(newVert);
                        }

                        packetReadCount += primitive.VertexCount * 6 + 3;
                        primitiveOffset += primitive.VertexCount * 6 + 3;

                        _renderList.Add(pmEntry);
                    }
                }
            }*/

            return finalData;
        }

        public override void Save(BinaryWriter stream)
        {
            stream.Write(_origDataCache);
        }

        #region File Formats

        private class Header
        {
            public string Magic;
            public string Type;
            public int FileSize;
            public int ChunkCount;

            public void Load(byte[] data, ref int offset)
            {
                Magic = FSHelpers.ReadString(data, 0, 4);
                Type = FSHelpers.ReadString(data, 4, 4);
                FileSize = FSHelpers.Read32(data, 8);
                ChunkCount = FSHelpers.Read32(data, 12);

                //Four variables are followed by an unused tag and some padding.
                offset += 32;
            }

            public void Save(BinaryWriter stream)
            {
                FSHelpers.WriteString(stream, Magic, 4);
                FSHelpers.WriteString(stream, Type, 4);
                FSHelpers.Write32(stream, FileSize);
                FSHelpers.Write32(stream, ChunkCount);

                //Write in the unused tag and padding
                FSHelpers.WriteArray(stream, FSHelpers.ToBytes(0x53565233, 4));
                FSHelpers.WriteArray(stream, FSHelpers.ToBytes(0xFFFFFFFF, 4));
                FSHelpers.WriteArray(stream, FSHelpers.ToBytes(0xFFFFFFFF, 4));
                FSHelpers.WriteArray(stream, FSHelpers.ToBytes(0xFFFFFFFF, 4));
            }
        }

        private class BaseChunk
        {
            public string Type;
            public int ChunkSize;

            public byte[] _dataCopy;

            public virtual void Load(byte[] data, ref int offset)
            {
                Type = FSHelpers.ReadString(data, offset, 4);
                ChunkSize = FSHelpers.Read32(data, offset + 4);

                _dataCopy = FSHelpers.ReadN(data, offset, ChunkSize);
            }

            public virtual void Save(BinaryWriter stream)
            {
                FSHelpers.WriteString(stream, Type, 4);
                FSHelpers.Write32(stream, ChunkSize);
            }
        }

        private class Inf1Chunk : BaseChunk
        {
            public short Unknown1;
            public int Unknown2;
            public int VertexCount;
            public int DataOffset;

            public override void Load(byte[] data, ref int offset)
            {
                base.Load(data, ref offset);

                Unknown1 = FSHelpers.Read16(data, offset + 8);
                Unknown2 = FSHelpers.Read32(data, offset + 12); //2 bytes padding after Unknown1
                VertexCount = FSHelpers.Read32(data, offset + 16);
                DataOffset = FSHelpers.Read32(data, offset + 20);

                var hierarchyDataList = new List<HierarchyData>();
                int dataOffsetCpy = DataOffset;

                //Nintendo doesn't seem to define how many of these there are anywhere,
                //so we're going to have to sort of guess at the maximum (since the struct
                //is padded to 32 byte alignment) and then break early. Bleh.
                int maxHierarchyData = (ChunkSize - 24) / 4; //24 bytes for the header, 4 bytes per HierarchyData
                for (int i = 0; i < maxHierarchyData; i++)
                {
                    HierarchyData hData = new HierarchyData();
                    hData.Load(data, ref dataOffsetCpy);

                    hierarchyDataList.Add(hData);

                    //Anything after the Finish command is just "This is padding data to " padding.
                    if (hData.Type == HierarchyData.HierarchyDataTypes.Finish)
                        break;
                }

                offset += ChunkSize;
            }

            //"SceneGraphRaw"
            private class HierarchyData
            {
                public enum HierarchyDataTypes : ushort
                {
                    Finish = 0x0, NewNode = 0x01, EndNode = 0x02,
                    Joint = 0x10, Material = 0x11, Shape = 0x12,
                }

                public HierarchyDataTypes Type;
                public ushort Index;

                public void Load(byte[] data, ref int offset)
                {
                    Type = (HierarchyDataTypes)FSHelpers.Read16(data, offset);
                    Index = (ushort)FSHelpers.Read16(data, offset + 2);

                    offset += 4;
                }
            }
        }

        private class Vtx1Chunk : BaseChunk
        {
            public class VertexFormat
            {
                public ArrayTypes ArrayType;
                public uint ArrayCount;
                public DataTypes DataType;
                public byte DecimalPoint;

                public void Load(byte[] data, ref int offset)
                {
                    ArrayType = (ArrayTypes)FSHelpers.Read32(data, offset);
                    ArrayCount = (uint)FSHelpers.Read32(data, offset + 4);
                    DataType = (DataTypes)FSHelpers.Read32(data, offset + 8);
                    DecimalPoint = FSHelpers.Read8(data, offset + 12);

                    offset += 16; //3 bytes padding after DecimalPoint
                }
            }

            public enum VertexDataTypes
            {
                Position = 0,
                Color0 = 3,
                Tex0 = 5,
            }

            public int DataOffset;
            public int[] VertexDataOffsets = new int[13];

            //Not part of the header
            public List<VertexFormat> VertexFormats = new List<VertexFormat>();

            public override void Load(byte[] data, ref int offset)
            {
                base.Load(data, ref offset);

                DataOffset = FSHelpers.Read32(data, offset + 0x8);
                for (int i = 0; i < 13; i++)
                    VertexDataOffsets[i] = FSHelpers.Read32(data, (offset + 0xC) + (i * 0x4));

                //Load the VertexFormats 
                int dataOffsetCpy = offset + DataOffset;
                VertexFormat vFormat;
                do
                {
                    vFormat = new VertexFormat();
                    vFormat.Load(data, ref dataOffsetCpy);
                    VertexFormats.Add(vFormat);
                } while (vFormat.ArrayType != ArrayTypes.NullAttr);

                offset += ChunkSize;

            }
        }

        private class Evp1Chunk : BaseChunk
        {
            public ushort SectionCount;
            public uint CountsArrayOffset;
            public uint IndicesOffset;
            public uint WeightsOffset;
            public uint MatrixDataOffset;

            public override void Load(byte[] data, ref int offset)
            {
                base.Load(data, ref offset);

                SectionCount = (ushort)FSHelpers.Read16(data, offset + 0x8);
                CountsArrayOffset = (uint)FSHelpers.Read32(data, offset + 0xC);
                IndicesOffset = (uint)FSHelpers.Read32(data, offset + 0x10);
                WeightsOffset = (uint)FSHelpers.Read32(data, offset + 0x14);
                MatrixDataOffset = (uint)FSHelpers.Read32(data, offset + 0x18);


                offset += ChunkSize;
            }
        }

        private class Drw1Chunk : BaseChunk
        {
            public ushort SectionCount;
            public uint IsWeightedOffset;
            public uint DataOffset;

            //Not part of header
            public bool[] IsWeighted;
            public ushort[] Data; //Related to that thing in collision perhaps?

            public override void Load(byte[] data, ref int offset)
            {
                base.Load(data, ref offset);

                SectionCount = (ushort)FSHelpers.Read16(data, offset + 0x8);
                IsWeightedOffset = (uint)FSHelpers.Read32(data, offset + 0xC);
                DataOffset = (uint)FSHelpers.Read32(data, offset + 0x10);

                IsWeighted = new bool[SectionCount];
                Data = new ushort[SectionCount];

                for (int i = 0; i < SectionCount; i++)
                {
                    IsWeighted[i] = Convert.ToBoolean(FSHelpers.Read8(data, (int)(offset + IsWeightedOffset + i)));
                    Data[i] = (ushort)FSHelpers.Read16(data, (int)(offset + DataOffset + (i * 2)));
                }

                offset += ChunkSize;
            }
        }

        private class Jnt1Chunk : BaseChunk
        {
            public ushort SectionCount;
            public uint EntryOffset;
            public uint UnknownOffset;
            public uint StringTableOffset;

            public override void Load(byte[] data, ref int offset)
            {
                base.Load(data, ref offset);

                SectionCount = (ushort)FSHelpers.Read16(data, offset + 0x8);
                EntryOffset = (uint)FSHelpers.Read32(data, offset + 0xC);
                UnknownOffset = (uint)FSHelpers.Read32(data, offset + 0x10);
                StringTableOffset = (uint)FSHelpers.Read32(data, offset + 0x14);

                offset += ChunkSize;
            }

        }

        /// <summary>
        /// Since primitives have different attributes (ie: some have position, some have normals, some have texcoords)
        /// each different primitive is placed into a "batch", and each batch ahs a fixed set of vertex attributes. Each 
        /// batch can then have several "packets", which are used for animations. A packet is a collection of primitives.
        /// </summary>
        private class Shp1Chunk : BaseChunk
        {
            public class Batch
            {
                public byte MatrixType;
                public ushort PacketCount;
                public ushort AttribOffset;
                public ushort FirstMatrixIndex;
                public ushort PacketIndex;

                public float Unknown;
                public Vector3 BoundingBoxMin;
                public Vector3 BoundingBoxMax;

                //Bleh
                public List<BatchAttribute> BatchAttributes = new List<BatchAttribute>();

                public void Load(byte[] data, ref int offset)
                {
                    MatrixType = FSHelpers.Read8(data, offset);
                    PacketCount = (ushort)FSHelpers.Read16(data, offset + 0x2);
                    AttribOffset = (ushort)FSHelpers.Read16(data, offset + 0x4);
                    FirstMatrixIndex = (ushort)FSHelpers.Read16(data, offset + 0x6);
                    PacketIndex = (ushort)FSHelpers.Read16(data, offset + 0x8);

                    Unknown = FSHelpers.ReadFloat(data, offset + 0xC);
                    BoundingBoxMin = FSHelpers.ReadVector3(data, offset + 0x10);
                    BoundingBoxMax = FSHelpers.ReadVector3(data, offset + 0x1C);

                    offset += 40;
                }
            }

            public class BatchAttribute
            {
                public ArrayTypes AttribType;
                public DataTypes DataType;

                public void Load(byte[] data, ref int offset)
                {
                    AttribType = (ArrayTypes)FSHelpers.Read32(data, offset);
                    DataType = (DataTypes)FSHelpers.Read32(data, offset + 0x4);

                    offset += 0x8;
                }
            }

            public struct BatchPacketLocation
            {
                public uint Size;
                public uint Offset;
            }

            public class BatchPrimitive
            {
                public PrimitiveTypes Type;
                public ushort VertexCount;

                public void Load(byte[] data, int offset)
                {
                    Type = (PrimitiveTypes)FSHelpers.Read8(data, offset);

                    VertexCount = (ushort)FSHelpers.Read16(data, offset + 0x1);
                }
            }

            public ushort SectionCount;
            public uint BatchOffset;
            public uint Unknown1Offset;
            public uint UnknownPadding;
            public uint AttributeOffset;
            public uint MatrixTableOffset;
            public uint PrimitiveDataOffset;
            public uint MatrixDataOffset;
            public uint PacketLocationOffset;

            public override void Load(byte[] data, ref int offset)
            {
                base.Load(data, ref offset);

                //Load information from header
                SectionCount = (ushort)FSHelpers.Read16(data, offset + 0x8);
                BatchOffset = (uint)FSHelpers.Read32(data, offset + 0xC);
                Unknown1Offset = (uint)FSHelpers.Read32(data, offset + 0x10);
                UnknownPadding = (uint)FSHelpers.Read32(data, offset + 0x14);
                AttributeOffset = (uint)FSHelpers.Read32(data, offset + 0x18);
                MatrixTableOffset = (uint)FSHelpers.Read32(data, offset + 0x1C);
                PrimitiveDataOffset = (uint)FSHelpers.Read32(data, offset + 0x20);
                MatrixDataOffset = (uint)FSHelpers.Read32(data, offset + 0x24);
                PacketLocationOffset = (uint)FSHelpers.Read32(data, offset + 0x28);

                offset += ChunkSize;
            }
        }
        #endregion
    }
}
