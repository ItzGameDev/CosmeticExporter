# Cosmetic Exporter

Standalone BepInEx mod for Gorilla Tag. Browse the loaded cosmetic catalog, search by name or ID, and export a selected model to a GLB file with embedded textures.

## Use

Copy `CosmeticExporter.dll` into `Gorilla Tag/BepInEx/plugins`. The window opens on startup; press **F6** to toggle it. Drag the title bar to move it; Escape or X closes it.

Exports appear in `Gorilla Tag/CosmeticExporter/Exports`.

## Build

Requires a .NET SDK, a local Gorilla Tag installation, and BepInEx 5 references.

```powershell
dotnet build CosmeticExporter.csproj -c Release -p:GamePath="C:/Program Files (x86)/Steam/steamapps/common/Gorilla Tag"
```

If BepInEx references are elsewhere, add `-p:BepInExCorePath="C:/path/to/BepInEx/core"`.

The result is `bin/Release/netstandard2.1/CosmeticExporter.dll`. Game assemblies are local build references and are not included in this repository. This project is independent of Taurus.

## Export details

GLB output includes static geometry, normals, UVs, vertex colors, and PNG base-color textures, including texture-array slices. Skinned meshes export in their asset pose. Unity custom shaders are approximated with glTF materials; animations and skeletons are not exported.

The browser lists the loaded V2 catalog, including disabled entries. Entries without available model references or geometry report an error. Addressables may download missing bundles.

## Validation

Build and Unity GPU export checks passed. In-game catalog loading and UI still need a live check.

The `tests` folder contains Unity Editor fixtures covering readable and GPU-only meshes, texture-array slices, embedded PNGs, coordinate conversion, triangle winding, UVs, vertex colors, and GLB framing. See `tests/README.md` to run them.
