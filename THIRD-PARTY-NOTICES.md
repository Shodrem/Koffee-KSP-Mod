# Dependencias y componentes de terceros

Las licencias MIT y CC BY-NC 4.0 de Koffee se aplican al material original de su autor. Los componentes siguientes conservan sus propias licencias y atribuciones. Sus DLL no se incluyen en el repositorio de fuentes ni en el paquete instalable de Koffee; el jugador instala las dependencias por separado.

## Harmony 2.2.1

- Archivo: `GameData/000_Harmony/0Harmony.dll`.
- Autoría: Copyright (c) 2017 Andreas Pardeike.
- Proyecto original: <https://github.com/pardeike/Harmony>.
- Licencia: MIT; texto completo en [LICENSE-Harmony.txt](GameData/000_Harmony/LICENSE-Harmony.txt).
- Fuente del aviso y licencia: <https://github.com/pardeike/Harmony/blob/master/LICENSE>.

## HarmonyKSP 2.2.1

- Archivos de distribución y comprobador de instalación en `GameData/000_Harmony`, incluido `HarmonyInstallChecker.dll`.
- Autoría: Copyright (c) 2021 HarmonyKSP.
- Proyecto: <https://github.com/KSPModdingLibs/HarmonyKSP>.
- Licencia: MIT; texto completo en [LICENSE-HarmonyKSP.txt](GameData/000_Harmony/LICENSE-HarmonyKSP.txt).
- Fuente del aviso y licencia: <https://github.com/KSPModdingLibs/HarmonyKSP/blob/main/LICENSE>.

Se conserva también el `ReadMe.txt` original de la distribución.

## Dependencias externas

ModuleManager es una dependencia requerida para aplicar las configuraciones del mod: <https://github.com/sarbian/ModuleManager>.

BackgroundResourceProcessing (BRP) y Dynamic Battery Storage (DBS) son integraciones opcionales que instala el jugador. `Koffee.BRP.dll` es el adaptador propio de Koffee; no contiene ni distribuye BRP.

Kerbal Space Program, Unity, KSPBuildTools y las demás herramientas o integraciones externas pertenecen a sus respectivos titulares y conservan sus propios términos. Mencionarlos o enlazarlos no los incluye en las licencias de Koffee ni implica que se distribuyan con este repositorio.
