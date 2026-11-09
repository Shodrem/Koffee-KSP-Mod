# Koffee para Kerbal Space Program

Un proyecto de shodrem (SHO Aerospace), creado por afición, para aprender y aportar a la comunidad de KSP.

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

Instala las dependencias por separado: ModuleManager y HarmonyKSP. BackgroundResourceProcessing (BRP), Dynamic Battery Storage (DBS), Contract Configurator, SystemHeat y Lonapse son integraciones opcionales y no se distribuyen con Koffee.

La release v0.1.3 incorpora Komfort, Deadly Mode, nuevas piezas y la KSPedia inglesa actualizada de 23 páginas. La guía española de versiones anteriores no está actualizada para esta entrega. Para actualizar, con KSP cerrado, elimina la carpeta anterior `GameData/ShoAerospace/Koffee` antes de copiar el nuevo `GameData`; conserva tus partidas.

Las dependencias externas y sus licencias se detallan en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
