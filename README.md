# CastMetrics

An experimental **client-side** BepInEx IL2CPP quality-of-life mod for OrbusVR Community Edition and Spades Edition. Goal: show Runemage casts per second and locally attributed DPS in compact floating text above your wand.

## v0.1.0 scope and honesty

- One DLL designed to load on both clients, using runtime type detection rather than a compile-time reference to either edition's Assembly-CSharp.
- Community: read-only Harmony postfix on `MagicalActual.Runemage.finishSpellCast()`, **not yet tested in a live character session**. Counts should be treated as diagnostic until validated as the local character's completed casts.
- Spades: class names are obfuscated; automatic cast counting is **not yet enabled**. The plugin logs a bounded method list from candidate `bnv` for investigation. You can provide an explicitly verified type and method in the config for an experimental hook, but do not guess.
- DPS: deliberately **not implemented** until reliable damage ownership is identified. The optional DPS line says `pending attribution`, never an invented number.
- TextMesh world-space HUD, attached to a nearby named wand tip or the hook's observed wand transform. Wand binding and VR legibility need in-game verification.
- Readout can be hidden, offset and resized in `BepInEx/config/com.horizon.castmetrics.cfg`.
- With `Diagnostics.PreviewAtHeadsetWithoutWand=true`, show the text 65 cm in front of the headset for rendering checks when wand-tip detection fails. OFF by default.

## Build locally

Install .NET 8 SDK. Install BepInEx IL2CPP into a separate Orbus instance and launch it once to generate interop.

```powershell
dotnet build src/CastMetrics/CastMetrics.csproj -c Release "-p:GameDir=C:\path\to\Modded Orbus"
```

Output: `src/CastMetrics/bin/Release/net6.0/CastMetrics.dll`.

Do **not** commit generated game assemblies or private build refs to this public repository.

## GitHub releases / RebornManager

The Actions workflow **Publish CastMetrics** offers `preview` (experimental, not offered by RebornManager) and `stable` (offered by RebornManager after testing). It publishes **one** asset named `CastMetrics.dll` for both editions.

One-time GitHub repository configuration, matching RebornCam and QuestHelper:

1. Repository **Settings → Secrets and variables → Actions → Variables**: `CASTMETRICS_BUILD_REFS_REPO` = the existing private build-references repo (`owner/name`).
2. **Actions → Secrets**: `CASTMETRICS_BUILD_REFS_TOKEN` = a fine-grained token with read access to that private repo.
3. Workflow uses the existing `refs-v1` release asset `RebornCamBuildRefs.zip`. Ensure it includes `0Harmony.dll` and Unity TextRendering interop, or update the private reference bundle.

Click **Actions → Publish CastMetrics → Run workflow → preview** first. After a successful real-game test on both editions, rerun on the **same commit**, choose `stable`; the workflow promotes that preview so RebornManager can install it. Changes after a tagged preview require a version bump.

## What to test

1. Install through the RM catalogue **after a stable release** (or import the preview DLL locally).
2. Launch each edition separately and look for `[CastMetrics]` in `BepInEx/LogOutput.log`.
3. Test `Diagnostics.PreviewAtHeadsetWithoutWand=true` if the text cannot attach above the wand.
4. Cast 10 fireballs over about five seconds; note whether the rate increments only for your spells.
5. Test switching worlds / character select and record any HUD disappearance or accidental attachment to someone else's wand.

Please share logs and observations before treating Community casts/s or Spades data as verified.
