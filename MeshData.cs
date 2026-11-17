using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace CosmeticExporter
{
    internal sealed class MeshData
    {
        public Vector3[] Positions, Normals;
        public Vector2[] Uv;
        public Color[] Colors;
        public int[][] Indices;

        public static MeshData Read(Mesh mesh)
        {
            var data = new MeshData { Indices = new int[mesh.subMeshCount][] };
            if (mesh.isReadable)
            {
                data.Positions = mesh.vertices;
                var normal = mesh.normals;
                var sourceUv = mesh.uv;
                if (normal.Length == mesh.vertexCount) data.Normals = normal;
                if (sourceUv.Length == mesh.vertexCount) data.Uv = sourceUv;
                var colors = mesh.colors;
                if (colors.Length == mesh.vertexCount) data.Colors = colors;
                for (int i = 0; i < mesh.subMeshCount; i++) data.Indices[i] = mesh.GetIndices(i, true);
                return data;
            }
            var streams = new Dictionary<int, byte[]>();
            float[] Attribute(VertexAttribute attribute)
            {
                if (!mesh.HasVertexAttribute(attribute)) return null;
                int stream = mesh.GetVertexAttributeStream(attribute);
                if (!streams.TryGetValue(stream, out var bytes))
                {
                    using var buffer = mesh.GetVertexBuffer(stream);
                    streams.Add(stream, bytes = ReadBuffer(buffer));
                }
                int offset = mesh.GetVertexAttributeOffset(attribute);
                int stride = mesh.GetVertexBufferStride(stream);
                int dimension = mesh.GetVertexAttributeDimension(attribute);
                var format = mesh.GetVertexAttributeFormat(attribute);
                int size = FormatSize(format);
                var values = new float[mesh.vertexCount * dimension];
                for (int vertex = 0; vertex < mesh.vertexCount; vertex++)
                    for (int axis = 0; axis < dimension; axis++)
                        values[vertex * dimension + axis] = Decode(bytes, vertex * stride + offset + axis * size, format);
                return values;
            }
            float[] positions = Attribute(VertexAttribute.Position) ?? throw new InvalidDataException("Mesh has no positions");
            int positionDimension = mesh.GetVertexAttributeDimension(VertexAttribute.Position);
            if (positionDimension < 3) throw new InvalidDataException("Unsupported vertex position layout");
            data.Positions = new Vector3[mesh.vertexCount];
            for (int i = 0; i < mesh.vertexCount; i++)
                data.Positions[i] = new Vector3(positions[i * positionDimension], positions[i * positionDimension + 1], positions[i * positionDimension + 2]);
            float[] normals = Attribute(VertexAttribute.Normal);
            if (normals != null && mesh.GetVertexAttributeDimension(VertexAttribute.Normal) >= 3)
            {
                int dimension = mesh.GetVertexAttributeDimension(VertexAttribute.Normal);
                data.Normals = new Vector3[mesh.vertexCount];
                for (int i = 0; i < mesh.vertexCount; i++) data.Normals[i] = new Vector3(normals[i * dimension], normals[i * dimension + 1], normals[i * dimension + 2]);
            }
            float[] uv = Attribute(VertexAttribute.TexCoord0);
            if (uv != null && mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0) >= 2)
            {
                int dimension = mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0);
                data.Uv = new Vector2[mesh.vertexCount];
                for (int i = 0; i < mesh.vertexCount; i++) data.Uv[i] = new Vector2(uv[i * dimension], uv[i * dimension + 1]);
            }
            using var indexBuffer = mesh.GetIndexBuffer();
            float[] colorValues = Attribute(VertexAttribute.Color);
            if (colorValues != null)
            {
                int dimension = mesh.GetVertexAttributeDimension(VertexAttribute.Color);
                if (dimension >= 3)
                {
                    data.Colors = new Color[mesh.vertexCount];
                    for (int i = 0; i < mesh.vertexCount; i++)
                        data.Colors[i] = new Color(colorValues[i * dimension], colorValues[i * dimension + 1],
                            colorValues[i * dimension + 2], dimension >= 4 ? colorValues[i * dimension + 3] : 1f);
                }
            }
            byte[] indexBytes = ReadBuffer(indexBuffer);
            int indexSize = mesh.indexFormat == IndexFormat.UInt16 ? 2 : 4;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var submesh = mesh.GetSubMesh(i);
                var indices = new int[submesh.indexCount];
                for (int j = 0; j < indices.Length; j++)
                {
                    int offset = (submesh.indexStart + j) * indexSize;
                    indices[j] = checked((indexSize == 2 ? BitConverter.ToUInt16(indexBytes, offset) : (int)BitConverter.ToUInt32(indexBytes, offset)) + submesh.baseVertex);
                }
                data.Indices[i] = indices;
            }
            return data;
        }

        private static byte[] ReadBuffer(GraphicsBuffer buffer)
        {
            if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("GPU mesh readback is unavailable on this graphics device");
            var request = AsyncGPUReadback.Request(buffer);
            request.WaitForCompletion();
            if (request.hasError) throw new InvalidOperationException("Could not read model geometry from the GPU");
            return request.GetData<byte>().ToArray();
        }

        private static int FormatSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32: return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16: return 2;
                case VertexAttributeFormat.UNorm8:
                case VertexAttributeFormat.SNorm8:
                case VertexAttributeFormat.UInt8:
                case VertexAttributeFormat.SInt8: return 1;
                default: throw new NotSupportedException("Unsupported vertex format: " + format);
            }
        }

        private static float Decode(byte[] bytes, int offset, VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32: return BitConverter.ToSingle(bytes, offset);
                case VertexAttributeFormat.Float16:
                    int bits = BitConverter.ToUInt16(bytes, offset);
                    int sign = (bits & 0x8000) == 0 ? 1 : -1;
                    int exponent = (bits >> 10) & 31;
                    int fraction = bits & 1023;
                    if (exponent == 31) return fraction == 0 ? sign * float.PositiveInfinity : float.NaN;
                    return sign * (float)(exponent == 0 ? fraction * Math.Pow(2, -24) : (1 + fraction / 1024.0) * Math.Pow(2, exponent - 15));
                case VertexAttributeFormat.UNorm8: return bytes[offset] / 255f;
                case VertexAttributeFormat.SNorm8: return Math.Max(-1f, (sbyte)bytes[offset] / 127f);
                case VertexAttributeFormat.UNorm16: return BitConverter.ToUInt16(bytes, offset) / 65535f;
                case VertexAttributeFormat.SNorm16: return Math.Max(-1f, BitConverter.ToInt16(bytes, offset) / 32767f);
                case VertexAttributeFormat.UInt8: return bytes[offset];
                case VertexAttributeFormat.SInt8: return (sbyte)bytes[offset];
                case VertexAttributeFormat.UInt16: return BitConverter.ToUInt16(bytes, offset);
                case VertexAttributeFormat.SInt16: return BitConverter.ToInt16(bytes, offset);
                case VertexAttributeFormat.UInt32: return BitConverter.ToUInt32(bytes, offset);
                case VertexAttributeFormat.SInt32: return BitConverter.ToInt32(bytes, offset);
                default: throw new NotSupportedException("Unsupported vertex format: " + format);
            }
        }
    }
}
