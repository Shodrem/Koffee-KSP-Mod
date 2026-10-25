# Compilar Koffee desde los fuentes

El código del repositorio permite revisar y compilar las DLL distribuidas con el mod. Los modelos y las texturas del paquete instalable no son necesarios para compilar el código C#.

## Requisitos

- .NET SDK 10 para los proyectos de pruebas actuales y la compilación con `dotnet`.
- Una instalación propia de Kerbal Space Program 1.12.5, con sus ensamblados de KSP y Unity en la carpeta del juego.
- HarmonyKSP 2.2.1 instalado en esa copia de KSP: `GameData/000_Harmony/0Harmony.dll`. Proyecto: <https://github.com/KSPModdingLibs/HarmonyKSP>.
- Acceso a NuGet para restaurar los paquetes declarados en los proyectos, incluido KSPBuildTools 1.1.1. Este paquete obtiene las referencias de .NET Framework 4.8, KSP y Unity desde la instalación indicada.

Para compilar también el adaptador opcional `Koffee.BRP.dll`, instalar BackgroundResourceProcessing 0.3.2 en esa misma copia del juego. Proyecto: <https://github.com/Phantomical/BackgroundResourceProcessing>.

## Compilar el núcleo

Desde la raíz del repositorio, en PowerShell, sustituir la ruta de ejemplo por la carpeta que contiene `GameData` y el ejecutable de KSP:

```powershell
dotnet build src/Koffee/Koffee.csproj -c Release "-p:KSPBT_GameRoot=C:/Games/KSP" -p:KSPBT_CopyDLLsToPluginFolder=false -p:KSPBT_InstallCKANDependencies=false -p:KSPBT_GenerateVersionFile=false
```

Resultado: `src/Koffee/bin/Release/net48/Koffee.dll`.

Las opciones desactivan la copia automática de DLL al juego, la instalación de dependencias mediante CKAN y la generación automática de archivos de versión.

## Compilar el núcleo y el adaptador BRP

```powershell
dotnet build src/Koffee.BRP/Koffee.BRP.csproj -c Release "-p:KSPBT_GameRoot=C:/Games/KSP" -p:KSPBT_CopyDLLsToPluginFolder=false -p:KSPBT_InstallCKANDependencies=false -p:KSPBT_GenerateVersionFile=false
```

Resultados:

- `src/Koffee/bin/Release/net48/Koffee.dll`.
- `src/Koffee.BRP/bin/Release/net48/Koffee.BRP.dll`.

Para utilizar las DLL compiladas, colocarlas en `GameData/ShoAerospace/Koffee/Plugins` de una instalación del mod que ya tenga los modelos, las texturas y las configuraciones de la release correspondiente.

## Pruebas y verificación

Los proyectos de `tests` contienen comprobaciones de lógica, configuración y compatibilidad con KSP. Algunas pruebas también verifican la presencia de modelos y texturas: para esas comprobaciones se necesita el contenido de la release, además de los fuentes. Las pruebas de integración que utilizan ensamblados stock requieren la instalación local de KSP indicada por cada proyecto.

Las comprobaciones automáticas no sustituyen las pruebas dentro de KSP. Las herramientas y los documentos de desarrollo pueden mencionar rutas locales o recursos artísticos que no se publican en el repositorio de fuentes.

Las referencias de KSP y Unity y las DLL de dependencias externas pertenecen a sus respectivos autores. No se redistribuyen como parte del código fuente de Koffee.
