# Simitone → Android (SM-T550) porting notes

Working notes for an in-progress port of **The Sims 1 base game** to a Samsung Galaxy Tab A 9.7
(SM-T550) via Simitone/FreeSO. Not upstream material — the DesktopGL head and the FreeSO
neighbourhood fix are, this file isn't.

Last updated: 2026-08-29.

## Goal and the constraint that decides it

Run TS1 **base game only** (no expansions) on an SM-T550: Android 7.1.1 / API 25, APQ8016
(Snapdragon 410), 4x Cortex-A53 @ 1.19 GHz, **armeabi-v7a (32-bit)**, **1.46 GB RAM**.

32-bit ARM and Android 7.1 are **not** blockers for a native build: `android-arm` is a supported
.NET 9 RID and .NET 9's floor is API 21. (They do kill the Winlator/Wine route, which needs
arm64 + Android 8+. Don't confuse the two.)

**RAM is the only thing that decides this.** A foreground app on that device realistically gets
500–700 MB. The go/no-go gate is: run the desktop build, load a lot, measure working set.
Under ~500 MB, proceed to the Android head. Over ~800 MB, it's dead on this hardware. Desktop
arm64 is a roughly conservative proxy — 64-bit pointers and the desktop GC inflate it relative
to 32-bit Mono on device.

**The gate has not been passed yet.** See "Where this stopped".

## What exists

**`Client/Simitone/Simitone.DesktopGL/`** — a cross-platform OpenGL head, because the only
executable head upstream is `Simitone.Windows` (`net9.0-windows` + WinForms), which cannot build
on macOS or Linux. Derived from `Simitone.Windows/Program.cs` minus the WinForms dialogs, the
DirectX path, and the `MonogameLinker` assembly shuffle. It drops the `FSO.IDE` reference
(also `net9.0-windows` + WinForms), so there is no `-ide` / Volcanic flag here.

It links `ILocator` / `MacOSLocator` / `LinuxLocator` straight out of `Simitone.Windows` rather
than duplicating them; `WindowsLocator` and `SteamGameFinder` stay out (registry + kernel32
P/Invoke). Builds and runs on macOS 26 / Apple M4.

**FreeSO submodule, branch `simitone-basegame-only`, commit `9b5cb9e9`** — stops
`TS1NeighborhoodProvider.InitSpecific` requiring `Houses/STDesc.iff` and `Houses/MTDesc.iff`.
Those are Studio Town (Superstar) and Magic Town (Makin' Magic); no base-game install has
either, but both were loaded unconditionally, so *any* base-game-only install crashed there.
They're only read for house IDs 80+, which base content never produces. Required files were
deliberately left throwing — turning a missing required file into silent breakage is worse than
a crash. This one is genuine upstream-PR material.

## Building and running

```sh
export DOTNET_ROOT=$HOME/.dotnet          # SDK 9.0.317 lives here, not on PATH
export PATH=$HOME/.dotnet:$PATH

# Build the csproj, NOT the solution: Simitone.Windows and FSO.IDE are net9.0-windows
# and fail on macOS without EnableWindowsTargeting. Pre-existing, not caused by us.
dotnet build Client/Simitone/Simitone.DesktopGL/Simitone.DesktopGL.csproj

cd Client/Simitone/Simitone.DesktopGL/bin/Debug/net9.0
./Simitone -path"$HOME/Work/TheSims"
```

## Where this stopped

Game assets. The engine launches, reaches `SimitoneGame.Initialize()`, and dies on a missing
base-game file.

Every hardcoded path built from `TS1BasePath` is loaded with a bare `new IffFile(path)` and no
existence check, so each missing file throws in turn. Known to be needed and currently absent:

- `GameData/UIText.iff` — all UI strings (`FSO.UI/ContentStrings.cs:122`)
- `GameData/walls.iff` (`WorldWallProvider.cs:182`)
- `GameData/Build.iff` (`WorldWallProvider.cs:185` **and** `WorldFloorProvider.cs:113`)
- `GameData/floors.iff` (`WorldFloorProvider.cs:108`)

**Do not curate a file list.** Three rounds of folder-level specs were each incomplete. The
engine scans its base path recursively (`Content.cs:263`, `_ScanFiles`) and indexes whatever it
finds. Extract the whole game install; don't hand it a subset.

## Gotchas worth knowing before you touch anything

**Pixel order.** `ImageLoaderHelpers.BitmapFunction` must return **RGBA** (MonoGame's
`SurfaceFormat.Color`). The Windows head arrives there by accident: `RGBToBGR()` swaps R and B,
then `LockBits(Format32bppArgb)` reads back BGRA, and the two swaps cancel. The DesktopGL head
uses `StbImageSharp` with `ColorComponents.RedGreenBlueAlpha` to get it directly. Don't "fix"
this by adding a swap.

**stb_image vs System.Drawing.** stb rejects RLE-compressed BMPs where System.Drawing decodes
them. Unverified whether TS1 ships any. If textures come up missing once assets are complete,
suspect this first — it fails *silently* (`BitmapFunction` returns null → "bad bitmap" → missing
texture, no crash).

**Stale user-data copy.** `InitSpecific` copies `UserData/` into `~/Documents/Simitone/` on
first run and re-copies only if that directory is absent. A run against incomplete assets plants
a partial copy that survives later fixes. Delete `~/Documents/Simitone/UserData*` after
completing the game files.

**Skins are not loose files.** `GameData/Skins/` being empty is normal — that folder is for
add-on skins. Base meshes and textures live in the FAR archives: `Animation.far` holds 1873
`.cmx`, 221 `.bmf`, 1060 `.cfp`, 662 `.bcf`; `Textures.far` holds 335 `.bmp8`. `TS1Provider`
globs `.*\.far` recursively and picks them up.

**Base-game-only is a supported shape.** `TS1Provider` globs whatever exists, so a missing
`ExpansionShared/` yields no matches rather than an error. `ExpansionShared` appears in exactly
one file across FreeSO and Simitone has zero expansion references. It is, however, the
least-tested path — `9b5cb9e9` is the first bug it turned up and probably not the last.

## Next steps

1. Complete the game assets, then rerun the command above and **measure peak working set with a
   lot loaded**. That is the gate; everything below is conditional on passing it.
2. If under budget, scaffold `Simitone.Android`: `net9.0-android`, RID `android-arm`,
   `MonoGame.Framework.Android` (3.8.5.1 on NuGet vs 3.8.4 in tree). Needs
   `dotnet workload install android`, the Android SDK, and a real JDK — `java` on the dev Mac is
   the Apple stub with no runtime behind it.
3. `TSOClient/FSODroid` in FreeSO is a legacy Xamarin.Android project (ToolsVersion 4.0,
   `TargetFrameworkVersion v8.1`, ABIs `armeabi-v7a;x86`, no arm64). Xamarin hit EOL in May 2024,
   so it will not build on current tooling. **Reference material only — do not try to revive it.**
4. Keep linking/trimming **off** for early Android builds. The UI scripting layer is heavily
   reflection-based (a wall of IL2075/IL2087 warnings); trimming will strip types it resolves at
   runtime. Costs APK size, but correctness first.

## Touch input already exists

Simitone was built for mobile, so the mouse-to-touch work is largely done already.
`UpdateState.TouchMode` is set in `tso.common/Rendering/Framework/GameScreen.cs:183` and
synthesises mouse state from touch, backed by `UILotControlTouchHelper.cs`,
`UIArchTouchHelper.cs`, `UITouchScroll.cs`, `UITouchListbox.cs` and purpose-drawn art in
`Simitone.Client/Content/uigraphics/live/touch/`. The 1024x768 panel is also a good fit for a UI
designed at 800x600.

## Licensing

FreeSO is MPL-2.0. **Simitone has no license file** (all rights reserved), so the clean route is
pull requests upstream rather than maintaining a distributed fork — which is how the .NET 9
migration (PR #55) landed. EA's cease-and-desist against Simitone targeted App Store
*distribution*, not personal builds.

Game assets must come from a copy you own. None are in this repository and none should be.
