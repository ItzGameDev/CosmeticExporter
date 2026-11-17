using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Valve.Newtonsoft.Json;
using UnityEngine;

namespace CosmeticExporter
{
    internal sealed class GlbExporter : IDisposable
    {
        private readonly MemoryStream binary = new MemoryStream();
        private readonly List<object> views = new List<object>();
        private readonly List<object> accessors = new List<object>();
        private readonly List<object> meshes = new List<object>();
        private readonly List<object> nodes = new List<object>();
        private readonly List<object> materials = new List<object>();
        private readonly List<object> images = new List<object>();
        private readonly List<object> textures = new List<object>();
        private readonly Dictionary<string, int> imageCache = new Dictionary<string, int>();
        public int MeshCount => meshes.Count;

        public void AddPrefab(GameObject prefab)
        {
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                bool active = renderer.enabled;
                for (var t = renderer.transform; t != null && t != prefab.transform; t = t.parent)
                    active &= t.gameObject.activeSelf;
                if (!active) continue;
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                var data = MeshData.Read(mesh);
                Matrix4x4 transform = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Matrix4x4 normalTransform = transform.inverse.transpose;
                var position = new float[data.Positions.Length * 3];
                var normals = data.Normals == null ? null : new float[position.Length];
                Vector3 minimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 maximum = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < data.Positions.Length; i++)
                {
                    Vector3 vertex = transform.MultiplyPoint3x4(data.Positions[i]);
                    vertex.x = -vertex.x;
                    if (!Finite(vertex.x) || !Finite(vertex.y) || !Finite(vertex.z)) throw new InvalidDataException("Mesh contains invalid positions");
                    minimum = Vector3.Min(minimum, vertex);
                    maximum = Vector3.Max(maximum, vertex);
                    Put(position, i, vertex);
                    if (normals == null) continue;
                    Vector3 normal = normalTransform.MultiplyVector(data.Normals[i]).normalized;
                    normal.x = -normal.x;
                    Put(normals, i, normal);
                }
                var attributes = new Dictionary<string, object>
                {
                    ["POSITION"] = Floats(position, "VEC3", new[] { minimum.x, minimum.y, minimum.z }, new[] { maximum.x, maximum.y, maximum.z })
                };
                if (normals != null) attributes["NORMAL"] = Floats(normals, "VEC3");
                if (data.Colors != null)
                {
                    var colors = new float[data.Colors.Length * 4];
                    for (int i = 0; i < data.Colors.Length; i++)
                    {
                        colors[i * 4] = Mathf.Clamp01(data.Colors[i].r);
                        colors[i * 4 + 1] = Mathf.Clamp01(data.Colors[i].g);
                        colors[i * 4 + 2] = Mathf.Clamp01(data.Colors[i].b);
                        colors[i * 4 + 3] = Mathf.Clamp01(data.Colors[i].a);
                    }
                    attributes["COLOR_0"] = Floats(colors, "VEC4");
                }
                if (data.Uv != null)
                {
                    var uv = new float[data.Uv.Length * 2];
                    for (int i = 0; i < data.Uv.Length; i++) { uv[i * 2] = data.Uv[i].x; uv[i * 2 + 1] = 1f - data.Uv[i].y; }
                    attributes["TEXCOORD_0"] = Floats(uv, "VEC2");
                }
                var primitives = new List<object>();
                var sourceMaterials = renderer.sharedMaterials;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                    int[] indices = data.Indices[submesh];
                    if (indices.Length == 0) continue;
                    bool flip = transform.determinant >= 0f;
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                        if (flip) { int saved = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = saved; }
                    foreach (int index in indices) if (index < 0 || index >= data.Positions.Length) throw new InvalidDataException("Mesh index outside its vertex buffer");
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block, submesh);
                    if (block.isEmpty) renderer.GetPropertyBlock(block);
                    Material material = sourceMaterials.Length == 0 ? null : sourceMaterials[Math.Min(submesh, sourceMaterials.Length - 1)];
                    primitives.Add(new { attributes, indices = Indices(indices), material = Material(material, block), mode = 4 });
                }
                if (primitives.Count == 0) continue;
                int meshIndex = meshes.Count;
                meshes.Add(new { name = mesh.name, primitives });
                nodes.Add(new { name = prefab.name + "/" + renderer.name, mesh = meshIndex });
            }
        }

        private int Material(Material source, MaterialPropertyBlock block)
        {
            Color color = Color.white;
            if (source != null)
            {
                if (source.HasProperty("_BaseColor")) color = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color")) color = source.GetColor("_Color");
            }
            if (block.HasColor("_BaseColor")) color = block.GetColor("_BaseColor");
            else if (block.HasColor("_Color")) color = block.GetColor("_Color");
            var pbr = new Dictionary<string, object>
            {
                ["baseColorFactor"] = new[] { color.r, color.g, color.b, color.a },
                ["metallicFactor"] = 0,
                ["roughnessFactor"] = .8f
            };
            var result = new Dictionary<string, object> { ["name"] = source?.name ?? "Default", ["pbrMetallicRoughness"] = pbr, ["doubleSided"] = source != null && source.HasProperty("_Cull") && source.GetFloat("_Cull") == 0 };
            if (source != null)
            {
                string property = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                Texture texture = block.GetTexture(property) ?? (source.HasProperty(property) ? source.GetTexture(property) : null);
                bool atlas = source.IsKeywordEnabled("_USE_TEX_ARRAY_ATLAS") || source.HasProperty("_USE_TEX_ARRAY_ATLAS") && source.GetFloat("_USE_TEX_ARRAY_ATLAS") > .5f;
                int slice = 0;
                if (atlas)
                {
                    texture = block.GetTexture("_BaseMap_Atlas") ?? (source.HasProperty("_BaseMap_Atlas") ? source.GetTexture("_BaseMap_Atlas") : texture);
                    slice = Mathf.RoundToInt(block.HasFloat("_BaseMap_AtlasSlice") ? block.GetFloat("_BaseMap_AtlasSlice") :
                        source.HasProperty("_BaseMap_AtlasSlice") ? source.GetFloat("_BaseMap_AtlasSlice") : 0);
                }
                if (texture is Texture2D || texture is Texture2DArray)
                {
                    int index = Image(texture, slice);
                    Vector2 scale = source.HasProperty(property) ? source.GetTextureScale(property) : Vector2.one;
                    Vector2 offset = source.HasProperty(property) ? source.GetTextureOffset(property) : Vector2.zero;
                    if (block.HasVector(property + "_ST"))
                    {
                        var st = block.GetVector(property + "_ST");
                        scale = new Vector2(st.x, st.y); offset = new Vector2(st.z, st.w);
                    }
                    pbr["baseColorTexture"] = new
                    {
                        index,
                        extensions = new Dictionary<string, object>
                        {
                            ["KHR_texture_transform"] = new { offset = new[] { offset.x, 1f - scale.y - offset.y }, scale = new[] { scale.x, scale.y } }
                        }
                    };
                }
                bool cutout = source.IsKeywordEnabled("_ALPHATEST_ON") || source.renderQueue >= 2450 && source.renderQueue < 3000;
                if (cutout) { result["alphaMode"] = "MASK"; result["alphaCutoff"] = source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : .5f; }
                else if (source.renderQueue >= 3000 || color.a < .999f) result["alphaMode"] = "BLEND";
            }
            int material = materials.Count;
            materials.Add(result);
            return material;
        }

        private int Image(Texture source, int slice)
        {
            string key = source.GetInstanceID() + ":" + slice;
            if (imageCache.TryGetValue(key, out int cached)) return cached;
            var pixels = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            Texture2D flattened = null;
            RenderTexture target = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Texture input = source;
                if (source is Texture2DArray array)
                {
                    if (slice < 0 || slice >= array.depth) throw new InvalidDataException("Texture atlas slice is out of range");
                    flattened = new Texture2D(array.width, array.height, array.graphicsFormat, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
                    Graphics.CopyTexture(array, slice, 0, flattened, 0, 0);
                    input = flattened;
                }
                target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(input, target);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                pixels.Apply(false);
                byte[] png = ImageConversion.EncodeToPNG(pixels);
                int view = View(png);
                int index = images.Count;
                images.Add(new { name = source.name, bufferView = view, mimeType = "image/png" });
                textures.Add(new { source = index, sampler = 0 });
                imageCache.Add(key, index);
                return index;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (flattened != null) UnityEngine.Object.Destroy(flattened);
                UnityEngine.Object.Destroy(pixels);
            }
        }

        private static void Put(float[] destination, int index, Vector3 value)
        {
            destination[index * 3] = value.x;
            destination[index * 3 + 1] = value.y;
            destination[index * 3 + 2] = value.z;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private int Floats(float[] data, string type, float[] min = null, float[] max = null)
        {
            var bytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            int view = View(bytes, 34962);
            int components = type == "VEC4" ? 4 : type == "VEC3" ? 3 : 2;
            var accessor = new Dictionary<string, object> { ["bufferView"] = view, ["componentType"] = 5126, ["count"] = data.Length / components, ["type"] = type };
            if (min != null) { accessor["min"] = min; accessor["max"] = max; }
            int index = accessors.Count; accessors.Add(accessor); return index;
        }

        private int Indices(int[] data)
        {
            var bytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            int view = View(bytes, 34963);
            int index = accessors.Count;
            accessors.Add(new { bufferView = view, componentType = 5125, count = data.Length, type = "SCALAR" });
            return index;
        }

        private int View(byte[] bytes, int target = 0)
        {
            while (binary.Length % 4 != 0) binary.WriteByte(0);
            var view = new Dictionary<string, object> { ["buffer"] = 0, ["byteOffset"] = binary.Length, ["byteLength"] = bytes.Length };
            if (target != 0) view["target"] = target;
            int index = views.Count; views.Add(view);
            binary.Write(bytes, 0, bytes.Length);
            return index;
        }

        public byte[] Build(string name)
        {
            if (meshes.Count == 0) throw new InvalidOperationException("This cosmetic has no exportable model geometry");
            var roots = new int[nodes.Count];
            for (int i = 0; i < roots.Length; i++) roots[i] = i;
            var document = new Dictionary<string, object>
            {
                ["asset"] = new { version = "2.0", generator = "Cosmetic Exporter" },
                ["scene"] = 0, ["scenes"] = new[] { new { name, nodes = roots } },
                ["nodes"] = nodes, ["meshes"] = meshes, ["materials"] = materials,
                ["buffers"] = new[] { new { byteLength = binary.Length } },
                ["bufferViews"] = views, ["accessors"] = accessors
            };
            if (images.Count > 0)
            {
                document["images"] = images;
                document["textures"] = textures;
                document["samplers"] = new[] { new { magFilter = 9729, minFilter = 9987, wrapS = 10497, wrapT = 10497 } };
                document["extensionsUsed"] = new[] { "KHR_texture_transform" };
            }
            byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document));
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            uint jsonLength = (uint)((json.Length + 3) & ~3);
            uint binLength = (uint)((binary.Length + 3) & ~3);
            writer.Write(0x46546C67u); writer.Write(2u); writer.Write(12u + 8u + jsonLength + 8u + binLength);
            writer.Write(jsonLength); writer.Write(0x4E4F534Au); writer.Write(json);
            while (output.Position % 4 != 0) writer.Write((byte)32);
            writer.Write(binLength); writer.Write(0x004E4942u);
            binary.Position = 0; binary.CopyTo(output);
            while (output.Position % 4 != 0) writer.Write((byte)0);
            return output.ToArray();
        }

        public void Dispose() => binary.Dispose();
    }
}
