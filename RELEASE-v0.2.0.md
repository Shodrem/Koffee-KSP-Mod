Koffee v0.2.0 for Kerbal Space Program 1.12.5.

## Changes

- More short crew conversations for experiment starts and completion, coffee consumption and usable supplies running out, preventive maintenance, repairs and emergency-mode changes. The dialogue table now has 57 bilingual alternatives; existing text, line identities and playback memory are preserved.
- Notices follow confirmed gameplay actions, identify the actual drinker or engineer and use conservative shared limits. Maintenance completion can speak through the active EVA engineer. Experiment completion still uses the stock science event once.
- Tourists and other non-professional kerbals are excluded from salary accrual and display; stale tourist salary entries are discarded.
- Keeps the v0.1.9 BRP air-warning fix, service upgrades, parts, models, textures and English KSPedia.

## Mod compatibility

- ModuleManager 4.2.3 — required configuration patches.
- HarmonyKSP 2.2.1.0 — required runtime hooks.
- Background Resource Processing 0.3.4 — unloaded-vessel resource and electrical accounting.
- Dynamic Battery Storage 2.3.7.0 — electrical accounting integration.
- Contract Configurator 2.13.4.0 — Koffee contracts.
- SystemHeat 0.9.1 — climate and emergency-support heat.
- Near Future Electrical 2.0.1 — emergency fission backend with SystemHeat.
- Lonapse 1.0.0 — bilingual crew dialogue.
- Station Parts Expansion Redux 2.0.11 — habitat/lab Komfort and coffee storage.
- B9 Part Switch 2.21.0.4 — SSPX coffee storage subtype.
- Planetside Exploration Technologies 1.0.2 — habitat Komfort patches.
- Connected Living Space 2.0.2.0 — crew-part passage metadata.
- Community Tech Tree 3.4.5 — technology placement and upgrades.
- Community Category Kit 112.0.1 — Koffee part category.
- ReStock 1.5.1 — crew parachute sizing with RealChute.
- RealChute 1.4.9.5 — ReStock crew parachute sizing patch.
- Parallax Continued 1.0.6 — STS-25 scaled-body resource integration.

ModuleManager and HarmonyKSP are required; the other integrations are optional. These are the current inspected compatibility targets, not promises for every newer version or every mod combination. Automated checks cover the relevant code/API and configuration paths; there is no new in-game validation of the full mod list. Install external mods separately; none is bundled.

## Installation

Close KSP. Remove the previous `GameData/ShoAerospace/Koffee` folder, then copy this ZIP's `GameData` into KSP and replace `GameData/ShoAerospace/Lonapse/Koffee.tsv`. Keep your saves and third-party mods. Existing saves and vessels remain usable. Install only one KSPedia edition; the older optional Spanish guide is not updated.

Download **Koffee-v0.2.0.zip** and its `.zip.sha256` checksum. GitHub's automatic source archives are for developers.

## Validation and limits

The three Koffee DLLs are rebuilt from the published source snapshot. 884 gameplay checks and 14 dialogue checks pass against these release sources. The official Lonapse 1.0.0 reader accepts all 95 rows; native BRP, DBS, SystemHeat and ModuleManager checks also pass. ZIP contents and hashes are verified before publication. The exhaustive long-running research matrix and a new in-game playtest are not claimed. Komfort change announcements await a native classification with hysteresis; emergency-exit wording confirms the mode switch, not full recovery of every service.

Original code/configuration: MIT. Original art/models/textures: CC BY-NC 4.0. Author: shodrem (SHO Aerospace).
