Koffee v0.1.6 for Kerbal Space Program 1.12.5.

Recompiled `Koffee.dll` and `Koffee.BRP.dll` from the public production sources against the installed Background Resource Processing (BRP) 0.3.4.

**Optional integration: [Background Resource Processing 0.3.4](https://github.com/Phantomical/BackgroundResourceProcessing). It should work with later versions, but those versions have not been tested.** BRP remains optional and is installed separately, together with its own dependencies. It is not bundled with Koffee. Without BRP, Koffee retains its stock background electricity path.

This release preserves the gameplay code, configurations, models, textures, KSPedia and contract adapter from v0.1.5. It updates the two rebuilt DLLs and installation instructions. The source README and build instructions now specify optional BRP 0.3.4 and the expected compatibility with later versions.

**Installation:** close KSP. For a full upgrade, remove the previous `GameData/ShoAerospace/Koffee` folder, then copy the ZIP's `GameData` into your KSP directory. Keep your saves. If v0.1.5 is already installed, replacing only `Koffee.dll` and `Koffee.BRP.dll` in `GameData/ShoAerospace/Koffee/Plugins` is sufficient for this update. Install ModuleManager and HarmonyKSP separately. DBS, Contract Configurator, SystemHeat, NFE and Lonapse also remain optional; contracts require Contract Configurator.

**Download:** use `Koffee-v0.1.6.zip` and its `.sha256` checksum. GitHub's automatic source archives are for developers.

**Validation:** Release rebuild against KSP 1.12.5 and the real BRP 0.3.4 DLL, with zero compiler warnings/errors. Targeted host checks pass: 43 BRP integration, 28 BRP native identity and 49 SystemHeat checks (counts overlap). The real-DLL probe verifies API compatibility, missing/duplicate assembly rejection, loaded-vessel guard, active-time bookkeeping, core isolation from BRP and the adapter's 0.3.4 compilation reference. Harmony checks against the real BRP logging methods pass. ZIP CRC, all entry contents and SHA-256 are verified. No new in-game acceptance test or future BRP version test was performed.

Code/configuration: MIT. Original art/models/textures: CC BY-NC 4.0. Author: shodrem (SHO Aerospace).
