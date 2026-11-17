Cosmetic Exporter 1.0.0

Copy CosmeticExporter.dll to Gorilla Tag/BepInEx/plugins.
Open Gorilla Tag through Steam. The browser opens on startup.
Press F6 to show or hide it. Escape or X closes it. Drag the title bar to move it.

Wait for the game's cosmetic catalog, search by name or ID, select an item,
then click Export selected to GLB.

Files are saved under Gorilla Tag/CosmeticExporter/Exports.
GLB files include static model geometry, normals, UVs, vertex colors, and
base-color textures embedded as PNG, including native texture-array slices.
Skinned meshes export in their asset pose. Unity custom materials are approximated
with standard glTF materials.

The browser lists the game's loaded V2 cosmetic catalog, including disabled entries.
An entry with unavailable model references or no model geometry reports an export
error. Models are loaded through the game's Addressables system, which may fetch
missing asset bundles.

Build and real Unity GPU export checks passed. In-game catalog loading and UI
still require a live check.
