# Tessitura — Definición del proyecto

Sep 29, 2026 · @mn

## Visión y objetivos

Tessitura (nombre de trabajo) es un editor de partituras de escritorio para Windows, macOS y Linux que produce grabado de calidad editorial sin retoques manuales y se maneja casi por completo desde el teclado. Compite en el espacio de Dorico, Sibelius y MuseScore, y ocupa el hueco que dejó Finale al descontinuarse en 2024.

**Propuesta de valor**

- **Grabado profesional por defecto:** espaciado óptico, evitación automática de colisiones y reglas tipográficas de referencia (Behind Bars, de Elaine Gould).
- **Entrada rápida:** teclado primero, MIDI paso a paso y en tiempo real; el ratón es apoyo, no requisito.
- **Modelo semántico:** se guarda música (alturas, duraciones, voces), no posiciones. La maquetación se recalcula siempre, así que cambiar el tamaño de página o la transposición nunca rompe la partitura.
- **Un solo código nativo:** C# y .NET 10 en las tres plataformas, con el mismo motor de dibujo en pantalla, PDF y SVG.

**Objetivos medibles para la 1.0**

| Objetivo | Meta |
| --- | --- |
| Respuesta al editar una nota | < 16 ms (60 fps) en una partitura de 30 pentagramas y 300 compases |
| Apertura de una partitura grande | < 3 s |
| Arranque en frío | < 2 s |
| Fidelidad MusicXML 4.0 (ida y vuelta) | ≥ 95 % del corpus de pruebas |
| Exportación | PDF y SVG vectoriales con fuentes incrustadas, idénticos a la pantalla |

## Usuarios y casos de uso

El usuario principal es el profesional que escribe música a diario; todo lo demás se diseña para no estorbarle.

| Perfil | Necesidad principal | Funciones clave |
| --- | --- | --- |
| Compositor o arreglista | Velocidad y control fino | Atajos, paleta de comandos, entrada MIDI, reducción de voces |
| Copista o editor musical | Calidad de grabado | Estilos de casa, ajustes de espaciado, exportación PDF para imprenta |
| Director de banda o coro | Partes y transposición | Extracción de partes, instrumentos transpositores, impresión por lotes |
| Docente o estudiante | Facilidad y escucha | Plantillas, reproducción, ejercicios exportables |

**Casos de uso principales**

1. Crear una partitura desde una plantilla (cuarteto de cuerda, banda, coro SATB) y escribir la música con el teclado.
2. Importar un MusicXML de otro programa, corregirlo y reexportarlo.
3. Escuchar la partitura para revisarla mientras el cursor sigue la reproducción.
4. Generar las partes individuales transpuestas, ajustar sus saltos de página e imprimirlas.
5. Exportar el PDF final y el MusicXML o MIDI para intercambio.

## Alcance funcional por versiones

El MVP demuestra el motor de grabado y la entrada por teclado con música de una voz por pentagrama; la 1.0 cubre la notación orquestal habitual; lo demás queda fuera hasta después.

| Área | MVP (0.x) | 1.0 | Después de la 1.0 |
| --- | --- | --- | --- |
| Notación básica | Claves, armaduras, compases, notas, silencios, puntillos, alteraciones, ligaduras de unión, barras automáticas | — | — |
| Notación avanzada | — | Hasta 4 voces, grupos irregulares, ligaduras de expresión, reguladores, dinámicas, articulaciones, ornamentos, octavas, pedal, repeticiones, casillas, letras, cifrado de acordes | Tablatura, percusión avanzada, notación contemporánea, música antigua |
| Entrada | Teclado | MIDI paso a paso y en tiempo real, popovers de texto, paleta de comandos | Reconocimiento de escaneos (OMR), entrada por voz |
| Vistas | Página | Página, continua (galera) y parte | Vista de ensayo multiventana |
| Partes | — | Extracción vinculada, transposición, compases de espera agrupados | Condensación automática de secciones |
| Reproducción | Sintetizador SoundFont básico | Mezclador, interpretación de dinámicas y articulaciones, salida MIDI | Instrumentos VST3 |
| Archivos | Formato .tess, exportación PDF | MusicXML, MIDI, SVG, PNG | MEI, MNX, ABC |
| Maquetación | Saltos automáticos | Estilos de casa, saltos manuales, ajustes finos por elemento | Plantillas de editorial compartibles |
| Extensibilidad | — | Atajos configurables | API de plugins en C# |

## Experiencia de usuario de principio a fin

El recorrido va de la pantalla de inicio al PDF final sin cambiar de herramienta, y la partitura ocupa siempre el centro de la ventana.

**Recorrido del usuario**

1. **Inicio:** partituras recientes con miniatura, plantillas y botón de importar (MusicXML, MIDI).
2. **Asistente de nueva partitura:** instrumentos (buscador con familias), título y compositor, compás, tonalidad, tempo, tamaño de página y tamaño de pentagrama. Todo es editable después.
3. **Escritura:** modo de entrada de notas con cursor, teclado o MIDI.
4. **Revisión:** reproducción con cursor que sigue la música y mezclador.
5. **Maquetación:** estilos, saltos de sistema y página, ajustes finos arrastrando elementos.
6. **Partes:** cada parte es una vista vinculada de la misma música, con su propia maquetación.
7. **Exportación:** PDF, SVG, PNG, MusicXML y MIDI, por lotes para todas las partes.

**Distribución de la ventana principal**

| Zona | Contenido |
| --- | --- |
| Barra superior | Menú, selector de vista (página, continua, parte), transporte de reproducción, zoom |
| Panel izquierdo | Paletas de notación por categoría (claves, dinámicas, articulaciones, líneas, texto) |
| Centro | Lienzo de la partitura (SkiaSharp) con desplazamiento y zoom suaves |
| Panel derecho | Inspector de propiedades del elemento seleccionado y ajustes de estilo |
| Panel inferior (plegable) | Mezclador y teclado de piano virtual |
| Barra de estado | Modo actual, duración y voz activas, compás y tiempo del cursor, selección |

Los paneles se pueden plegar para trabajar a pantalla completa. Tema claro y oscuro; la página se dibuja siempre como papel, con opción de papel oscuro.

**Modos de trabajo**

- **Selección** (por defecto, `Esc`): seleccionar, mover, borrar, copiar.
- **Entrada de notas** (`N`): un cursor marca pentagrama, voz y posición; cada tecla escribe música.
- **MIDI paso a paso:** el teclado MIDI da la altura, el teclado del ordenador la duración.
- **MIDI en tiempo real:** grabación con metrónomo y cuantización posterior.

**Atajos principales (configurables)**

| Acción | Atajo |
| --- | --- |
| Escribir nota | `A`–`G` |
| Duración: semicorchea, corchea, negra, blanca, redonda | `3`, `4`, `5`, `6`, `7` |
| Puntillo | `.` |
| Silencio | `0` |
| Subir o bajar semitono | `↑` / `↓` |
| Subir o bajar octava | `Ctrl+↑` / `Ctrl+↓` |
| Añadir intervalo al acorde | `Alt+2`–`Alt+8` |
| Ligadura de unión | `T` |
| Popover de dinámicas, tempo o texto | `Shift+D`, `Shift+T`, `Shift+X` |
| Paleta de comandos | `Ctrl+K` |
| Reproducir o detener | `Espacio` |
| Deshacer o rehacer | `Ctrl+Z` / `Ctrl+Y` |

Los popovers aceptan texto: escribir `mf`, `q=120` o `Cmaj7` crea el elemento correcto sin buscarlo en una paleta.

## Arquitectura general

La aplicación se divide en ocho proyectos en cuatro capas, más un proyecto de arranque (Tessitura.Desktop), y el modelo musical no sabe nada de Avalonia ni de SkiaSharp. Eso permite probar el dominio y el motor de grabado sin interfaz, y reutilizarlos en una futura versión web o de línea de comandos.

&#91;embedded content: arquitectura en capas · 8 proyectos\]

El motor de grabado usa solo las métricas SMuFL (JSON), no SkiaSharp, así que puede ejecutarse en un hilo de fondo o en un servidor. Rendering es el único proyecto que dibuja.

**Flujo de una edición**

1. El usuario pulsa una tecla; `ScoreView` la traduce a una acción registrada.
2. La acción crea un comando que produce una nueva instantánea inmutable de la partitura.
3. El motor de grabado recibe los compases modificados y recalcula solo los sistemas afectados, en segundo plano.
4. El resultado es una lista de dibujo (primitivas posicionadas) por página.
5. Rendering vuelve a grabar solo las páginas cambiadas y Avalonia refresca el lienzo.

**Estructura de la solución**

```
Tessitura.sln
src/
  Tessitura.Core/          modelo, teoría, fracciones
  Tessitura.Smufl/         carga de metadatos SMuFL
  Tessitura.Engraving/     maquetación y reglas de grabado
  Tessitura.Rendering/     SkiaSharp, lista de dibujo, exportadores
  Tessitura.Editing/       comandos, selección, historial
  Tessitura.Playback/      secuenciador, MIDI, audio
  Tessitura.IO/            .tess, MusicXML, MIDI
  Tessitura.App/           vistas y ViewModels de Avalonia
  Tessitura.Desktop/       punto de entrada y empaquetado
tests/
  Tessitura.Core.Tests/
  Tessitura.Engraving.Tests/   pruebas de imagen de referencia
  Tessitura.IO.Tests/          corpus MusicXML
  Tessitura.Benchmarks/
assets/
  fonts/  soundfonts/  templates/
```

## Modelo de dominio musical

El modelo representa la música, no su dibujo: guarda alturas escritas, duraciones exactas y relaciones entre eventos, y deja la posición en la página al motor de grabado. Es inmutable y cada edición produce una instantánea nueva que comparte todo lo que no cambió.

**Jerarquía**

```
Score
├─ Metadata          título, compositor, derechos, créditos
├─ Instruments[]     nombre, transposición, rango, sonido
│   └─ Staves[]      líneas, clave inicial
├─ Measures[]        línea temporal global: compás, armadura,
│                    tempo, barra, marca de ensayo, repeticiones
├─ StaffMeasures[staff, measure]
│   └─ Voices[1..4]
│       └─ Events[]    Chord | Rest | TupletGroup
│           └─ Notes[]  Pitch, alteración forzada, cabeza, ligadura
├─ Attachments[]     dinámicas, articulaciones, texto, letra, acordes
│                    (anclados a un EventId)
├─ Spanners[]        ligaduras de expresión, reguladores, octavas,
│                    pedal (EventId inicial y final)
├─ Parts[]           vistas: instrumentos incluidos y estilo propio
└─ Style             reglas de grabado y ajustes manuales
```

**Tipos centrales**

```csharp
public readonly record struct Fraction(long Num, long Den);   // siempre reducida

public readonly record struct Pitch(Step Step, int Alter, int Octave)
{
    public int MidiNumber => /* ... */;
    public Pitch Transpose(Interval interval) => /* ... */;
}

public readonly record struct Duration(NoteValue Value, int Dots)
{
    public Fraction Length => /* ... */;
}

public sealed record Chord(EventId Id, Duration Duration,
    ImmutableArray<Note> Notes, StemDirection Stem) : MusicEvent;

public sealed record Score(ScoreMetadata Metadata,
    ImmutableArray<Instrument> Instruments,
    ImmutableArray<Measure> Measures,
    ImmutableDictionary<StaffMeasureKey, StaffMeasure> Content,
    ImmutableArray<Spanner> Spanners, Style Style);
```

**Decisiones de diseño**

- **Tiempo racional:** toda posición y duración es una fracción exacta; nunca `double`. Los tresillos y los compases compuestos cuadran sin errores de redondeo.
- **Altura escrita, no número MIDI:** Do sostenido y Re bemol son distintos. La altura sonora se deriva aplicando la transposición del instrumento.
- **Partitura en concierto o transpuesta** es una opción de vista, no dos copias de la música.
- **Identificadores estables** en cada evento para anclar ligaduras, dinámicas y comentarios aunque la música se mueva.
- **Inmutabilidad con granularidad de compás:** una edición reconstruye solo el compás tocado y la raíz. Deshacer es volver a la instantánea anterior, y el grabado puede leer una instantánea en otro hilo sin bloqueos.
- **Ajustes manuales separados:** los desplazamientos que el usuario hace a mano se guardan como diferencias sobre la posición automática, nunca como posiciones absolutas.
- **Invariante de compás:** cada voz suma exactamente la duración del compás; el editor rellena con silencios o parte notas con ligaduras al cruzar la barra.

## Motor de grabado

El motor de grabado es la pieza que define la calidad del producto y la de mayor riesgo técnico. Transforma una instantánea del modelo en páginas con cada glifo posicionado, trabajando en espacios de pentagrama (distancia entre dos líneas) y convirtiendo a puntos solo al final.

**Etapas**

1. **Resolución musical:** barras automáticas según el compás, alteraciones según las reglas del compás y la armadura, dirección de plicas y reparto de voces.
2. **Segmentos rítmicos:** columnas verticales que agrupan todo lo que suena en el mismo instante en todos los pentagramas, para que los tiempos queden alineados.
3. **Espaciado horizontal:** cada columna recibe un ancho ideal según la duración más corta que la atraviesa, más el ancho mínimo que evita colisiones de alteraciones, puntillos y cabezas.
4. **Saltos de sistema:** programación dinámica al estilo Knuth-Plass que minimiza la penalización total por estirar o comprimir cada sistema, respetando los saltos manuales.
5. **Justificación:** el espacio sobrante del sistema se reparte según la elasticidad de cada columna.
6. **Distribución vertical y saltos de página:** separación de pentagramas con perfiles de contorno (skylines) y reparto del espacio de la página.
7. **Elementos colocados:** plicas, inclinación de barras, ligaduras como curvas Bézier que esquivan obstáculos, dinámicas, texto y letras, cada uno contra el skyline del pentagrama.
8. **Emisión:** una lista de dibujo por página con primitivas y el identificador del elemento de origen.

**Espaciado horizontal**

El ancho ideal crece con el logaritmo de la duración, como en el grabado tradicional: una blanca no ocupa el doble que una negra.

```latex
w(d) = w_{\min} \left(1 + \alpha \log_2 \frac{d}{d_{\min}}\right)
```

Aquí d es la duración de la columna, d\_min la duración más corta del sistema, w\_min su ancho y α un parámetro de estilo (en torno a 0,6).

**Reglas y estilo**

Las reglas se toman de *Behind Bars* (Elaine Gould) y de las constantes `engravingDefaults` de SMuFL. Todos los valores (grosor de plica, distancia entre notas y alteraciones, altura de ligaduras) viven en un objeto `Style` que el usuario puede ajustar y guardar como estilo de casa.

**Maquetación incremental**

- Se guarda en caché el ancho mínimo e ideal de cada compás.
- Una edición invalida solo sus compases; si el ancho del sistema no cambia, el resto de la página no se toca.
- Si cambia, el reflujo avanza sistema a sistema y se detiene en cuanto un salto vuelve a coincidir con el anterior.
- La maquetación corre en un hilo de fondo con cancelación: si llega otra edición, se descarta el trabajo obsoleto.

## Renderizado con SkiaSharp

Un único camino de dibujo sirve para pantalla, PDF, SVG y PNG, así que lo que se ve es exactamente lo que se imprime.

**Fuentes**

- **Música:** Bravura, la fuente de referencia del estándar SMuFL (licencia OFL). Se carga su `bravura_metadata.json` con cajas de glifos, anclajes de plica (`stemUpSE`, `stemDownNW`) y constantes de grabado. La arquitectura admite otras fuentes SMuFL (Petaluma, Leland) sin cambios de código.
- **Texto:** una fuente compañera con licencia OFL (por ejemplo Academico o Edwin) para títulos, letras y términos de expresión, con modelado tipográfico mediante HarfBuzzSharp.

**Lista de dibujo**

El motor de grabado emite primitivas inmutables en coordenadas de página: `Glyph` (código SMuFL, posición, tamaño), `Line`, `Path` (Bézier), `Text` y `Rect`. Cada primitiva lleva el `ElementId` del modelo, que sirve para la selección y el color.

**Lienzo en Avalonia**

- `ScoreCanvas` es un control propio que sobrescribe `Render` y añade una operación `ICustomDrawOperation`; dentro obtiene el `SKCanvas` a través de `ISkiaSharpApiLeaseFeature` y dibuja directamente con Skia.
- Cada página se graba una sola vez como `SKPicture` y se reproduce a cualquier zoom sin volver a recorrer la lista de dibujo.
- Solo se dibujan las páginas visibles; una edición invalida únicamente las páginas afectadas.
- La selección, el cursor de entrada, la cabeza de reproducción y los arrastres van en una capa superpuesta aparte, para no regrabar la página al seleccionar.
- Pantallas de alta densidad: el zoom y la escala del sistema se aplican como matriz del lienzo, con todo en vectores.

**Detección de clics**

Cada página mantiene un índice espacial (rejilla o R-tree) con las cajas de su lista de dibujo. Un clic se convierte a coordenadas de página, se consulta el índice y se devuelve el `ElementId` más cercano, con prioridad para notas sobre líneas.

**Exportación**

| Formato | API de SkiaSharp |
| --- | --- |
| PDF | `SKDocument.CreatePdf`, una página por página de la partitura, fuentes incrustadas |
| SVG | `SKSvgCanvas` |
| PNG | `SKSurface` a la resolución elegida |

## Edición

Toda modificación de la partitura pasa por un comando, y todo comando está registrado como acción con identificador, nombre y atajo. Así el teclado, los menús, la paleta de comandos y los futuros plugins usan el mismo camino.

**Comandos e historial**

```csharp
public interface IScoreCommand
{
    string Description { get; }
    Score Apply(Score score, EditContext context);
}

public sealed class History
{
    // Pila de (instantánea, descripción, selección)
    public void Push(Score next, string description, Selection selection);
    public Score Undo();
    public Score Redo();
}
```

- Deshacer restaura la instantánea anterior; como las instantáneas comparten estructura, guardar cientos de pasos cuesta poca memoria.
- Las entradas consecutivas del mismo tipo (escribir notas seguidas) se agrupan en un solo paso si el usuario lo configura.
- El historial guarda también la selección, para que deshacer devuelva el cursor al sitio.

**Selección**

- **Elemento:** una nota, dinámica o ligadura.
- **Rango:** un rectángulo musical (pentagramas × intervalo de tiempo), no gráfico; sobrevive a los cambios de maquetación.
- **Lista:** varios elementos con `Ctrl+clic`, con filtros por tipo (solo dinámicas, solo voz 2).

**Entrada de notas**

El cursor de entrada es una posición musical (pentagrama, voz, instante). Al escribir una nota el editor decide la octava más cercana a la anterior, rellena con silencios lo que falte, parte la nota con una ligadura de unión si cruza la barra y reescribe los silencios según el compás. Esta notación rítmica automática evita que el usuario tenga que pensar en cómo se agrupa la música.

**Portapapeles**

Copiar un rango pone en el portapapeles el formato interno y, a la vez, MusicXML, para pegar en otros programas. Pegar respeta la voz y el pentagrama de destino y transpone si el instrumento lo requiere.

## Reproducción y audio

La reproducción convierte la partitura en una interpretación y la envía a un sintetizador interno o a un dispositivo MIDI externo, con el reloj de audio como fuente de verdad para el cursor.

1. **Modelo de interpretación:** despliega repeticiones, casillas, D.C. y D.S.; construye el mapa de tempo; traduce dinámicas a velocidad MIDI y articulaciones a duración y ataque (staccato al 50 %, tenuto al 100 %).
2. **Secuenciador:** lista ordenada de eventos con tiempo en muestras, reprogramable si el usuario edita mientras suena.
3. **Salida:**
   - Sintetizador interno con SoundFont `.sf2` mediante MeltySynth (C# puro, licencia MIT).
   - Salida de audio multiplataforma detrás de una interfaz `IAudioOutput` con miniaudio, elegida en F0.11.
   - Entrada y salida MIDI detrás de `IMidiPort`: DryWetMIDI en Windows/macOS y adaptador al secuenciador de ALSA (`alsa-lib`) en Linux, elegidos en F0.12. La validación con dispositivos físicos en Windows/Linux corresponde a F3.9.
4. **Sincronización:** la UI lee la posición del reloj de audio en cada fotograma, mueve la cabeza de reproducción y desplaza la vista si hace falta.

El mezclador ofrece por instrumento volumen, panorama, silencio y solo, y el sonido de cada instrumento se asigna automáticamente a su programa General MIDI. La latencia objetivo del búfer es inferior a 20 ms.

## Formato de archivo, importación y exportación

El formato propio `.tess` es un ZIP con JSON versionado, y MusicXML es la puerta principal de interoperabilidad.

**Formato nativo .tess**

```
partitura.tess (ZIP)
├─ manifest.json    versión de formato, versión de la app
├─ score.json       música y ajustes manuales
├─ style.json       estilo de la partitura
├─ parts/           estilo y saltos de cada parte
└─ thumbnail.png    miniatura para la pantalla de inicio
```

- Serialización con `System.Text.Json` y generación de código en compilación (rápido y compatible con recorte).
- Cada versión de formato tiene una migración a la siguiente; nunca se rompe la apertura de archivos antiguos.
- Guardado atómico (escribir en temporal y renombrar) y autoguardado de recuperación cada 2 minutos.

**Formatos externos**

| Formato | Importar | Exportar | Versión |
| --- | --- | --- | --- |
| MusicXML (`.musicxml`, `.mxl`) | 1.0 | 1.0 | 4.0 |
| MIDI (`.mid`) | 1.0, con cuantización | 1.0 | SMF tipo 1 |
| PDF | — | MVP | — |
| SVG, PNG | — | 1.0 | — |
| MEI, MNX, ABC | Después de 1.0 | Después de 1.0 | — |

MNX es el sucesor de MusicXML que prepara el grupo comunitario de notación del W3C; conviene seguirlo pero no bloquear nada por él.

## Stack tecnológico y dependencias

Las dependencias propuestas son de código abierto. Se permiten MIT, BSD, Apache 2.0, OFL y, por decisión expresa del 29 de septiembre de 2026, LGPL. Las obligaciones de distribución de cualquier componente LGPL se documentarán antes del lanzamiento.

| Área | Elección | Motivo |
| --- | --- | --- |
| Lenguaje y runtime | C# 14 sobre .NET 10 (LTS) | Soporte largo, rendimiento, `Span<T>`, NativeAOT opcional |
| Interfaz | Avalonia, última versión estable | Multiplataforma real, estilo propio, backend Skia |
| MVVM | CommunityToolkit.Mvvm | Generadores de código, sin reflexión |
| Dibujo 2D | SkiaSharp, la versión que trae Avalonia | Evita conflictos de binarios nativos entre ambos |
| Modelado de texto | HarfBuzzSharp | Ligaduras tipográficas, idiomas no latinos en letras |
| Fuente musical | Bravura (SMuFL) | Estándar de la industria, OFL |
| Inyección y registro | Microsoft.Extensions.DependencyInjection y Logging | Estándar en .NET |
| Serialización | System.Text.Json con generadores; `XmlReader` para MusicXML | Rápido y compatible con recorte |
| Sintetizador | MeltySynth | SoundFont en C# puro |
| MIDI | DryWetMIDI; `alsa-lib` para dispositivos en Linux | Archivos MIDI y dispositivos Windows/macOS con DryWetMIDI; puertos Linux mediante adaptador `IMidiPort` a ALSA |
| Salida de audio | miniaudio, elegido en F0.11 | Multiplataforma, baja latencia; validación física Windows/Linux en F3.6 |
| Pruebas | xUnit, Verify, FsCheck, BenchmarkDotNet | Unitarias, instantáneas, propiedades y rendimiento |
| Instaladores y actualizaciones | Velopack | Un solo flujo para Windows, macOS y Linux |
| Integración continua | GitHub Actions, matriz de 3 sistemas operativos | Compila y prueba en cada plataforma |

**Convenciones**

- Nullable activado y advertencias como errores en todos los proyectos.
- `Directory.Packages.props` para fijar versiones de forma central.
- Los proyectos Core, Smufl y Engraving no pueden referenciar Avalonia ni SkiaSharp; una prueba de arquitectura lo verifica.

## Calidad, rendimiento y pruebas

La calidad del grabado se protege con pruebas de imagen de referencia y el rendimiento con presupuestos medidos en cada integración.

**Presupuestos de rendimiento** (partitura de referencia: 30 pentagramas, 300 compases)

| Operación | Presupuesto |
| --- | --- |
| Escribir una nota hasta verla en pantalla | < 16 ms |
| Desplazamiento y zoom | 60 fps constantes |
| Maquetación completa desde cero | < 1,5 s |
| Memoria en uso | < 500 MB |

**Hilos**

El hilo de la interfaz solo procesa la entrada y compone el lienzo. La maquetación y la exportación trabajan sobre instantáneas inmutables en segundo plano, y la reproducción tiene su propio hilo de audio sin asignaciones de memoria.

**Estrategia de pruebas**

- **Unitarias del dominio:** alteraciones, transposición, aritmética de fracciones, agrupación rítmica.
- **Basadas en propiedades (FsCheck):** invariantes como «cada voz suma la duración del compás» tras cualquier secuencia de comandos.
- **Imágenes de referencia:** un catálogo de ejemplos se renderiza a PNG y se compara con la versión aprobada; cualquier diferencia exige revisión humana.
- **Ida y vuelta de MusicXML:** la MusicXML Test Suite pública más archivos reales exportados desde Dorico, Sibelius y MuseScore.
- **Rendimiento:** BenchmarkDotNet en integración continua, con fallo si una métrica empeora más de un 10 %.
- **Interfaz:** pruebas sin ventana con Avalonia.Headless para los flujos principales.

**Accesibilidad y localización**

- Todo se puede hacer con teclado, y los atajos son configurables.
- El lector de pantalla describe la selección («negra, Do 5, compás 12, tiempo 3») mediante los peers de automatización de Avalonia.
- Tema de alto contraste y tamaño de interfaz ajustable.
- Textos en archivos de recursos, con español e inglés desde el inicio.

## Hoja de ruta

Seis fases llevan del primer pentagrama dibujado a la 1.0 en unas 54 semanas; el orden prioriza el motor de grabado porque es el mayor riesgo.

&#91;embedded content: hoja de ruta · 6 fases, 54 semanas\]

Ninguna fase empieza sin cerrar la puerta de la anterior. Las duraciones son estimaciones iniciales que conviene revisar al terminar la fase 1, cuando se conozca la velocidad real del equipo.

## Riesgos y licencias

El mayor riesgo es subestimar el motor de grabado; por eso se construye primero y se mide desde el primer mes.

| Riesgo | Impacto | Mitigación |
| --- | --- | --- |
| El motor de grabado tarda más de lo previsto | Alto | Empezar con un subconjunto (una voz), catálogo de imágenes de referencia desde la fase 1, reglas de Behind Bars como especificación |
| Rendimiento insuficiente en partituras grandes | Alto | Modelo inmutable, maquetación incremental, caché de `SKPicture`, pruebas de rendimiento desde la fase 1 |
| Audio y MIDI poco fiables en Linux | Medio | Interfaces `IAudioOutput` e `IMidiPort`, prueba de concepto en la fase 0 |
| MusicXML inconsistente entre programas | Medio | Corpus con archivos reales de cada exportador, importación tolerante con avisos |
| Choque de versiones de SkiaSharp con Avalonia | Medio | Usar la versión que trae Avalonia, fijada de forma central |
| Crecimiento del alcance | Alto | MVP cerrado; toda función nueva se asigna a una fase antes de empezar |

**Licencias de terceros**

| Componente | Licencia | Nota |
| --- | --- | --- |
| Avalonia, SkiaSharp, HarfBuzzSharp | MIT | Incluir los avisos de copyright |
| CommunityToolkit.Mvvm, MeltySynth, DryWetMIDI, Velopack | MIT | Incluir los avisos de copyright |
| Bravura y fuente de texto | SIL OFL 1.1 | Se pueden incrustar y distribuir; no vender la fuente por separado |
| OpenAL Soft, si se elige | LGPL 2 o posterior | Documentar y cumplir las obligaciones de distribución antes del lanzamiento |
| `alsa-lib` (adaptador Linux aprobado en F0.12) | LGPL 2.1 o posterior | Documentar y cumplir las obligaciones de distribución antes del lanzamiento |
| Especificación MusicXML | Licencia de especificación del W3C Community Group | Libre de implementar |

Si la aplicación va a ser propietaria, no se puede copiar código de MuseScore (GPL); sí se puede estudiar su diseño y documentación. Queda abierta la decisión de la licencia de Tessitura: propietaria, de código abierto o mixta (núcleo abierto con funciones profesionales de pago).
