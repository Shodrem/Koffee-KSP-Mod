Koffee v0.1.7 for Kerbal Space Program 1.12.5.

Career missions now include persistent Kerbal salaries, enabled by default. The default daily rate is 6 funds plus 2 funds per departure level. A salary day is 6 Kerbin hours; every started day is charged in full, with zero elapsed time costing zero. The rate is fixed at launch and paid when the Kerbal is recovered on Kerbin, including quick recovery and EVA.

Salary accrues through transfers, docking, EVA, unloaded vessels and time warp. Settings offer a base rate of 0–16 in steps of 2 and an increase per level of 0–5. Disabling salaries pauses accrual; recovery while disabled clears pending pay without a charge. Tourists, Sandbox and Science mode are excluded. Kerbals already in flight without a salary record begin accruing only on a future launch.

Death forfeits that mission's salary and applies Koffee's additional penalty of 5% of the absolute current reputation, once per death. The Space Center crew details show pending pay, daily rate and departure level. The English KSPedia now has 24 pages, including the mission salaries reference page. The ES-375 reference now documents 10 EC/s generation, 9 EC/s internal consumption, 1 EC/s net, 100 kW of waste heat and the optional nuclear fuel endurance.

Preserves the v0.1.6 configurations, service power balance, models and textures, and optional BRP 0.3.4 integration. BRP should work with later versions, but those versions have not been tested. BRP and its dependencies are installed separately.

**Installation:** close KSP, remove the previous `GameData/ShoAerospace/Koffee` folder, then copy the ZIP's `GameData` into your KSP directory. Keep your saves. Install ModuleManager and HarmonyKSP separately. BRP, DBS, Contract Configurator, SystemHeat, NFE and Lonapse remain optional; contracts require Contract Configurator. The older optional Spanish KSPedia is not updated for this release; install only one guide edition.

**Download:** use `Koffee-v0.1.7.zip` and its `.sha256` checksum. GitHub's automatic source archives are for developers.

**Validation:** the core and both own adapters are rebuilt in Release against KSP 1.12.5 and the installed optional dependency DLLs. All builds finish with zero compiler warnings/errors. Checks against the public production sources pass: 21 salary, 35 settings, 176 core and 43 BRP host checks (counts overlap), plus the real-DLL BRP API/IL probe. The 24-page English KSPedia is updated for the current ES-375 power, fuel and heat specifications, rendered and rebuilt in Unity 2019.4.18f1. Native metadata, prefab instantiation, missing scripts, textures, text bounds, glyphs and current source hashes are checked against the public release sources. ZIP CRC, every entry's contents and SHA-256 are checked. No new in-game acceptance test was performed.

Code/configuration: MIT. Original art/models/textures: CC BY-NC 4.0. Author: shodrem (SHO Aerospace).
