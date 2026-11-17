# Unity export checks

Use Unity 6000.2.9f1 (or a compatible editor) with graphics available.

1. Create a temporary project at `tests/UnityProject` using the Built-in renderer.
2. Copy `GlbExporter.cs`, `MeshData.cs`, and `tests/ExportChecks.cs` into its `Assets/Editor` folder.
3. Copy `tests/Fixture.shader` into `Assets`.
4. Reference `Valve.Newtonsoft.Json.dll` from your local game installation in `Assets/Plugins`.
5. Ensure the image conversion module is enabled.
6. Run the static editor method `ExportChecks.Run`, or use `-batchmode -executeMethod ExportChecks.Run -quit -logFile checks.log` with the editor executable. Do not use `-nographics`: the tests exercise GPU readback.

Success logs `COSMETIC_GLB_CHECKS_PASSED`. Generated fixtures are written to `tests/Results`. Both generated folders are ignored by Git.
