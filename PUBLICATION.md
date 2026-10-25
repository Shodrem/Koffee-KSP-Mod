# Base de la publicación inicial

Preparación del 25 de octubre de 2026 para <https://github.com/Shodrem/Koffee-KSP-Mod>.

- El código de producción procede del commit local de desarrollo `1e0f228b23ff60cfdafd110893bbc108347a0bab`, sin cambios en los archivos de `src`.
- La base de la release es la carpeta `ShoAerospace` de la instalación KSP 1.12.5 que el autor confirma como funcional. Se conservan los bytes de sus DLL, CFG, modelos y texturas seleccionados para ejecución.
- Se han copiado al repositorio público los 26 CFG que diferían entre la instalación funcional y el proyecto de desarrollo. Incluyen valores de ciencia y costes tecnológicos. Por ejemplo, Integridad usa `baseValue = 10` y `scienceCap = 12`, y el nodo Experimental cuesta 90. La prueba de contenido de Integridad se ajusta a esos mismos valores.
- Los documentos históricos de diseño pueden describir otros balances. Los CFG de esta publicación son la referencia del contenido distribuido.
- Las dos DLL se han vuelto a compilar desde los fuentes públicos con KSPBuildTools 1.1.1, sin errores ni advertencias. La comparación con las DLL de la instalación coincide en IL de métodos, tipos, referencias, firmas, constantes, cadenas, atributos y descriptores de recursos, excluyendo identificadores y rutas de compilación y el sufijo de revisión de `AssemblyInformationalVersion`.
- Esa comparación no afirma que los archivos PE sean idénticos byte a byte: las DLL del ZIP son las de la instalación funcional del autor, no las recién recompiladas.
- El paquete instalable excluye fuentes, Blender, FBX, carpetas Source, archivos desactivados, registros y documentación de desarrollo. Se añaden las licencias de Koffee y las instrucciones de instalación.
- ModuleManager y HarmonyKSP se instalan por separado. BRP y DBS son integraciones opcionales que también instala el jugador; no se distribuyen sus archivos.
- La corrección de tildes dañadas en un documento técnico se registra en [el registro de texto](docs/Correcciones-Texto-Publicacion-20261025.md).

## Comprobaciones de la preparación

- Compilación Release de `Koffee.dll` y `Koffee.BRP.dll`: cero errores y advertencias.
- Compilación de Core Checks y Hook Checks: cero errores y advertencias. No se presenta Hook Checks como ejecutado.
- Core Checks ejecutado contra el GameData extraído del ZIP y los fuentes utilizados como soporte de sus comprobaciones de código: 201 comprobaciones correctas. Esos fuentes auxiliares de prueba no se añaden al ZIP.
- ZIP: 481 archivos, 74 referencias explícitas a recursos comprobadas, CRC y contenido byte a byte correctos. Se excluyen 40 archivos de desarrollo de la carpeta instalada.
- Las únicas DLL del ZIP son `Koffee.dll` y `Koffee.BRP.dll`. Los archivos instalables conservan los bytes de la copia funcional y las configuraciones coinciden con las del repositorio público.

La preparación no ejecuta ni modifica la instalación de KSP. La validación dentro del juego corresponde a la confirmación del autor sobre la copia funcional; los controles de empaquetado no añaden una nueva prueba visual del juego.
