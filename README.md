# CastMetrics

Experimental client-side BepInEx IL2CPP quality-of-life mod for **OrbusVR Community Edition and Spades Edition**. Goal: show Runemage casts per second and locally attributed DPS as compact floating text above the wand.

## Local Windows setup: GitHub -> RebornManager

This follows the same **local build / GitHub Release** approach as RebornCam and QuestHelper. The local modded game is read **only for compiler references**; no DLL is installed there by the script.

**Clone once** (PowerShell):

```powershell
$workspace = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'OrbusMods'
New-Item -ItemType Directory -Force -Path $workspace | Out-Null
Set-Location $workspace
git clone https://github.com/Hor1zonVR/CastMetrics.git
Set-Location .\CastMetrics
```

**On each update** (PowerShell while inside the cloned CastMetrics folder):

```powershell
git pull
.\release.ps1
```

`release.ps1` automatically finds a generated BepInEx installation under `%LOCALAPPDATA%\OrbusRebornModdingInstances` (preferring a Community client for compile references), verifies dependencies, compiles `CastMetrics.dll` locally, commits pending source changes, pushes `main`, and publishes a **stable GitHub Release**. RebornManager then finds `CastMetrics.dll` from its approved live catalogue. **A stable release is public to every RM user**, so use a separate modded test instance for gameplay tests.

The script automatically bumps the patch version (e.g. `v0.1.2` → `v0.1.2`) only when an existing version is already tagged **and there are source changes**. It deliberately stops if the local branch has diverged from GitHub or an expected dependency is missing; it never force-pushes.

If you have several game installations, pass the exact folder for compiler references:

```powershell
.\release.ps1 -GameDir "$env:LOCALAPPDATA\OrbusRebornModdingInstances\Modded Orbus"
```

For a *GitHub-only* prerelease not shown in RM, use `.\release.ps1 -Preview`. To see the mod in RM, publish a stable release. The GitHub Actions workflow is an **optional** alternative and needs private build-reference settings; it is **not** needed for local `release.ps1`.

The prerequisites are Windows, .NET 8 SDK, Git, GitHub CLI (`gh auth login` once), plus a modded game copy with generated BepInEx IL2CPP interop.

## Configuring the floating readout

After installing through RM and launching the game once, BepInEx writes:

`<your-game-instance>\BepInEx\config\com.horizon.castmetrics.cfg`

You can edit that file **with the game closed**, then relaunch. Current v0.1 settings include:

- `[General] Enabled = true` — master switch
- `[Display] ShowCastsPerSecond = true` — casts/s readout
- `[Display] ShowDps = false` — reserved until local damage attribution is verified
- `[Display] Scale = 1.0` — font size multiplier (0.25–3)
- `[Position] RightMeters = 0`, `UpMeters = 0.13`, `ForwardMeters = 0` — readout offset from wand tip (metres)
- `[Display] HideWhenIdle = false` and `IdleSeconds = 8` — optional automatic hiding
- `[Metrics] RollingWindowSeconds = 5` — smoothing / responsiveness
- `[Diagnostics] PreviewAtHeadsetWithoutWand = false` — test whether text renders if the wand transform isn't found

A native in-VR settings interface with visibility toggles, scale and position adjustment is **planned but not implemented**. The CFG is the working v0.1 configuration method.

## Experimental implementation status

- **Community:** read-only Harmony postfix on `MagicalActual.Runemage.finishSpellCast()`. This is an *event probe* until in-game validation confirms local-player ownership and completed-cast semantics.
- **Spades:** the legacy `cec.lkk()` posthook is an **experimental structural match** to Community `PlayerWand.finishSpellCast()`; it may not be used by the current Spades Runemage. The newer `bnv` controller uses read-only counters on eight selected candidate methods; **none of those candidates are counted as spells** before validation. Logs show whether anything fires during real casts.
- **DPS:** **not implemented** until damage can be attributed reliably to the local player. If enabled, the HUD displays `DPS: pending attribution`, never invented damage numbers.
- **HUD:** a lightweight TextMesh attached to your local controller (or a named wand-tip child when available). It now follows in `LateUpdate` every frame, uses the actual VR eye instead of the desktop camera, and **never enumerates all scene transforms**. Billboard facing was inverted based on the supplied Spades recording; verify in VR.
- All observed events are read-only. The mod doesn't write gameplay state, change combat timings, or inject inputs.

## v0.1.2 test change

The v0.1.1 recording showed the HUD stayed visible while the wand was holstered because it was anchored to the always-tracked right controller. In v0.1.2 the HUD is **hidden by default unless a local named wand/weapon object is detected near the right hand**. The local-rig scan is limited to 1,800 transforms every 1.5 seconds and does not enumerate the world.

**Important:** if Spades doesn't give its actual wand a recognizable object name under the local rig, the HUD will remain hidden even while the wand is equipped. That's intentional to avoid false visibility. The log lines `Local-rig wand-name scan` and `Right-controller children` help identify the real object during the next equipped/unequipped test. `PreviewAtHeadsetWithoutWand=true` explicitly overrides this for testing only.

Spades cast events are still **not verified**. Probe logs now distinguish *all calls* from *locally-owned calls* to reveal whether the candidate methods execute but fail the ownership check. Accurate CPS and DPS require verified hooks.

## In-game test checklist

1. Install the **stable** GitHub release through RM's **Add mods → Quality of Life → CastMetrics**.
2. Enter either game edition, confirm there is no crash, then inspect `BepInEx\LogOutput.log` for `[CastMetrics]`.
3. If the wand display isn't visible, set `[Diagnostics] PreviewAtHeadsetWithoutWand = true` in the config and restart to test text rendering independently.
4. On Community, cast 10 fireballs over about five seconds; compare the displayed event rate to what you actually cast.
5. On Spades, cast repeatedly for 20 seconds, then collect `Spades bnv probe hits` and `First LOCAL cast callback` (if any) from the log to validate the obfuscated casting path.
6. Test rejoining, switching scenes and whether it ever follows somebody else's wand.

Please share logs and screenshots to help validate the cast hook and improve wand attachment before treating the counters as accurate.

## GitHub Actions (optional; not required for local publication)

The separate workflow `Publish CastMetrics` can compile and publish releases on GitHub-hosted Windows runners after one-time private reference configuration:

- Repository Actions variable `CASTMETRICS_BUILD_REFS_REPO`: your existing private build-reference repo
- Repository Actions secret `CASTMETRICS_BUILD_REFS_TOKEN`: fine-grained read token
- Existing `refs-v1` release with `RebornCamBuildRefs.zip`, including `0Harmony.dll` and TextRendering

Do not commit generated Orbus interop, BepInEx game assemblies or private build references to the public repository.
