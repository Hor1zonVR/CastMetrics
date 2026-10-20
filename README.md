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

`release.ps1` automatically finds a generated BepInEx installation under `%LOCALAPPDATA%\OrbusRebornModdingInstances` (**preferring `My Orbus Renewed` / Spades, then `Modded Orbus` / Community, then another valid instance; no selection prompt**), verifies dependencies, compiles `CastMetrics.dll` locally, commits pending source changes, pushes `main`, and publishes a **stable GitHub Release**. RebornManager then finds `CastMetrics.dll` from its approved live catalogue. **A stable release is public to every RM user**, so use a separate modded test instance for gameplay tests.

The script automatically bumps the patch version (e.g. `v0.1.4` → `v0.1.5`) only when an existing version is already tagged **and there are source changes**. It deliberately stops if the local branch has diverged from GitHub or an expected dependency is missing; it never force-pushes.

To override the automatic Spades reference selection, pass another folder for compiler references:

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

- **Community:** counts `MagicalActual.Runemage.prepareSpellCast(int)` instead of every `finishSpellCast()` drawing attempt. Needs in-game validation.
- **Spades:** counts `cgk.lsk(int)` (mapped from Community `Runemage.prepareSpellCast(int)`), not `cgk.lsa()` (mapped from `finishSpellCast()`). The v0.1.3 log showed more completed drawing attempts than preparations, confirming why earlier casts/s was inflated. Still experimental: live tests must confirm valid spell IDs match successful rune recognitions.
- **DPS:** **not implemented** until damage can be attributed reliably to the local player. If enabled, the HUD displays `DPS: pending attribution`, never invented damage numbers.
- **HUD:** a lightweight TextMesh attached to your local controller (or a named wand-tip child when available). It now follows in `LateUpdate` every frame, uses the actual VR eye instead of the desktop camera, and **never enumerates all scene transforms**. Billboard facing was inverted based on the supplied Spades recording; verify in VR.
- All observed events are read-only. The mod doesn't write gameplay state, change combat timings, or inject inputs.

## v0.1.4 recognized-spell filtering

Version 0.1.3 **overcounted**, because `cgk.lsa()` fires for drawing attempts even when the spell fails recognition. Its logs showed 41 drawing finishes but only 23 spell preparations in one ten-second interval, 36 vs 22 in another, and 29 vs 21 in another.

v0.1.4 instead counts the local Runemage's `prepareSpellCast(int)` event (`cgk.lsk(int)` on Spades), with negative or verified Community `spellIdx.NONE` value 99 excluded. `finishSpellCast()`/`cgk.lsa()` remains an **observational counter only**. The logs show a bounded histogram of observed spell IDs, plus cancelled and cast-prepared events.

This means casts/s is now designed to represent **successful spell recognition**, not every drawing gesture. It is not a measurement of projectile hits, server confirmation or actual DPS. Confirm in-game that invalid rune drawings do not increase the number, and valid rune drawings do. If a specific spell is skipped, share its name and the ID histogram.

## v0.1.3 casting investigation

The previous probes targeted **the wrong Spades classes**, explaining why every result stayed at zero. Comparing the supplied interop reveals that `cgk` is the actual Spades counterpart of Community `MagicalActual.Runemage`. Version 0.1.3 installs a **read-only experimental postfix** on `cgk.lsa()` (aligned with Community `Runemage.finishSpellCast()` by its position immediately preceding `getRelativePosition`). Read-only, bounded probes on related methods now report call totals and local-player ownership. **Treat all cast values as experimental until in-game confirmation**.

## v0.1.2 test change

The v0.1.1 recording showed the HUD stayed visible while the wand was holstered because it was anchored to the always-tracked right controller. In v0.1.3 the HUD is **hidden by default unless a local named wand/weapon object is detected near the right hand**. The local-rig scan is limited to 1,800 transforms every 1.5 seconds and does not enumerate the world.

**Important:** if Spades doesn't give its actual wand a recognizable object name under the local rig, the HUD will remain hidden even while the wand is equipped. That's intentional to avoid false visibility. The log lines `Local-rig wand-name scan` and `Right-controller children` help identify the real object during the next equipped/unequipped test. `PreviewAtHeadsetWithoutWand=true` explicitly overrides this for testing only.

Spades cast events are still **not verified**. Probe logs now distinguish *all calls* from *locally-owned calls* to reveal whether the correctly identified Runemage controller executes relevant methods but fails the ownership check. Accurate CPS and DPS require verified hooks.

## In-game test checklist

1. Install the **stable** GitHub release through RM's **Add mods → Quality of Life → CastMetrics**.
2. Enter either game edition, confirm there is no crash, then inspect `BepInEx\LogOutput.log` for `[CastMetrics]`.
3. If the wand display isn't visible, set `[Diagnostics] PreviewAtHeadsetWithoutWand = true` in the config and restart to test text rendering independently.
4. On Community, cast 10 fireballs over about five seconds; compare the displayed event rate to what you actually cast.
5. On Spades, deliberately draw 10 failed/incomplete runes and then 10 successful fireballs. Collect `Recognized-spell probe last 10s` and `First LOCAL recognized spell` lines to check that only recognized spells increase casts/s.
6. Test rejoining, switching scenes and whether it ever follows somebody else's wand.

Please share logs and screenshots to help validate the cast hook and improve wand attachment before treating the counters as accurate.

## GitHub Actions (optional; not required for local publication)

The separate workflow `Publish CastMetrics` can compile and publish releases on GitHub-hosted Windows runners after one-time private reference configuration:

- Repository Actions variable `CASTMETRICS_BUILD_REFS_REPO`: your existing private build-reference repo
- Repository Actions secret `CASTMETRICS_BUILD_REFS_TOKEN`: fine-grained read token
- Existing `refs-v1` release with `RebornCamBuildRefs.zip`, including `0Harmony.dll` and TextRendering

Do not commit generated Orbus interop, BepInEx game assemblies or private build references to the public repository.
