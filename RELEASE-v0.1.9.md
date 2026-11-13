Koffee v0.1.9 for Kerbal Space Program 1.12.5.

## Fix: false insufficient-air-filtration warning with BRP

Some players using Background Resource Processing (BRP) could receive an insufficient air filtration popup after leaving an orbital vessel for the Space Center, even with a working, powered filter. Returning to the vessel and then to the Space Center could make the warning disappear.

- Koffee now waits for BRP to provide an actual operating rate after loading or resetting scene continuity. An uninitialized rate is no longer treated as an electrical failure. The unobserved interval does not consume air or filter lifetime.
- A queued insufficient-filtration popup is discarded if filtration has already recovered before it is displayed. Genuine shortages, critical health and death warnings remain active.

This fixes Koffee's BRP adapter; it does not require an update to BRP itself. The save format is unchanged. **Existing saves and vessels can be used: no new career or launch is required.** The fix does not reconstruct or undo resource, air, filter-life or health changes already saved by an older version.

The original reporter confirmed that the local fix appears to resolve the problem with the existing orbital vessel. Automated checks cover initialization, deserialization, saved pending warnings, restored filtration and genuine zero-power operation.

## Installation

Close KSP, remove the previous `GameData/ShoAerospace/Koffee` folder, then copy the ZIP's `GameData` into your KSP directory. Keep your saves. Install ModuleManager and HarmonyKSP separately. BRP 0.3.4, DBS, Contract Configurator, SystemHeat, NFE and Lonapse remain optional; contracts require Contract Configurator. Later BRP versions have not been tested. Install only one KSPedia edition; the older optional Spanish guide is not updated for this release.

Download **Koffee-v0.1.9.zip** and its `.sha256` checksum. GitHub's automatic source archives are for developers.

## Validation

The release is prepared from the public v0.1.8 sources with only the two production files needed for this fix. Checks against these exact public production sources pass: 63 Deadly, 70 Komfort and 43 BRP host checks (counts overlap). Production builds and the installed-BRP API/IL probe are also verified before publication. The 24-page English KSPedia, models, textures and configurations retain their v0.1.8 bytes. No unrelated development changes are included.

Original code/configuration: MIT. Original art/models/textures: CC BY-NC 4.0. Author: shodrem (SHO Aerospace).
