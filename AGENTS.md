# Instrucciones del proyecto

Todos los archivos del proyecto deben guardarse en UTF-8 sin BOM.

Nunca cambies la codificación de un archivo existente.

Cuando leas archivos desde consola o sistema de archivos, interpreta siempre el contenido como UTF-8 sin BOM y evita cualquier conversión automática de encoding.

Cuando modifiques archivos:
- No cambies el encoding al guardar archivos.
- Respeta exactamente el encoding original del archivo.
- No reescribas bloques completos si no es necesario.
- No alteres textos existentes del jugador.
- Corrige siempre los textos deteriorados que detectes (mojibake, tildes o separadores sustituidos por signos de interrogación, etc.), conservando el significado, la codificación original y los demás bytes del archivo.
- El usuario autoriza estas correcciones sin pedir confirmación en cada caso. Registra las correcciones realizadas.

## Fabricante de piezas y experimentos

- El nombre visible del fabricante es siempre `SHO Aerospace`, en todos los idiomas. Para contenido nuevo usar `manufacturer = #LOC_Koffee_Manufacturer`; las claves específicas existentes deben mostrar el mismo texto.
- No renombrar IDs internos, rutas ni recursos para corregir el nombre visible. Excluir las carpetas OLD (sin distinguir mayúsculas/minúsculas) del contenido que se actualiza.
- Convención y revisión: [Fabricante SHO Aerospace](docs/Fabricante-SHO-Aerospace.md).

## Descripciones de experimentos

- Al crear o modificar la descripción de un experimento, incluir su categoría científica con la frase: "Científicos expertos en el campo de [categoría] realizarán este experimento en menos tiempo."
- Sustituir `[categoría]` por el nombre localizado de la categoría declarada en `scienceCategory`; no mostrar el marcador ni inventar una categoría diferente.
- Incluir una frase equivalente en los demás idiomas disponibles, conservando el significado y el resto del texto existente del jugador.

## Recursos de los experimentos

- Las etiquetas de categoría (`Label_*.png`) están en `GameData/ShoAerospace/Koffee/Parts/Experiment_Container_62`, junto al modelo y las texturas del contenedor. Referenciarlas desde esa raíz; no usar la subcarpeta `Labels` para contenido nuevo o actualizado.
- Usar el nombre de archivo existente sin renombrar ni duplicar recursos. La etiqueta de Environment en la raíz se llama actualmente `Label_Environment.png`; ese nombre de recurso no cambia `scienceCategory = Environment` ni su nombre localizado Entorno.

## Datos stock en el diseño de experimentos

- Antes de diseñar, crear o ampliar un experimento, comprobar siempre si KSP stock expone datos o APIs relevantes para su objetivo científico. Consultar la DLL local de KSP 1.12.5, sus CFG y, cuando corresponda, fuentes primarias; no deducir datos solo por el nombre de una propiedad.
- Notificar al usuario los datos relevantes encontrados, indicando qué representan, sus unidades, cuándo se actualizan y sus límites en naves cargadas, descargadas y warp. Si no se encuentra un dato adecuado, comunicarlo también. Esta consulta es investigación técnica y no una solicitud de permiso.
- Separar una observación real de un valor definido para el cuerpo o una estimación. No atribuir al instrumento una lectura que no se haya capturado ni inventar un historial a partir del estado final.
- Registrar en la documentación del diseño la fuente stock comprobada, la semántica de la medida, el instante de captura, la persistencia y los casos sin dato. Reutilizar las rutas stock de presentación y guardado cuando sean adecuadas.

## Futuros trabajos de IVA y exterior

- Antes de crear o modificar un IVA y su exterior, consultar [Estudio de orientación IVA/exterior](docs/Estudio-Orientacion-IVA-Exterior.md), su evidencia JSON y los scripts de `Utils/IvaOrientationProbe`.
- No existe un IVA funcional del proyecto que pueda utilizarse como patrón. La calibración Blender → FBX → Unity está comprobada; la exportación PartTools y la validación dentro de KSP siguen pendientes.
- Aplicar la compensación de orientación una sola vez y repetir la calibración si cambian la preparación, el preset, el origen o las escalas.

## Git

El usuario autoriza guardar en Git local los cambios de código realizados a partir de ahora, sin pedir confirmación para cada commit.

- Al terminar un cambio de código, ejecutar las comprobaciones pertinentes y crear un commit con un mensaje que describa el cambio.
- Incluir las configuraciones, documentación y recursos relacionados con ese cambio cuando corresponda.
- Revisar el contenido preparado antes del commit y evitar incluir cambios ajenos a la tarea.
- Si una comprobación falla o no puede ejecutarse, comunicarlo; no presentar el cambio como validado.
- Informar del commit creado y de la validación realizada al finalizar la tarea.
- Conservar los bytes originales de los archivos y respetar `.gitattributes`.
- Esta autorización corresponde a commits locales; no autoriza publicar ni hacer push a un remoto.
