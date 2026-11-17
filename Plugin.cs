using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using GorillaNetworking;
using GorillaTag.CosmeticSystem;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CosmeticExporter
{
    [BepInPlugin("local.gtag.cosmeticexporter", "Cosmetic Exporter", "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal readonly List<Entry> Entries = new List<Entry>();
        internal readonly List<Entry> Matches = new List<Entry>();
        internal Entry Selected;
        internal string Status = "Waiting for the cosmetic catalog...";
        internal bool Busy;
        internal string Folder;
        private CosmeticInfoV2[] catalog;
        private float nextCatalogCheck;
        private string query = "";
        private Browser panel;
        private Coroutine operation;
        private readonly List<AsyncOperationHandle<GameObject>> handles = new List<AsyncOperationHandle<GameObject>>();
        private bool open;
        private CursorLockMode cursorLock;
        private bool cursorVisible;

        private void Awake()
        {
            Folder = Path.Combine(Paths.GameRootPath, "CosmeticExporter", "Exports");
            panel = new Browser(this);
            SetOpen(true);
        }

        private void Update()
        {
            panel.Visibility = Mathf.MoveTowards(panel.Visibility, open ? 1f : 0f, Time.unscaledDeltaTime * 7f);
            var keyboard = Keyboard.current;
            if (keyboard?.f6Key.wasPressedThisFrame == true) SetOpen(!open);
            else if (open && keyboard?.escapeKey.wasPressedThisFrame == true) SetOpen(false);
            if (Time.unscaledTime < nextCatalogCheck) return;
            nextCatalogCheck = Time.unscaledTime + 1f;
            var current = CosmeticsController.instance?.v2_allCosmetics;
            if (current == null || current.Length == 0 || ReferenceEquals(current, catalog)) return;
            catalog = current;
            Entries.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var info in current)
            {
                if (string.IsNullOrWhiteSpace(info.playFabID)) continue;
                if (!seen.Add(info.playFabID)) continue;
                Entries.Add(new Entry(info));
            }
            Entries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            Filter(query);
            if (!Busy) Status = Entries.Count + " cosmetics loaded";
        }

        private void LateUpdate()
        {
            if (open) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        private void OnGUI() => panel?.Draw(open);

        internal void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            if (open)
            {
                cursorLock = Cursor.lockState;
                cursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else { Cursor.lockState = cursorLock; Cursor.visible = cursorVisible; }
        }

        internal void Filter(string value)
        {
            query = value ?? "";
            Matches.Clear();
            foreach (var item in Entries)
                if (item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    Matches.Add(item);
        }

        internal void Export()
        {
            if (Busy || Selected == null) return;
            Busy = true;
            operation = StartCoroutine(GuardedExport(Selected));
        }

        private IEnumerator GuardedExport(Entry selected)
        {
            var work = ExportItem(selected);
            {
                try
                {
                    while (true)
                    {
                        object next;
                        try
                        {
                            if (!work.MoveNext()) break;
                            next = work.Current;
                        }
                        catch (Exception error)
                        {
                            Status = "Export failed: " + error.Message;
                            Logger.LogError(error);
                            break;
                        }
                        yield return next;
                    }
                }
                finally
                {
                    (work as IDisposable)?.Dispose();
                    foreach (var handle in handles) if (handle.IsValid()) Addressables.Release(handle);
                    handles.Clear();
                    Busy = false;
                    operation = null;
                }
            }
        }

        private IEnumerator ExportItem(Entry selected)
        {
            var parts = new List<CosmeticPart>();
            void Add(CosmeticPart[] items) { if (items != null) parts.AddRange(items); }
            Add(selected.Info.functionalParts);
            Add(selected.Info.holdableParts);
            if (parts.Count == 0) Add(selected.Info.wardrobeParts);
            if (parts.Count == 0) Add(selected.Info.storeParts);
            if (parts.Count == 0) Add(selected.Info.localRigParts);
            if (parts.Count == 0) Add(selected.Info.firstPersonViewParts);
            var unique = new HashSet<object>();
            using var exporter = new GlbExporter();
            int loaded = 0;
            foreach (var part in parts)
            {
                if (part.prefabAssetRef == null || !part.prefabAssetRef.RuntimeKeyIsValid()) continue;
                var key = part.prefabAssetRef.RuntimeKey;
                if (!unique.Add(key)) continue;
                Status = "Loading model part " + (++loaded) + " for " + selected.Name;
                var handle = Addressables.LoadAssetAsync<GameObject>(key);
                handles.Add(handle);
                yield return handle;
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw new InvalidOperationException("Model part unavailable: " + key);
                Status = "Exporting " + selected.Name;
                exporter.AddPrefab(handle.Result);
                yield return null;
            }
            byte[] bytes = exporter.Build(selected.Name);
            string name = FileName(selected.Id + "_" + selected.Name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".glb";
            Directory.CreateDirectory(Folder);
            string destination = Path.Combine(Folder, name);
            var writing = Task.Run(() => WriteAtomic(destination, bytes));
            while (!writing.IsCompleted) yield return null;
            writing.GetAwaiter().GetResult();
            Status = "Saved " + name + " (" + exporter.MeshCount + " meshes)";
        }

        private static string FileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var result = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
            if (result.Length > 100) result = result.Substring(0, 100);
            return string.IsNullOrEmpty(result) ? "cosmetic" : result;
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private void OnDestroy()
        {
            SetOpen(false);
            if (operation != null) StopCoroutine(operation);
            foreach (var handle in handles) if (handle.IsValid()) Addressables.Release(handle);
            handles.Clear();
        }

        internal sealed class Entry
        {
            public readonly CosmeticInfoV2 Info;
            public string Id => Info.playFabID;
            public string Name => string.IsNullOrWhiteSpace(Info.displayName) ? Id : Info.displayName;
            public Entry(CosmeticInfoV2 info) => Info = info;
        }
    }
}
