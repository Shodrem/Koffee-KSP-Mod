# Koffee para Kerbal Space Program

Un proyecto de shodrem (SHO Aerospace), creado por afición, para aprender y aportar a la comunidad de KSP.

[Quick guide (English): installation, Koffee, research, Komfort and Deadly Mode](https://github.com/Shodrem/Koffee-KSP-Mod/wiki).

## Licencias

- **Código, scripts y configuraciones originales: MIT.** Se permite su uso, modificación y redistribución, incluido el uso comercial, conservando el aviso de autoría y la licencia.
- **Gráficos, modelos, texturas y diseños visuales originales: CC BY-NC 4.0.** Se permite compartirlos y modificarlos con atribución, para fines no comerciales.
- Los componentes de terceros conservan sus propias licencias.

Consulta el [alcance y los créditos](LICENSE.md), el [texto de MIT](LICENSE-MIT.txt) y el [texto de CC BY-NC 4.0](LICENSE-CC-BY-NC-4.0.txt). Ambos textos incluyen sus respectivas cláusulas sobre garantías y responsabilidad.

Las licencias y los créditos también se incluyen en `GameData/ShoAerospace` para acompañar a la distribución del mod.

## Repositorio y código fuente

Origen del proyecto: [Shodrem/Koffee-KSP-Mod](https://github.com/Shodrem/Koffee-KSP-Mod).

Este repositorio publica los fuentes de producción, las configuraciones y los datos de integración en texto para revisar el código y compilar las DLL. Consulta las [instrucciones de compilación](BUILDING.md).

Para jugar, descarga el ZIP instalable de [Releases](https://github.com/Shodrem/Koffee-KSP-Mod/releases): contiene las DLL compiladas, los modelos, las texturas y las configuraciones necesarias dentro de `GameData`, junto con sus licencias. Los archivos fuente, Blender, FBX, pruebas y herramientas de desarrollo quedan fuera del paquete instalable.

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

La release v0.2.0 amplía las conversaciones de la tripulación y corrige los salarios de turistas. Conserva las piezas, mejoras de servicios, Komfort, Deadly Mode y el arreglo BRP de v0.1.9. Consulta las [notas de la versión](RELEASE-v0.2.0.md) para instalación y límites de validación.

Las dependencias externas y sus licencias se detallan en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
