using System;
using System.IO;
using System.Text;
using CosmeticExporter;
using UnityEngine;
using Valve.Newtonsoft.Json.Linq;

public static class ExportChecks
{
    public static void Run()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Results"));
        Directory.CreateDirectory(folder);
        Check(folder, false, false);
        Check(folder, true, false);
        Check(folder, false, true);
        Debug.Log("COSMETIC_GLB_CHECKS_PASSED readable mesh, GPU-only mesh, PNG textures, atlas slice, handedness, index winding, UVs and GLB framing");
    }

    private static void Check(string folder, bool gpu, bool atlas)
    {
        var root = new GameObject("Fixture");
        var mesh = new Mesh { name = "triangle" };
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var array = new Texture2DArray(2, 2, 2, TextureFormat.RGBA32, false);
        Material material = null;
        try
        {
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.colors32 = new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255) };
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); texture.Apply();
            array.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }, 0);
            array.SetPixels(new[] { Color.blue, Color.blue, Color.blue, Color.blue }, 1); array.Apply();
            material = new Material(Shader.Find(atlas ? "Hidden/CosmeticExportFixture" : "Unlit/Texture"));
            if (atlas)
            {
                material.SetTexture("_BaseMap_Atlas", array);
                material.SetFloat("_USE_TEX_ARRAY_ATLAS", 1);
                material.SetFloat("_BaseMap_AtlasSlice", 1);
            }
            else material.mainTexture = texture;
            renderer.sharedMaterial = material;
            if (gpu) mesh.UploadMeshData(true);
            using var exporter = new GlbExporter();
            exporter.AddPrefab(root);
            byte[] glb = exporter.Build("Fixture");
            Require(BitConverter.ToUInt32(glb, 0) == 0x46546c67 && BitConverter.ToUInt32(glb, 4) == 2 &&
                BitConverter.ToUInt32(glb, 8) == glb.Length, "Invalid GLB framing");
            int jsonLength = (int)BitConverter.ToUInt32(glb, 12);
            var json = JObject.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength));
            int binaryStart = 20 + jsonLength + 8;
            var primitive = json["meshes"][0]["primitives"][0];
            int Position(int accessor)
            {
                int view = (int)json["accessors"][accessor]["bufferView"];
                return binaryStart + (int)json["bufferViews"][view]["byteOffset"];
            }
            int positions = Position((int)primitive["attributes"]["POSITION"]);
            Require(Math.Abs(BitConverter.ToSingle(glb, positions + 12) + 1f) < .001f, "Handedness conversion failed");
            int indices = Position((int)primitive["indices"]);
            Require(BitConverter.ToUInt32(glb, indices + 4) == 2, "Triangle winding was not reversed");
            int uv = Position((int)primitive["attributes"]["TEXCOORD_0"]);
            Require(BitConverter.ToSingle(glb, uv + 4) == 1f, "UV orientation was not converted");
            int colors = Position((int)primitive["attributes"]["COLOR_0"]);
            Require(BitConverter.ToSingle(glb, colors) == 1f && BitConverter.ToSingle(glb, colors + 4) == 0f, "Vertex colors were lost");
            int imageView = (int)json["images"][0]["bufferView"];
            int offset = binaryStart + (int)json["bufferViews"][imageView]["byteOffset"];
            int length = (int)json["bufferViews"][imageView]["byteLength"];
            var png = new byte[length]; Buffer.BlockCopy(glb, offset, png, 0, length);
            var decoded = new Texture2D(2, 2);
            try
            {
                Require(ImageConversion.LoadImage(decoded, png), "Embedded PNG cannot decode");
                if (atlas) Require(decoded.GetPixel(0, 0).b > .8f && decoded.GetPixel(0, 0).r < .1f, "Wrong atlas slice exported");
            }
            finally { UnityEngine.Object.DestroyImmediate(decoded); }
            File.WriteAllBytes(Path.Combine(folder, atlas ? "atlas.glb" : gpu ? "gpu.glb" : "mesh.glb"), glb);
        }
        finally
        {
            foreach (var item in new UnityEngine.Object[] { root, mesh, material, texture, array })
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
