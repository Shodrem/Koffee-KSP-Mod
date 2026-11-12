Koffee v0.1.8 for Kerbal Space Program 1.12.5.

## Recovery salary breakdown

- The stock recovery report's **Crew** tab now shows each professional Kerbal's charged days, fixed daily rate and calculated mission salary below their crew card, followed by the total crew salary deduction. Stock role, status, experience and level information stays visible.
- If currency modifiers or the stock zero-funds limit change the effective deduction, the report shows that amount separately from the calculated salary.
- The **Parts / funds** view shows recovered funds, the salary deduction and the net recovery amount. The stock summary uses the net amount and the final balance, including when recovering an EVA with no recoverable parts.
- Salaries settle after stock recovery processing. Repeated or reentrant recovery callbacks cannot charge the same crew twice; unrelated transactions triggered by currency listeners are excluded from the salary receipt.

Existing salary rates, started-day rounding, pauses, tourists and mission eligibility keep the v0.1.7 rules. Quick recovery still pays without opening a report. No salary history is reconstructed for past recoveries, and this update does not change the save format. New report text is localized in English and Spanish.

## Installation

Close KSP, remove the previous `GameData/ShoAerospace/Koffee` folder, then copy the ZIP's `GameData` into your KSP directory. Keep your saves. Install ModuleManager and HarmonyKSP separately. BRP 0.3.4, DBS, Contract Configurator, SystemHeat, NFE and Lonapse remain optional; contracts require Contract Configurator. Later BRP versions have not been tested. Install only one KSPedia edition; the older optional Spanish guide is not updated for this release.

Download **Koffee-v0.1.8.zip** and its `.sha256` checksum. GitHub's automatic source archives are for developers.

## Validation

The release is built from a clean copy of the public repository with only the recovery salary change. Targeted salary checks and real-KSP-DLL hook checks verify the receipts, modifiers, insufficient funds, payment order and reentry safeguards. All builds finish with zero compiler warnings/errors. Checks against these public production sources pass: 27 salary, 35 settings, 176 core and 43 BRP host checks (counts overlap), plus the real-DLL salary UI/currency hooks and BRP API/IL probe. No new visual acceptance test inside KSP has been performed.

Models, textures, the 24-page English KSPedia and existing configurations are carried over from the verified v0.1.7 package, apart from the added salary localization keys. The KSPedia bundle is unchanged in this patch.

Original code/configuration: MIT. Original art/models/textures: CC BY-NC 4.0. Author: shodrem (SHO Aerospace).
