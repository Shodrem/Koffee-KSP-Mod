# Catálogo activo de configuraciones de experimentos

> Progresión vigente: [árbol Koffee obligatorio](../../../../docs/Arbol-Tecnologico-Predeterminado.md). Cada configuración declara directamente su nodo propio; no se mantienen tecnologías stock de respaldo. Las menciones al extra y al reparto stock/CTT de las incorporaciones históricas siguientes han quedado sustituidas.

> Esquema vigente (22-10-2026): [capacidades de laboratorios y circuito común de Astronomía](../../../../docs/Capacidades-Laboratorios.md). Sustituye las referencias anteriores a StationBasic universal o a VesselCrew; conservar los datos de captura y balance descritos.

Guía de alta y mantenimiento del contenido: [Crear un experimento](../../../../docs/Guia-Crear-Experimentos.md). Incluye los archivos, la definición científica, la localización, los desbloqueos y las comprobaciones.

Esta carpeta contiene datos de producción necesarios para el selector del PAW y la presentación de desbloqueos en I+D. Forma parte de la instalación de Koffee. Conservarla al limpiar piezas antiguas o preparar una distribución.

## Organización

Cada experimento tiene una carpeta propia:

| Carpeta | ID científico | Anfitrión |
| --- | --- | --- |
| `ContainerIntegrity` | `containerIntegrityTest` | Contenedor |
| `SmallMechanismReliability` | `smallMechanismReliability` | Contenedor |
| `AlloySolidification` | `alloySolidification` | Contenedor |
| `CapillaryTransport` | `capillaryTransport` | Contenedor |
| `ExtendedCrewStudy` | `extendedCrewStudy` | ED-01 |
| `SeedGermination` | `seedGermination` | Contenedor |
| `MineralCrystalGrowth` | `mineralCrystalGrowth` | Contenedor |
| `AlgaeCultivation` | `algaeCultivation` | Contenedor |
| `RadiationDosimetry` | `radiationDosimetry` | ED-01 |
| `ThermalEnvironmentStudy` | `thermalEnvironmentStudy` | ED-01 |
| `SurfacePressureStudy` | `surfacePressureStudy` | ED-01 |
| `SurfaceObservation` | `surfaceObservation` | ED-01 |
| `SurfaceAltimetry` | `surfaceAltimetry` | ED-01 |
| `TelescopeObservation` | `telescopeObservation` | ED-01 |
| `SolarRadiometry` | `solarRadiometry` | ED-01 |
| `TelescopeOccultation` | `telescopeOccultation` | ED-01 |
| `SolutionChemistry` | `solutionChemistry` | Contenedor |
| `PolymerAging` | `polymerAging` | Contenedor |
| `BatteryConservation` | `batteryConservation` | ED-01 |
| `RadiationPressure` | `solarRadiationPressure` | ED-01 |
| `MysteryGooStudy` | `mysteryGooStudy` | ED-01 |
| `OpticalSystemStability` | `opticalSystemStability` | ED-01 |

- `configuration.cfg`: registro `PART` del experimento, con ID interno, metadatos, modelo, coste, masa y vínculo mediante `ModuleKoffeeExperiment.experimentID`. El selector usa ese registro como origen de la configuración; I+D lo representa como desbloqueo.
- `experiment.cfg`: definición científica stock `EXPERIMENT_DEFINITION` y reglas de trabajo `KOFFEE_STATION_EXPERIMENT`, incluidos categoría, presupuesto RP, personal, equipo y condiciones.
- `AlgaeCultivation/surfaceUpgrade.cfg`: mejora stock `koffeeSurfaceBiology`, conservada junto a su experimento. Las mejoras del árbol opcional siguen en `Extras/ExperimentTechTree`.

Los modelos, materiales y etiquetas compartidos permanecen en `Parts/Experiment_Container_62` y `Parts/Experiments_Device_ED-01`. Los registros los referencian mediante URLs explícitas de GameData. La textura existente `Koffee_ContainerIntegrity_diffuse.png` se conserva con los recursos del contenedor, fuera de este catálogo.

## Relación con las piezas físicas y ModuleManager

El editor ofrece el contenedor configurable y ED-01. Cada instancia guarda una elección que queda fija en vuelo.

`Patches/ExperimentSelector.cfg` clona primero el anfitrión del contenedor a partir del registro de Integridad. Después convierte los veintidós registros en configuraciones: `category = none`, `TechHidden = false`, `entryCost = 0` y un módulo informativo para I+D. Los anfitriones físicos conservan sus compras stock. Los costes de entrada presentes en los CFG de origen también sirven para crear el anfitrión antes de esa conversión; conservar el orden del parche.

Investigar la tecnología habilita la configuración en Ciencia y Carrera. Los registros reciben sus recibos gratuitos por la ruta stock de I+D; seleccionar una configuración conserva su coste y masa de construcción. Las mejoras de superficie se resuelven aparte y la capacidad se guarda por instancia.

## Contrato de carga y mantenimiento

KSP 1.12.5 descubre las `PART` por tipo de nodo desde la raíz de `GameDatabase`: `PartLoader.<CompileAll>.MoveNext` solicita `GetConfigs("PART")`, y `GameDatabase.GetConfigs` consulta `root.GetConfigs(typeName, true)`. La carga admite esta ubicación fuera de `Parts`. Contrato comprobado en la DLL local y en HookChecks.

Mantener exactamente un registro de configuración por ID científico. Al trasladar archivos, retirar las copias de la ubicación anterior también en KSP; dos copias crearían enlaces duplicados. Las rutas originales `Parts/StationExperiments` y `Parts/Experiments_Device_ED-01/Experiments` quedan sustituidas por esta carpeta. No se mantienen migraciones de partidas o naves anteriores.

Las suites de núcleo, ciencia y hooks leen el catálogo desde esta ubicación. HookChecks busca también copias duplicadas en todo el GameData de Koffee y aplica los parches reales con ModuleManager para stock/CTT y con/sin el extra.

Diseño, validación y límites: [selector del editor](../../../../docs/Selector-Experimentos-Editor.md), [progresión tecnológica](../../../../docs/Diseno-Rama-Tecnologica-Experimentos.md) y [mejoras de superficie](../../../../docs/Diseno-PartUpgrades-Superficie.md). Estos enlaces corresponden al repositorio de desarrollo.

## Transporte por capilaridad

`CapillaryTransport` / `capillaryTransport` incorpora Física (`Physics`) al contenedor y eleva el catálogo a trece. Se desbloquea en Miniaturización sin el extra y en Física espacial y astronomía I con el extra stock/CTT. Trabajo de referencia 288 RP/seis horas, ciencia 13/16, transmisión 100 %. Informes ES/EN diferentes para órbita baja y alta, sin captura gravitatoria ni instrumento externo. [Diseño y validación](../../../../docs/Diseno-Transporte-Capilaridad.md).

## Fiabilidad de pequeños mecanismos

El registro `SmallMechanismReliability` / `smallMechanismReliability` añade una configuración de `Technology` al contenedor y eleva el catálogo a once. Usa `koffeeTechnologyMaterials1` con el extra stock/CTT y `spaceExploration` sin él. El ensayo es narrativo, sin instrumento externo ni captura. Ciencia base 14; balance provisional: 576 RP, límite 18 y transmisión 0,3, sin bonus adicional por la caja. [Diseño y validación](../../../../docs/Diseno-Fiabilidad-Pequenos-Mecanismos.md).

## Solidificación de aleaciones

`AlloySolidification` / `alloySolidification` añade Materiales y química (`MaterialsChemistry`) en contenedor; el catálogo activo suma doce configuraciones. Comparte el nivel I stock/CTT con mecanismos y usa `spaceExploration` sin el extra. Ensayo narrativo sin instrumento ni captura: 576 RP, ciencia base 14, límite 18 y transmisión 0,3; sin superficie ni bonus adicional. Usa Yellow y `Label_Materials` de la raíz compartida. Implementado y desplegado el 16 de octubre de 2026; cinco CFG verificados, DLL conservada y prueba en KSP pendiente. [Registro de despliegue](../../../../docs/Solidificacion-Aleaciones-Despliegue.json). [Diseño y validación](../../../../docs/Diseno-Solidificacion-Aleaciones.md).

## Captura única STS-25

`TelescopeObservation` / `telescopeObservation` añade Astronomía en ED-01 y eleva el catálogo a catorce configuraciones. Requiere el instrumento STS-25 y una captura válida desde su visor antes de iniciar 288 RP de elaboración con el personal y Koffee habituales. La ciencia base/límite es 8/8, con transmisión completa y multiplicador stock del objetivo observado; cambiar la órbita del observador no crea otro sujeto. Las observaciones posteriores se archivan sin otro informe ni recompensa, preparadas para consumidores futuros sin implementar un contrato. [Diseño, fuentes stock y validación](../../../../docs/Diseno-Observacion-Telescopio-STS25.md).

## Radiometría solar orbital

`SolarRadiometry` / `solarRadiometry` añade Física en ED-01; el catálogo actual suma dieciséis configuraciones. Requiere RS-A instalado en la nave y abierto, publicación solar física coherente y flujo positivo para iniciar en órbita baja o alta. Después analiza esa captura inicial: 576 RP (12 horas a afinidad 5/nivel 3), ciencia base/límite 20/20 y transmisión completa. Sin el extra usa `scienceTech`; con el extra stock/CTT usa `koffeePhysicsAstronomy2`. Admite la publicación agregada de Kopernicus sin confundir el eclipse de la estrella principal con sombra total. [Diseño final, persistencia, auditoría y validación](../../../../docs/Diseno-Radiometria-Solar-Orbital.md).

## Química de soluciones y envejecimiento de polímeros

`SolutionChemistry` / `solutionChemistry` y `PolymerAging` / `polymerAging` añaden dos contenedores de Materiales y química: el catálogo suma 18 configuraciones. Ambos admiten superficie aterrizada y órbitas baja/alta, con informes ES/EN por situación, sin biomas científicos ni sensores. Soluciones usa 576 RP y polímeros 1152 RP; ambos ofrecen ciencia 14/18 y transmisión 0,3. El vacío de polímeros procede de su cámara interna. Usan `spaceExploration` sin extra y `koffeeTechnologyMaterials1` con extra stock/CTT. [Diseño y validación](../../../../docs/Diseno-Quimica-Soluciones-y-Polimeros.md).

## Conservación de baterías

`BatteryConservation` / `batteryConservation` añade Tecnología en ED-01, con una Z-100 stock (`batteryPack`) instalada en la misma nave como equipo obligatorio. Admite superficie y órbitas baja/alta sin mejora: 576 RP (12 horas de referencia), ciencia 14/18, transmisión completa y sin captura ni modificación de la batería. Usa `spaceExploration` sin extra y `koffeeTechnologyMaterials1` con extra stock/CTT. El catálogo suma 19 configuraciones. [Diseño y validación](../../../../docs/Diseno-Conservacion-Baterias.md).

Presión de radiación solar: `RadiationPressure` / `solarRadiationPressure`, configuración ED-01 de Física con RP-A como único equipo requerido. [Diseño, captura inicial y límites de sombra](../../../../docs/Diseno-Presion-Radiacion-Solar.md).

## Estudio prolongado de materiales con Goo

`MysteryGooStudy` / `mysteryGooStudy` eleva el catálogo a 21 configuraciones. Ensayo narrativo de Materiales y química en ED-01, con unidad stock `GooExperiment` y laboratorio `StationBasic`: aplicaciones como aislante en órbita en TQF I y cimientos flexibles en superficie con la mejora `koffeeGooSurface` de TQF II. Trabajo 480 RP; ciencia base/límite 6/8 con multiplicadores stock por cuerpo/situación y transmisión 0,3. Coste de configuración 1200 frente a 1000 de temperatura; conserva masa de 0,15 t y volumen de 91 L del ED-01. No altera el experimento stock ni añade un multiplicador científico propio. [Diseño y validación](../../../../docs/Diseno-Goo-Prolongado.md).

## Estabilidad de sistemas ópticos

`OpticalSystemStability` / `opticalSystemStability` eleva el catálogo a 22 configuraciones. ED-01, Tecnología, MaPhyTec, BO-01 instalada en la misma nave y TQF II directo para configuración e instrumento. Solo órbitas baja/alta de Kerbin, Mun y Minmus, sin captura numérica ni requisito solar. Balance provisional: 864 RP, ciencia 16/20 y transmisión completa. No exige ni desbloquea el LMR-X final. [Implementación, modelo y validación](../../../../docs/BO01-Implementacion-y-Validacion.md).
