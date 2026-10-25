# SHO Technical Pilon TP-25

## Configuración final vigente — 15 de octubre de 2026

Los seis nodos de las caras y los dos axiales están siempre disponibles, junto con la posibilidad de montar equipos por superficie. Se eliminan del CFG el selector `ModulePartVariants` y los seis controladores de visibilidad. El pilón se instala por stack, conserva escala 1, sus modelos y colliders, 200 EC, 20 unidades de monopropelente, 120 kg secos / 200 kg llenos calculados, coste 800 y entryCost 1800. El árbol propio stock/CTT lo asigna a Hardware. Desde el 16 de octubre de 2026, la categoría del editor es `Utility`.

Revisión de categoría del 16 de octubre: comprobado que el único cambio del CFG es `Structural` → `Utility`, conservando los demás bytes, módulos y recursos; los parches stock/CTT no sobrescriben la categoría y HookChecks pasa (dos detours EVA requieren Unity/Mono). El comprobador `check_tp25_resources.py` sigue fallando porque exige los módulos de variantes y visibilidad ya retirados; se confirmó que el CFG anterior a este cambio también carecía de ellos. Tras cerrarse KSP, el cambio se desplegó el 16 de octubre de 2026 en `D:/Juegos/KSP`, sustituyendo solo el valor de categoría. SHA-256 del CFG instalado: `717467530b1d08affaa4338ae594eb9670e1935c70b0a4a70b19ceb7b79c2478`; los otros 253 archivos instalados conservaron sus bytes. Respaldo y manifiesto: `Utils/Backups/TP25Utility/20261016T095833Z`. Prueba visual en KSP pendiente.

Inventario de piezas, anclajes y despliegue en [el estado final de equipos y TP-25](../../../../../docs/Anclajes-Traseros-Equipos-TP25.md). No se han realizado pruebas de esta configuración final ni se crea commit, por indicación del usuario. Las pruebas del selector y los informes existentes documentan revisiones anteriores.


Pieza estructural independiente para Koffee con batería y reserva de monopropelente. Entrega de diseño del 15 de octubre de 2026. Desplegada en `D:/Juegos/KSP` para las pruebas del selector descritas abajo.

## Archivos

- `SHO_Technical_Pilon_TP25.blend`: fuente editable en Blender 4.4, en metros. Malla visible con grupos de vértices por componente, colliders separados y escena de revisión con las mallas stock y seis ED-01 actuales.
- `SHO_Technical_Pilon_TP25.mu`: modelo de la pieza, sin bahía ni equipos incorporados.
- `TP25_AlbedoSpecular.png`: atlas RGBA de 1024 × 1024. RGB difuso; alfa de intensidad especular, no transparencia.
- `TP25_Normal.png`: normal de ejecución, empaquetado X en A e Y en G, R/B a 255.
- `TechnicalPilonTP25.cfg` y `Localization.cfg`: configuración y localización ES/EN. ID nuevo `SHOTechnicalPilonTP25`; claves `#LOC_Koffee_TP25_*`. El título exacto es `SHO Technical Pilon TP-25` en ambos idiomas. Se reutiliza `#LOC_Koffee_Manufacturer`, cuyo valor existente es `SHO Aerospace` en ES/EN.
- `Source/`: construcción reproducible, informes JSON, normal RGB para Blender y vistas de comprobación. No son módulos del juego.

## Diseño y dimensiones

Prisma hexagonal regular de seis caras. Paneles de 0,600 × 0,790 m con 54 agujeros vacíos cada uno y cuatro tornillos de sujeción diferenciados. Agujeros y tornillos están dibujados en el atlas, con relieve normal analítico. La estructura, las abrazaderas y los terminales son grises; solo el aislamiento de los cables es rojo. Pantalla e indicador son detalles visuales sin consumo, animación ni código.

Altura total: **1,000 m**, desde Y KSP = −0,500 hasta +0,500, incluido el accesorio circular superior. Dimensiones máximas X/Y/Z KSP: aproximadamente 1,3741 × 1,0000 × 1,1900 m. Apotema de montaje: 0,590 m. La base termina en el borde hexagonal inferior: no tiene pie, disco, pedestal ni geometría por debajo de Y = −0,500.

Nodos de stack inferior y superior en Y = ±0,500 m, direcciones respectivas −Y y +Y, tamaño 0. `scale = 1`, `rescaleFactor = 1`. `attachRules = 1,0,1,1,0` permite equipos por superficie y piezas hijas por stack. El bastidor se instala por stack. Las seis caras ofrecen colliders físicos utilizables.

### Historial: antiguas configuraciones de montaje en el PAW

El selector de variantes stock `ModulePartVariants` ofrece exactamente dos opciones localizadas en el PAW:

- **Superficie / Surface**, predeterminada: montaje superficial como en la entrega inicial, con los dos nodos axiales permanentes.
- **Seis nodos laterales / Six side nodes**: mantiene el montaje superficial y añade un nodo de stack tamaño 0 en el centro de cada panel, orientado hacia fuera.

Los IDs axiales siguen siendo `top` y `bottom`, ahora mediante `NODE` y los transforms **`attach_North` y `attach_South`**. Conservan exactamente las posiciones ±0,500 m y direcciones ±Y. Permanecen disponibles en ambas configuraciones. No se añaden duplicados de estos nodos.

Los seis nodos laterales se llaman `face_1` a `face_6`, usan `attach_Face01` a `attach_Face06` y están en el plano de montaje, a 0,590 m del eje. Su altura es Y = −0,005 m, el centro exacto de los paneles claros actuales. `Source/node_validation.json` registra posiciones y direcciones medidas directamente del MU. Las variantes no cambian las reglas de montaje ni la simetría. No hay variación de masa, coste, aspecto, geometría ni colliders.

La revisión del 15 de octubre de 2026 sustituye el ciclo `ModuleDynamicNodes`: aunque ambos conjuntos estaban presentes en el CFG instalado y en la caché de ModuleManager, el usuario confirmó que el control no aparecía en el PAW. La entrega anterior solo había comprobado los datos y simulado los cambios; eso no validaba la aparición del menú en el juego.

Las dos variantes activan u ocultan exclusivamente los grupos vacíos `TP25_SurfaceConfiguration` y `TP25_FaceNodes`; la malla visible permanece activa. Se reutilizan seis instancias del controlador **ya existente** `ModuleKoffeeDockingVisibility`, una por `face_1` a `face_6`, con `controlDockingPorts = false`. Este controlador escucha `onVariantApplied`, comprueba el grupo en `LateUpdate` y oculta/restaura tipo, radio e icono del nodo conservando su objeto y las referencias de piezas unidas. North/South no están gestionados por esos controladores. No se añaden puertos, no se modifica ni recompila la DLL y no se conserva la ruta de autostrut de `ModuleDynamicNodes`. La clase y sus campos se han comprobado por reflexión en la DLL instalada.

La actualización original del Blender y el MU solo cambió los transforms de conexión. `Source/node_update_validation.json` confirma que geometría visible, colliders, materiales y texturas se conservaban exactamente. La corrección del selector no cambia esos archivos. La comprobación offline revisa las dos variantes, los seis controladores y los cambios Superficie → Seis nodos → Superficie para verificar que los seis se desactivan al volver y que North/South permanecen activos. Las vistas existentes siguen representando la misma geometría.

**Prueba real del PAW realizada en KSP 1.12.5, en inglés**, el 15 de octubre de 2026: partida Sandbox independiente `TP25SelectorQA20261015`, nave `TP25 Selector QA`. El PAW muestra `Variant: Surface` y `Six side nodes`; al seleccionar otra pieza, los marcadores laterales aparecen en Six y desaparecen en Surface mientras el nodo axial sigue disponible. Se unieron dos TP-25 por top/bottom y se cambió la variante del inferior sin perder la unión. Se guardó la nave con `selectedVariant = FaceNodes` y se volvió a cargar desde el VAB: el PAW restauró Six side nodes y la unión axial permaneció. En pantalla se observaron 200/200 EC y 20/20 de monopropelente por pieza, 800 fondos para una pieza y 1600 para las dos. El registro acotado de estas pruebas es `Source/paw_selector_validation.json`. Quedan pendientes el montaje de equipos en cada una de las seis caras, simetría y pruebas en vuelo.

Se han usado 26 colliders convexos sin triggers: seis sectores de pared, seis paneles con su rail inferior, dos tapas y doce segmentos del accesorio superior. El centro hueco queda libre. No hay volumen de protección, `ModuleCargoBay` ni comportamiento asociado a la matriz o la garantía. La protección aerodinámica corresponde exclusivamente a la bahía stock.

## Referencias comprobadas

- Concepto v5 e instrucciones: `docs/Concepts/HexagonalEquipmentRack/Concept-Bastidor-Hexagonal-v5-Cables-Rojos.png` y `Referencia-Modelado.md`.
- Dirección artística: `docs/ArtStyle-Stock-MasterPrompt.txt`.
- Bahía local: `D:/Juegos/KSP/GameData/Squad/Parts/Utility/ServiceBay_v2/ServiceBay_250_v2.cfg` y `Assets/ServiceBay_250_v2.mu`.
- ED-01 actual: `GameData/ShoAerospace/Koffee/Parts/Experiments_Device_ED-01/Experiment_Device_ED01.cfg` y `.mu`.

Los nodos interiores stock están en ±0,550 m: separación 1,100 m. Sus colliders de suelo y techo llegan a ±0,557153 m. El MU contiene un desplazamiento de exportación en su raíz; las medidas de ejecución lo eliminan, siguiendo la normalización del modelo por PartLoader, sin mover los hijos. `Source/source_audit.json` conserva los hashes de las mallas y el transform original.

El ED-01 usa `rescaleFactor = 0.75`. Su envolvente renderizada actual mide 0,536366 × 0,598931 × 0,280433 m en X/Y/Z KSP. No se ha cambiado su escala, modelo, CFG, textura ni nodo posterior. La posición de comprobación utiliza el nodo superficial actual Z = −0,174 antes de aplicar la escala. Este nodo deja aproximadamente 5,4 mm entre la envolvente visual trasera del equipo y el plano del panel; es una propiedad de la pieza existente, no un volumen añadido al bastidor.

Al unir el nodo inferior del bastidor al nodo interior inferior stock, su centro queda en Y = −0,050 m. Al unirlo por el superior, queda en +0,050 m. Se comprobaron ambas alturas, con un ED-01 centrado en cada cara y su centro 0,020 m por debajo del centro del bastidor.

## Balance y capacidades

| Parámetro | Valor acordado | Motivo |
|---|---:|---|
| Masa seca | 0,12 t (120 kg) | Bastidor con batería, depósito y conducciones integrados |
| ElectricCharge | 200 / 200 EC | Batería stock, inicialmente llena |
| MonoPropellant | 20 / 20 unidades | Reserva stock, inicialmente llena; añade 80 kg |
| Masa llena | 0,20 t (200 kg) | 120 kg secos + 80 kg de monopropelente |
| Coste con recursos iniciales | 800 fondos | Incluye los 24 fondos de monopropelente; no se suman de nuevo |
| Coste de entrada | 1800 fondos | Desbloqueo modesto de una estructura especializada |
| Tecnología | `advConstruction` | Construcción estructural avanzada; ID comprobado en el árbol stock local |

Con el extra `Extras/ExperimentTechTree` instalado, los parches stock y CTT reasignan el TP-25 a `koffeeHardware` (Hardware, 18 de ciencia). `advConstruction` queda como alternativa cuando no se instala ese extra.

El balance de recursos sigue [la propuesta acordada](../../../../../docs/Propuesta-Balance-TP25-Recursos.md). La frase original española y su traducción inglesa se conservan completas, incluida la matriz hipertrónica, el cableado afásico y la advertencia de garantía; solo se añade al final una frase breve con las dos capacidades. No se han realizado correcciones de mojibake.

La pieza solo almacena recursos stock. No genera electricidad ni incorpora propulsores, consumidores adicionales o código nuevo. La pantalla no consume EC. La batería y la reserva siguen las reglas de flujo stock y pueden alimentar equipos y RCS externos de la nave; su autonomía depende del consumo y del vehículo. La configuración fija conserva estas capacidades, masa y precio, con todos los nodos disponibles. El modelo, los colliders y los ocho nodos se conservan.

Comprobación estática con `ResourcesGeneric.cfg` local: `MonoPropellant.density = 0.004` t/unidad y `unitCost = 1.2` fondos/unidad; ElectricCharge tiene ambos valores a cero. Por tanto, vacío de monopropelente: 120 kg y 776 fondos; con 10 unidades: 160 kg y 788 fondos; con 20 unidades: 200 kg y 800 fondos. Son cálculos de configuración, no lecturas del editor. El coste de entrada permanece en 1800 fondos.

`Source/check_tp25_resources.py` comprueba recursos, capacidades, masa seca/llena, precio inicial, coste de entrada y ausencia de módulos nuevos de generación o propulsión. El informe es `Source/resource_validation.json`. La prueba del selector confirmó las capacidades y el precio lleno mostrado en KSP y su conservación al guardar/cargar la nave; siguen pendientes las variaciones de masa/precio con distintos niveles de llenado y el consumo por batería/RCS externos.

## Validación histórica y límites de la configuración final

La comprobación offline verifica mallas cerradas, orientación exterior, triángulos no degenerados, seis paneles, altura, escala, CFG, nodos, IDs, materiales, texturas, canales del normal y colliders exportados. La reapertura del Blender vuelve a exportar la pieza para contrastarla con el MU entregado; los resultados quedan en `Source/reopen_validation.json`.

`Source/validation.json` registra la comprobación por ejes separadores (SAT). Usa colliders convexos reales de la bahía y el bastidor, cajas conservadoras de las mallas renderizadas actuales de los ED-01 y un prisma hexagonal que engloba los detalles visibles del bastidor. Se evalúan **241 instantes** del clip stock `ServiceBayDoors`, con interpolación Hermite de sus curvas y normalización de cuaterniones, para cada una de las dos alturas de instalación. No se detectan intersecciones en las muestras.

Separaciones proyectadas conservadoras obtenidas:

- Suelo o techo stock: **7,15 mm** en el extremo unido por stack.
- Conjunto respecto de paredes y puertas: **244,6 mm** como mínimo de las muestras.
- Entre envolventes de ED-01: **117,1 mm**.
- Envolvente del ED-01 respecto del bastidor: **5,4 mm**.

Son separaciones sobre ejes, no distancias exactas entre superficies. El muestreo finito del recorrido no constituye una demostración continua ni una simulación de Unity. Los equipos distintos del ED-01 requieren su propia comprobación según forma y colocación.

**KSP se ha ejecutado para la comprobación del selector descrita arriba.** Se observaron la pieza y sus texturas, el nombre y las capacidades en el catálogo/PAW en inglés, la unión axial top/bottom, el cambio de variantes y el guardado/carga de la nave de prueba. Quedan pendientes la revisión específica de normales, la localización española dentro del juego, el montaje superficial y por nodos de equipos en cada cara, la apertura/cierre de la bahía con una nave completa, colisiones físicas, consumo por equipos y RCS externos y protección aerodinámica. No se presentan estas pruebas restantes como realizadas.

## Vistas y uso de la fuente

- `Source/TP25_Final.png`: pieza independiente, vista desde arriba sin cara inferior visible.
- `Source/TP25_Front.png`: rejilla, tornillos, pantalla y cableado.
- `Source/TP25_InStockBay_Open.png` y `TP25_InStockBay_Front.png`: bahía real abierta con seis ED-01, sin retirar la tapa.
- `Source/TP25_InStockBay_Cutaway.png` y `TP25_InStockBay_Top.png`: **sección de revisión**, con la tapa superior stock ocultada solo en la visualización para mostrar los seis equipos.
- `Source/TP25_InStockBay_Closed.png`: bahía real cerrada; el montaje interior queda oculto.

En Blender, la colección `TP25_ASSET_ONLY` contiene la pieza exportable. Las colecciones `REVIEW_ONLY_*` no se exportan. Para revisar el montaje completo, mostrar `REVIEW_ONLY_STOCK_BAY_AND_SIX_ED01`; para la sección, ocultar los objetos `REVIEW_Bay_*` de esa colección y mostrar `REVIEW_ONLY_TOP_DECK_CUTAWAY`. Las referencias quedan alineadas con la raíz de la pieza: la bahía se desplaza +0,050 m en Z Blender para reproducir la unión inferior, manteniendo la raíz exportable en identidad. Las texturas de las referencias están empaquetadas en el Blender; no se copian al modelo de ejecución ni se modifica ningún archivo stock.

Reproducción desde la raíz del proyecto, con Python y Blender 4.4 instalados en las rutas locales registradas:

```powershell
python 'GameData/ShoAerospace/Koffee/Parts/Technical Pilon TP-25/Source/inspect_sources.py'
python 'GameData/ShoAerospace/Koffee/Parts/Technical Pilon TP-25/Source/run_blender.py' build
python 'GameData/ShoAerospace/Koffee/Parts/Technical Pilon TP-25/Source/run_blender.py' validate
python 'GameData/ShoAerospace/Koffee/Parts/Technical Pilon TP-25/Source/run_blender.py' reopen
python 'GameData/ShoAerospace/Koffee/Parts/Technical Pilon TP-25/Source/check_tp25_nodes.py'
python 'GameData/ShoAerospace/Koffee/Parts/Technical Pilon TP-25/Source/check_tp25_resources.py'
```

Los archivos de texto nuevos son UTF-8 sin BOM. No se modifican las bahías antiguas, sus Blender ni ningún archivo ajeno a esta pieza. No se han realizado correcciones de mojibake en archivos existentes.

La revisión de codificación y espacios finales de los archivos nuevos pasa. `git diff --check` limitado a esta carpeta no informa de incidencias. La ejecución global sí informa de espacios finales en cambios ajenos de `GameData/ShoAerospace/Agencies/ShoAerospace.cfg` (líneas 7 y 8) y `docs/Categorías de Experimentos.txt` (línea 6); no se han alterado esos archivos.

