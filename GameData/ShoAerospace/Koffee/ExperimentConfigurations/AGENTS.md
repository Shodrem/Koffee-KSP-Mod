# Configuraciones activas de Koffee

Esta carpeta es contenido de producción del selector del PAW y de I+D. Incluirla en despliegues y distribuciones. Leer `README.md` antes de reorganizar o retirar sus archivos.

- `configuration.cfg` contiene un registro `PART` necesario para la configuración; que su pieza quede oculta en construcción no lo convierte en contenido obsoleto.
- Mantener exactamente un registro por `experimentID`, junto a su `experiment.cfg` y sus mejoras propias.
- Conservar IDs, enlaces, textos del jugador y URLs explícitas de modelos y texturas. Los recursos físicos compartidos permanecen en `Parts`.
- Al trasladar un registro, retirar la copia anterior del proyecto y de la instalación de pruebas para evitar duplicados. Actualizar las rutas de herramientas y pruebas.
- Guardar los archivos en UTF-8 sin BOM y preservar los bytes y finales de línea existentes. Ejecutar núcleo, ScienceChecks y HookChecks cuando cambie la organización del catálogo.
