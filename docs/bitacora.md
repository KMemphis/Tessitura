# Bitácora de Tessitura

## 2026-09-29 · F0.1 Solución y estructura

- Se creó la solución con nueve proyectos de `src/` y cuatro de `tests/`, y se fijó .NET 10, C# 14, nullable y advertencias como errores.
- Las referencias entre proyectos siguen las capas de la definición. Las dependencias de xUnit y del ejecutor de pruebas se fijaron en `Directory.Packages.props`; no se añadieron paquetes de interfaz ni audio antes de sus tareas.
- Verificación: `python3 tests/verify_solution.py` pasó; `dotnet build Tessitura.sln --nologo` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --nologo` pasó 3 pruebas, sin fallos ni omisiones.
- Pendiente: F0.2, integración continua y comprobación en Windows, macOS y Ubuntu.

## 2026-09-29 · F0.2 Integración continua

- Se añadió un flujo de GitHub Actions que compila y ejecuta las pruebas en Windows, macOS y Ubuntu con .NET 10.
- La verificación local de la matriz y de los comandos pasó. `dotnet build` en Release terminó con 0 advertencias y 0 errores; `dotnet test` pasó 3 pruebas, sin fallos ni omisiones.
- La ejecución remota [CI #36648703193](https://github.com/KMemphis/Tessitura/actions/runs/36648703193) terminó en verde en los tres sistemas.
- Pendiente: F0.3, prueba automática de las referencias de arquitectura.

## 2026-09-29 · F0.3 Prueba de arquitectura

- Se añadió una prueba que inspecciona las referencias compiladas y declaradas de Core, Smufl y Engraving para impedir dependencias de Avalonia y SkiaSharp.
- La prueba detectó una referencia temporal a SkiaSharp añadida a Core; se retiró antes del build final.
- `dotnet build` terminó con 0 advertencias y 0 errores. `dotnet test` pasó 6 pruebas, sin fallos ni omisiones.
- Pendiente: F0.4, aritmética racional con `Fraction`.

## 2026-09-29 · F0.4 Fraction

- Se implementó `Fraction` como struct de valor exacto, siempre reducida, con denominador positivo, aritmética, comparación e identidad canónica para `default`.
- Se decidió usar `BigInteger` solo como intermedio para evitar desbordamientos silenciosos; si el resultado reducido no cabe en `long`, se lanza `OverflowException`. FsCheck.Xunit 3.4.0 quedó fijado centralmente para las pruebas de propiedades.
- Las pruebas de propiedades cubren reducción, identidad y asociatividad; también se probaron operaciones, división por cero y límites de `long`.
- `dotnet build` terminó con 0 advertencias y 0 errores. `dotnet test` pasó 13 pruebas, sin fallos ni omisiones.
- Pendiente: F0.5, alturas escritas e intervalos.

## 2026-09-29 · F0.5 Pitch e Interval

- Se añadieron alturas escritas (`Step`, `Alter`, `Octave`) e intervalos firmados por pasos diatónicos y semitonos. `MidiNumber` se calcula a partir de la escritura.
- La transposición calcula la nueva letra y octava por separado de la altura sonora, preservando diferencias enarmónicas.
- Las pruebas cubren Do4 + tercera mayor = Mi4, Si♯3 y Do4 con el mismo número MIDI pero distinta escritura, cruce de octava y transposición inversa con 500 casos de propiedades.
- `dotnet build` terminó con 0 advertencias y 0 errores. `dotnet test` pasó 17 pruebas, sin fallos ni omisiones.
- Pendiente: F0.6, duración de figuras con puntillos.

## 2026-09-29 · F0.6 Duration

- Se añadieron las figuras de redonda a garrapatea y los puntillos, con longitud exacta en `Fraction`.
- El constructor rechaza figuras inválidas y cantidades de puntillos cuya longitud no cabe en una fracción de 64 bits.
- La prueba principal verifica que la negra con doble puntillo vale 7/16; otras pruebas cubren un puntillo y entradas inválidas.
- `dotnet build` terminó con 0 advertencias y 0 errores. `dotnet test` pasó 20 pruebas, sin fallos ni omisiones.
- Pendiente: F0.7, modelo mínimo e invariante de compás.

## 2026-09-29 · F0.7 Modelo mínimo

- Se añadieron records para partitura, instrumentos, pentagramas, compases, voces, acordes, silencios, notas e identificadores estables; las colecciones del modelo son inmutables.
- Los eventos guardan el inicio como `Fraction`, y el validador comprueba que cada voz llena el compás sin huecos ni solapes.
- Las pruebas aceptan cuatro negras en 4/4, rechazan cinco y detectan un hueco aunque la suma de duraciones coincida.
- `dotnet build` terminó con 0 advertencias y 0 errores. `dotnet test` pasó 24 pruebas, sin fallos ni omisiones.
- Pendiente: F0.8, cargar Bravura y sus metadatos SMuFL.

## 2026-09-29 · F0.8 Tessitura.Smufl

- Se incorporaron Bravura 1.482, sus metadatos y la licencia OFL en `assets/fonts`. El mapa de 2.940 nombres y códigos se derivó de los metadatos oficiales del estándar SMuFL; `assets/fonts/README.md` documenta versiones y procedencia.
- `SmuflMetadata` carga valores numéricos de `engravingDefaults`, familias de texto, cajas, anclajes y códigos de glifo en espacios de pentagrama.
- Una prueba compara `stemUpSE` de `noteheadBlack` directamente con el JSON. Otras comprueban la caja, el grosor de plica, el código de glifo y la presencia de la fuente.
- `dotnet build` terminó con 0 advertencias y 0 errores. `dotnet test` pasó 26 pruebas, sin fallos ni omisiones.
- Pendiente: F0.9, ventana Avalonia y `ScoreCanvas`.

## 2026-09-29 · F0.9 Ventana y ScoreCanvas

- Se creó la ventana Avalonia con `ScoreCanvas`, una operación `ICustomDrawOperation` y acceso a `SKCanvas` mediante `ISkiaSharpApiLeaseFeature`. El dibujo de la página A4 blanca, su sombra y el fondo reside en Rendering.
- El lienzo ajusta inicialmente toda la página a la ventana, permite zoom con `Ctrl` + rueda centrado en el puntero y desplazamiento con rueda o arrastre del botón central.
- Se fijó Avalonia 12.1.3 y SkiaSharp 3.119.4, la versión resuelta por Avalonia, en `Directory.Packages.props`.
- Las pruebas comprueban zoom, desplazamiento, encuadre inicial y píxeles de la página. La captura de la ventana macOS, tomada por ID de ventana, está en `docs/capturas/f0.9-macos.png`.
- `dotnet build` terminó con 0 advertencias y 0 errores. `dotnet test` pasó 30 pruebas, sin fallos ni omisiones.
- Pendiente: F0.10, primer dibujo musical con métricas SMuFL.

## 2026-09-29 · F0.10 Primer dibujo musical

- Se dibujaron a mano cinco líneas, clave de sol, sostenido y negra con plica usando Bravura y sus metadatos SMuFL. La plica comienza exactamente en el anclaje `stemUpSE` de `noteheadBlack`, comprobado por prueba.
- Se guardó una captura de la ventana real de macOS en `docs/capturas/f0.10-macos-window.png`, identificada por el proceso `Tessitura.Desktop` y capturada mediante su ID de ventana; no se capturó la pantalla completa.
- CI generó capturas del renderizado en Windows, macOS y Ubuntu: `docs/capturas/f0.10-windows-render.png`, `docs/capturas/f0.10-macos-render.png` y `docs/capturas/f0.10-ubuntu-render.png`. Se revisaron visualmente las tres; son salidas del renderizador, no fotografías de ventanas de CI.
- `dotnet build` terminó con 0 advertencias y 0 errores. `dotnet test` pasó 32 pruebas, sin fallos ni omisiones. La matriz CI pasó en los tres sistemas: https://github.com/KMemphis/Tessitura/actions/runs/36651569950.
- Pendiente: F0.11, comparar las opciones de salida de audio.

## 2026-09-29 · F0.11 Salida de audio

- Se detectó que OpenAL Soft usa LGPL 2 o posterior, en conflicto con la regla anterior de licencias. El propietario autorizó LGPL también para el producto; se actualizaron `AGENTS.md` y la definición.
- Se añadió un SoundFont MIT solo para pruebas, una prueba que verifica que MeltySynth produce señal, y un ejecutable experimental para OpenAL Soft y miniaudio.
- En el Mac arm64 ambos candidatos iniciaron reproducción cinco veces sin errores. Las medianas de retorno de la llamada API fueron 0,048 ms (OpenAL Soft) y 0,056 ms (miniaudio); el primer callback de miniaudio llegó a los 8,204 ms. Ningún valor mide la llegada del sonido al altavoz.
- El informe `docs/decisiones/audio.md` recomienda provisionalmente miniaudio y solicita decidir si se traslada la medición física de Windows y Linux a F3.6, ya que el propietario solo dispone de este Mac.
- `dotnet build` terminó con 0 advertencias y 0 errores. `dotnet test` pasó 33 pruebas, sin fallos ni omisiones. [CI #36653006026](https://github.com/KMemphis/Tessitura/actions/runs/36653006026) pasó en Windows, macOS y Ubuntu.
- El propietario aprobó miniaudio y trasladó la reproducción y medición física de Windows/Linux a F3.6. Se marcó F0.11 como terminada; sigue pendiente demostrar el objetivo de latencia en hardware. Siguiente tarea: F0.12, decisión sobre MIDI.

## 2026-09-29 · F0.12 Dispositivos MIDI

- Se añadió DryWetMIDI 8.0.3 para una prueba de enumeración y recepción. En este Mac no hay puertos físicos; un puerto virtual de CoreMIDI recibió Do4 en canal 2 con velocidad 100.
- La documentación oficial de DryWetMIDI excluye Linux de su API de dispositivos. `docs/decisiones/midi.md` propone un adaptador ALSA detrás de `IMidiPort`; el propietario aprobó esta nueva dependencia de sistema.
- La primera CI enumeró correctamente Windows (sin entradas, con salida Microsoft GS Wavetable Synth) y recibió la nota de loopback en macOS; Ubuntu confirmó ausencia de backend nativo DryWetMIDI. Se corrigió el diagnóstico para dejarlo explícito sin fallar la matriz por esa incompatibilidad prevista.
- [CI #36654098552](https://github.com/KMemphis/Tessitura/actions/runs/36654098552) pasó en los tres sistemas. Ubuntu informó `Supported=false`, Windows enumeró una salida y macOS recibió la nota virtual. `dotnet build` local terminó con 0 advertencias y 0 errores; `dotnet test` pasó 34 pruebas sin fallos ni omisiones.
- El propietario eligió DryWetMIDI para Windows/macOS y ALSA para Linux. Las pruebas con teclado físico en Windows/Linux se trasladaron a F3.9; el adaptador ALSA aún no está implementado. F0.12 queda cerrada y sigue la puerta F0, pendiente de aprobación antes de F1.1.

## 2026-09-29 · F1.1 Lista de dibujo

- El propietario aprobó la puerta F0 y se inició F1. Añadí en Engraving las primitivas inmutables `Glyph`, `Line`, `Path`, `Text` y `Rect`, con `ElementId`, caja y coordenadas en espacios de pentagrama, agrupadas en `Page`.
- La lista se serializa con metadatos generados por `System.Text.Json`. `Page` y `Path` comparan el contenido de sus arreglos por valor; el espacio de nombres `DisplayLists` evita conflictos con los tipos `Path` y `Rect` de otras capas.
- Dos pruebas nuevas cubren la ida y vuelta JSON de las cinco primitivas, sus identificadores y cajas, la igualdad estructural y la detección de cambios en glifos y curvas. `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 36 pruebas sin fallos ni omisiones.
- No quedan pendientes de F1.1. Siguiente tarea: F1.2, renderizar la lista de dibujo con SkiaSharp.

## 2026-09-29 · F1.2 Renderizador de listas de dibujo

- Se añadió `DisplayListRenderer` en Rendering para dibujar `Page` sobre `SKCanvas` y grabarla como `SKPicture`; admite glifos, líneas, curvas, texto y rectángulos. Engraving permanece independiente de SkiaSharp.
- Dos pruebas nuevas comprueban que la nota de F0.10 coincide píxel por píxel con el renderizado provisional y con la reproducción del `SKPicture`, y que se dibujan curvas, texto y rectángulos.
- `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 38 pruebas sin fallos ni omisiones. No quedan pendientes de F1.2. Siguiente tarea: F1.3, imágenes de referencia.

## 2026-09-29 · F1.3 Imágenes de referencia

- Se añadió una comparación PNG por canales con tolerancia, límite de píxeles distintos y PNG de diferencias. La prueba del ejemplo F0.10 usa las capturas de renderizado ya revisadas en F0, según la plataforma; no se sustituyó ninguna imagen aprobada.
- En este Mac la captura aprobada difiere de un renderizado nuevo en 168 de 800.000 píxeles con igualdad estricta y en 90 píxeles con tolerancia de 4 niveles por canal. Se fijó un límite de 100 píxeles para esa tolerancia. Cuando una comparación excede el límite, conserva el candidato y la diferencia; CI los adjunta.
- Una prueba cambia `noteheadBlack` por otro glifo, exige que falle y se genere la diferencia, aprueba una copia temporal y confirma que entonces pasa. La aprobación temporal no toca las referencias del repositorio.
- `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 40 pruebas sin fallos ni omisiones. Siguiente tarea: F1.4, estilo y valores de grabado.

## 2026-09-29 · F1.4 Estilo de grabado

- Se creó `Style` inmutable en Engraving. Los grosores de pentagrama, plica, barra y línea adicional, la separación de barras y la extensión de líneas adicionales proceden de `engravingDefaults` de Bravura; cada propiedad cita su clave.
- La plica normal de 3,5 espacios sigue [*Behind Bars*, «Ground Rules > Stems»](https://www.behindbarsnotation.co.uk/contents/sample_pages.pdf). Los mínimos configurables de Tessitura, 0,25 espacios entre alteración y nota y 0,5 espacios entre columnas, se inspiran respectivamente en las secciones «Accidentals > Placing» y «Ground Rules > Spacing symbols» del [índice del libro](https://behindbarsnotation.co.uk/contents/toc.pdf); esas dos cifras son elecciones del producto, no valores atribuidos al libro.
- Dos pruebas nuevas comprueban los valores frente a los metadatos de Bravura y la ida y vuelta JSON con modificaciones. `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 42 pruebas sin fallos ni omisiones. Siguiente tarea: F1.5, resolución de alteraciones.

## 2026-09-29 · F1.5 Resolución de alteraciones

- Se añadió en Engraving un resolutor que sigue la altura escrita por paso y octava, la armadura vigente y el estado de cada compás. Una continuación de ligadura no repite el signo y establece el estado para notas posteriores. No se modificó el modelo de dominio de Core.
- Pasaron 18 escenarios, incluidos becuadros de cancelación, repetición dentro y fuera del compás, armaduras con sostenidos y bemoles, cambios de armadura, enarmonía, octavas independientes y ligaduras. Dos casos adicionales rechazan armaduras fuera de siete alteraciones.
- Las reglas se documentaron en código con las secciones «Accidentals and Key Signatures» y «Ties» de [*Behind Bars*](https://behindbarsnotation.co.uk/contents/toc.pdf). `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 62 pruebas sin fallos ni omisiones. Siguiente tarea: F1.6, plicas y agrupación automática de barras.

## 2026-09-29 · F1.6 Plicas y barras automáticas

- Se eligió plica arriba por debajo de la línea central y abajo desde la línea central. La agrupación usa `Fraction` para duraciones y límites: pulso de negra en 2/4 y 3/4, medio compás en 4/4 y 2/2, y negra con puntillo en 6/8 y 9/8.
- Trece pruebas nuevas cubren los seis compases, cinco posiciones de plica y cortes por silencio, hueco y nota larga. La división de 4/4 en medios compases sigue [*Behind Bars*, «Metre > Beaming according to the metre», p. 153](https://www.behindbarsnotation.co.uk/contents/sample_pages.pdf).
- `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 75 pruebas sin fallos ni omisiones. Siguiente tarea: F1.7, segmentos rítmicos.

## 2026-09-29 · F1.7 Segmentos rítmicos

- Se añadió un constructor de segmentos en Engraving que lee una medida de `Score`, agrupa todos los eventos por inicio exacto `Fraction` y conserva `EventId`, pentagrama y voz en orden.
- Dos pruebas nuevas verifican un piano con negras en la mano derecha y blancas en la izquierda: ambas coinciden en los instantes 0 y 1/2. Una segunda voz en el pentagrama superior comparte esas mismas columnas.
- `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 77 pruebas sin fallos ni omisiones. Siguiente tarea: F1.8, espaciado horizontal y cabecera de sistema.

## 2026-09-29 · F1.8 Espaciado horizontal

- Se implementó la fórmula logarítmica de la definición con α = 0,6 y límites anticolisión basados en los salientes izquierdo y derecho de cada columna. Las duraciones musicales permanecen en `Fraction`; solo el ancho geométrico se calcula en `double`.
- La cabecera reserva posiciones horizontales para clave, alteraciones de la armadura y cifras del compás, con anchos derivados de las cajas SMuFL. La colocación vertical de esos símbolos corresponde a F1.9.
- Tres pruebas nuevas verifican que la blanca ocupa 1,6 veces el ancho ideal de la negra, que una alteración medida con las cajas reales de Bravura no toca la nota anterior y que los elementos de cabecera no se superponen horizontalmente.
- `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 80 pruebas sin fallos ni omisiones. Siguiente tarea: F1.9, colocación de notas, silencios y símbolos en el pentagrama.

## 2026-09-29 · F1.9 Colocación en el pentagrama

- Se añadió un colocador en Engraving para cabezas, plicas, alteraciones, puntillos, ocho figuras de silencio y líneas adicionales, usando cajas y anclajes SMuFL. Las plicas de notas con varias líneas adicionales llegan como mínimo a la línea central, según *Behind Bars*, «Ground Rules > Stems».
- El propietario aprobó las imágenes de referencia de la escala de cuatro octavas y todos los silencios. Se guardaron versiones renderizadas por CI para macOS, Windows y Ubuntu; una prueba compara cada nuevo renderizado con la referencia de su plataforma y conserva PNG candidato y diferencia si falla.
- Once pruebas nuevas cubren el anclaje de plica, extensión y líneas adicionales, separación de alteraciones y puntillos, los ocho silencios y ambas imágenes. `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 91 pruebas sin fallos ni omisiones. [CI #36659222134](https://github.com/KMemphis/Tessitura/actions/runs/36659222134) pasó en Windows, macOS y Ubuntu. Siguiente tarea: F1.10, barras de corchea.

## 2026-09-29 · F1.10 Barras de corchea

- Se añadió `BeamPlacer` en Engraving para plicas compartidas, barras primarias, niveles múltiples y barras parciales. Las pendientes se limitan a medio espacio y el largo de plica respeta el mínimo del estilo; el grosor y el espacio entre barras proceden de los valores SMuFL.
- El propietario aprobó las referencias de grupos ascendentes, descendentes y mixtos con barras parciales. Se incorporaron las imágenes generadas en Windows, macOS y Ubuntu y se comparan automáticamente en cada ejecución.
- Cinco pruebas cubren pendientes y largos de plica en ambas direcciones, espaciado SMuFL, barras múltiples y parciales, más las tres imágenes. `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 96 pruebas sin fallos ni omisiones. [CI #36661033953](https://github.com/KMemphis/Tessitura/actions/runs/36661033953) pasó en Windows, macOS y Ubuntu; después de aprobar las referencias, las comparaciones automáticas también pasaron en [CI #36661394376](https://github.com/KMemphis/Tessitura/actions/runs/36661394376). Siguiente tarea: F1.11, saltos de sistema y justificación.

## 2026-09-29 · F1.11 Saltos de sistema y justificación

- Se añadió `SystemBreaker` con programación dinámica de badness cúbica para elegir saltos entre compases, basada en el enfoque Knuth-Plass descrito en `docs/definicion-proyecto.md`, «Motor de grabado > Etapas > Saltos de sistema». Los sistemas no finales conservan al menos el 80 % del ancho ideal y el sobrante se reparte según la elasticidad de cada compás; la compresión respeta anchos mínimos y tiene un límite de producto del 12 %.
- Seis pruebas comprueban el salto óptimo, el límite del 80 %, el reparto proporcional, la compresión dentro de mínimos y el rechazo de medidas imposibles o de compresión excesiva. `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 102 pruebas sin fallos ni omisiones. [CI #36662230335](https://github.com/KMemphis/Tessitura/actions/runs/36662230335) pasó en Windows, macOS y Ubuntu. Siguiente tarea: F1.12, páginas y espaciado vertical.

## 2026-09-29 · F1.12 Páginas y espaciado vertical

- Se añadió `VerticalPageLayouter` para ubicar sistemas en páginas y calcular las posiciones de cada pentagrama a partir de su skyline básico. La separación toma el mayor valor entre la distancia mínima y la suma de los salientes vecinos más la holgura; si el sistema no cabe en una página, se informa el error en vez de solapar contenido.
- Cuatro pruebas verifican separación por skyline, paginación de una partitura de diez páginas sin solapes, empaquetado de sistemas cuando caben y rechazo de un sistema demasiado alto. `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 106 pruebas sin fallos ni omisiones. [CI #36662666716](https://github.com/KMemphis/Tessitura/actions/runs/36662666716) pasó en Windows, macOS y Ubuntu. Siguiente tarea: F1.13, textos de página.

## 2026-09-29 · F1.13 Textos de página

- Se añadió la fuente Noto Serif variable con su licencia OFL y suma SHA-256 en `assets/fonts/README.md`. `PageTextLayouter` usa HarfBuzz para medir y colocar centrados el título y el compositor, conserva los identificadores de los números de compás y omite los campos vacíos. `DisplayListRenderer` dibuja las primitivas de texto con la misma fuente y modelado.
- El propietario revisó las candidatas de primera página para macOS, Windows y Ubuntu e indicó continuar el plan hasta F5; se adoptaron como referencias aprobadas. Una prueba compara cada ejecución con la imagen de su plataforma y adjunta la candidata a CI. Se toleran hasta 500 píxeles distintos con diferencia máxima de 16 niveles por canal para cubrir variaciones de antialiasing entre máquinas.
- Las pruebas cubren el centrado con acentos, las posiciones de los números de compás, la omisión de texto vacío y las tres referencias. `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 109 pruebas sin fallos ni omisiones. [CI #36663977981](https://github.com/KMemphis/Tessitura/actions/runs/36663977981) pasó en Windows, macOS y Ubuntu.
- No quedan pendientes de F1.13. Siguiente tarea: F1.14, exportación PDF.

## 2026-09-29 · F1.14 Exportación PDF

- Se añadió `PdfExporter` en Rendering con `SKDocument.CreatePdf`. Conserva los límites de cada página en puntos según el tamaño de espacio de pentagrama elegido, exporta todas las páginas de la lista y valida páginas vacías, dimensiones inválidas y escala no finita. La salida de prueba mantiene música y texto como vectores; Noto Serif y Bravura aparecen como subconjuntos Type 3 con sus programas de glifos `CharProcs` incrustados.
- Dos pruebas nuevas verifican PDF multipágina, tamaño individual de página, fuentes incrustadas y rechazo de opciones inválidas. `pdfinfo` confirma dos páginas (595 × 840 pt y 350 × 525 pt). Rendericé la primera a 148,114 dpi y la comparé visualmente con la salida Skia de 1224 × 1728 píxeles; el texto, glifo y plica coinciden. Preview, Safari y Chrome Guest abrieron el PDF y expusieron el texto de ambas páginas.
- `dotnet build` terminó con 0 advertencias y 0 errores; `dotnet test` pasó 111 pruebas sin fallos ni omisiones. [CI #36665074497](https://github.com/KMemphis/Tessitura/actions/runs/36665074497) pasó en Windows, macOS y Ubuntu.
- No quedan pendientes de F1.14. Siguiente tarea: F1.15, maquetación incremental y rendimiento.

## 2026-09-29 · F1.15 Maquetación incremental y rendimiento

- Se añadió MeasureWidthCalculator: agrupa eventos simultáneos de todos los pentagramas por inicio Fraction y mide cabezas, silencios, alteraciones y puntillos con las cajas SMuFL seleccionadas. IncrementalScoreLayouter conserva los anchos por compás; un cambio de nota recalcula solo ese compás. Si su ancho no cambia, reutiliza todos los sistemas; si cambia, recompone desde el sistema afectado y reutiliza el sufijo al recuperar un salto previo. SystemBreaker y la medición aceptan cancelación sin publicar resultados parciales.
- El generador del proyecto de benchmarks crea 30 pentagramas, 300 compases de 4/4 y 36.000 notas. Con BenchmarkDotNet 0.15.8, Release, Apple M5 y .NET 10.0.11: maquetación completa 4,86 ms de media; cambio de una nota 38,59 µs. Se midieron 5 iteraciones por caso; ambos resultados quedan por debajo de los límites de 1,5 s y 10 ms.
- Seis pruebas nuevas cubren el tamaño de referencia, cálculo con métricas SMuFL, invalidación de un solo compás, reutilización cuando el ancho no cambia, cancelación y continuidad de los sistemas. dotnet build terminó con 0 advertencias y 0 errores; dotnet test pasó 117 pruebas sin fallos ni omisiones. [CI #36666323833](https://github.com/KMemphis/Tessitura/actions/runs/36666323833) pasó en Windows, macOS y Ubuntu.
- No quedan pendientes de F1.15. Siguiente tarea: F1.16, catálogo de 20 ejemplos de referencia.

## 2026-09-30 · F1.16 Catálogo de referencia

- Se creó el catálogo de 20 páginas para escalas, armaduras, alteraciones, figuras y silencios, ritmos, compases simples y compuestos, piano, cuarteto de cuerda y líneas adicionales. El propietario aprobó los ejemplos; quedaron guardadas 20 referencias por plataforma (macOS, Windows y Ubuntu).
- Dos pruebas nuevas verifican los 20 identificadores, renderizan cada página y comparan el PNG con su referencia de plataforma. La matriz usa imágenes específicas por sistema operativo para conservar los umbrales de comparación frente a diferencias de rasterizado.
- `dotnet build Tessitura.sln --configuration Release` terminó con 0 advertencias y 0 errores. `dotnet test Tessitura.sln --configuration Release --no-build` pasó 119 pruebas, sin fallos ni omisiones. [CI #36668009068](https://github.com/KMemphis/Tessitura/actions/runs/36668009068) pasó en Windows, macOS y Ubuntu.
- No quedan tareas de F1. Para la puerta F1, la maquetación de 30 pentagramas × 300 compases midió 4,86 ms completa y 38,59 µs por cambio de una nota; los 20 ejemplos están aprobados. Siguiente: presentar la evidencia de la puerta F1 y esperar aprobación antes de F2.

## 2026-09-30 · F2.1 ActionRegistry y atajos

- Se añadió `ActionRegistry` en App con identificadores estables, nombres, atajos predeterminados y despacho por atajo o ID. La configuración versionada `shortcuts.json` se crea en la carpeta de ajustes del usuario y permite cambiar los atajos sin recompilar; los IDs desconocidos, atajos inválidos y conflictos se rechazan.
- `ScoreCanvas` despacha las teclas mediante el registro y `TessituraApplication` registra zoom, reducción de zoom y ajuste de página. Estas acciones controlan la vista y no modifican la partitura.
- Cinco pruebas nuevas cubren creación de JSON, cambio de atajo, ejecución por ID, validación de entradas y conflictos. `dotnet build Tessitura.sln --configuration Release` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --configuration Release --no-build` pasó 124 pruebas, sin fallos ni omisiones. [CI #36669310714](https://github.com/KMemphis/Tessitura/actions/runs/36669310714) pasó en Windows, macOS y Ubuntu.
- Siguiente tarea: F2.2, comandos de edición e historial.

## 2026-09-30 · F2.2a Comandos e historial base

- Se añadió `IScoreCommand` y `EditContext` en Editing. Los comandos insertan y borran notas, cambian altura escrita, alteración, duración y puntillos; insertar en un silencio crea un acorde y borrar su última nota lo convierte en silencio, conservando instante y duración.
- `History` conserva instantáneas inmutables y selección, deshace y rehace, descarta la rama de rehacer después de una edición nueva y permite consultar si hay pasos disponibles. No se modificó el modelo de Core.
- Ocho pruebas cubren comandos, selección, bifurcación del historial y una propiedad FsCheck con secuencias generadas. `dotnet build Tessitura.sln --configuration Release` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --configuration Release --no-build` pasó 132 pruebas, sin fallos ni omisiones.
- F2.2b implementa el comando de ligadura con un indicador en `Note`, como establece el modelo de la definición.

## 2026-09-30 · F2.2b Ligaduras de unión

- Se añadió `Note.TiedToNext` con valor predeterminado falso y `ChangeTieCommand`, que modifica la nota seleccionada sin mutar la instantánea anterior. Los comandos de altura y alteración conservan el estado de ligadura.
- Una prueba verifica activación, desactivación, preservación al cambiar altura y el ciclo de deshacer/rehacer. La propiedad FsCheck de historial ahora genera también cambios de ligadura y deshace la secuencia completa.
- `dotnet build Tessitura.sln --configuration Release` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --configuration Release --no-build` pasó 133 pruebas, sin fallos ni omisiones.
- Siguiente tarea: F2.3, notación rítmica automática.

## 2026-09-30 · F2.3 Notación rítmica automática

- Cada comando normaliza la voz afectada sobre la línea temporal global. Los eventos posteriores se desplazan si el anterior se alarga, los silencios se reducen o insertan para completar huecos y cada compás queda cubierto exactamente con `Fraction`.
- Las duraciones que cruzan una barra se dividen en segmentos con ligadura entre sus notas. Si el último compás no alcanza, se amplía la partitura y se crean compases de silencios para los otros pentagramas. Los silencios se reescriben en valores que respetan los pulsos; 6/8 usa pulsos de negra con puntillo.
- Cinco pruebas cubren acortamiento, desplazamiento de notas, cruce de barra, ampliación multipentagrama y silencios en 6/8. La prueba FsCheck verifica la invariante de cada voz después de cada comando generado. `dotnet build Tessitura.sln --configuration Release` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --configuration Release --no-build` pasó 138 pruebas, sin fallos ni omisiones.
- Siguiente tarea: F2.4, cursor y modo de entrada.

## 2026-09-30 · F2.4 Cursor y modo de entrada

- Se añadió el controlador de entrada con cursor musical en `Fraction`, modo de selección y entrada, elección de la octava diatónica más cercana, y acciones registradas para notas, duraciones, puntillo, silencios, transposición, ligadura y deshacer/rehacer. Al agotar el compás, el comando añade uno nuevo con silencios para todas las voces.
- `ScoreCanvas` dibuja el cursor como una operación superpuesta independiente y recibe foco al abrirse la ventana. Una prueba de acciones de teclado escribió Do mayor a través de dos compases y validó la invariante; otras cubren `N`/`Esc`, duración con puntillo, octava y restauración del cursor al deshacer/rehacer.
- La ventana de Tessitura se abrió y se capturó por identificador de su proceso (sin captura de pantalla completa); tras enviar la secuencia de teclado, el cursor avanzó en la ventana. La partitura visible sigue siendo el ejemplo provisional hasta conectar la maquetación y el repintado en F2.6.
- `dotnet build Tessitura.sln --configuration Release` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --configuration Release --no-build` pasó 144 pruebas, sin fallos ni omisiones.
- Siguiente tarea: F2.5, detección de clics y selección.

## 2026-09-30 · F2.5 Detección de clics y selección

- Se añadió `PageSpatialIndex` con una rejilla uniforme de las cajas de cada primitiva. Las consultas eligen el glifo más cercano dentro de la tolerancia, dan prioridad a glifos sobre líneas y devuelven los límites unidos de los elementos para resaltarlos.
- `ScoreCanvas` transforma los clics de pantalla a espacios de pentagrama y los pasa al controlador: clic para seleccionar un elemento, `Shift+clic` para ampliar el rectángulo musical entre pentagramas e instantes `Fraction`, y `Ctrl+clic` para alternar elementos en una lista. El resaltado se dibuja en una operación superpuesta.
- Siete pruebas nuevas cubren prioridad y cercanía de impactos, tolerancia, límites agregados, selección de una nota, rango entre dos pentagramas y lista por elementos. `dotnet build Tessitura.sln --configuration Release` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --configuration Release --no-build` pasó 151 pruebas, sin fallos ni omisiones.
- El lienzo acepta y prepara el índice de una página maquetada; el ejemplo que muestra la ventana aún es estático y se conectará al índice y al ciclo de repintado en F2.6.
- Siguiente tarea: F2.6, ciclo de actualización.

## 2026-09-30 · F2.6 Ciclo de actualización

- Se conectaron los comandos de edición con la maquetación incremental en segundo plano. El coordinador compone la página afectada, prepara un índice espacial y graba un `SKPicture`; publica únicamente la instantánea más reciente en el hilo de interfaz. El lienzo muestra la página real y permite seleccionar sus notas.
- El visual de composición de Avalonia recibe la página grabada y solicita su dibujo directamente en el hilo de renderizado. Se conservaron las referencias del `SKPicture` durante dibujos concurrentes para evitar su liberación prematura. La ventana de Tessitura se capturó por el identificador de su proceso y solo de esa ventana: `docs/capturas/f2.6-macos-window.png`.
- Cinco pruebas nuevas cubren geometría A4, IDs de eventos, 30 pentagramas, publicación de la última instantánea, trabajo fuera del hilo de interfaz y selección sobre la página publicada. BenchmarkDotNet midió 3,257 ms de media para actualizar un compás, componer e indexar la página, grabarla y rasterizarla con la partitura de 30 pentagramas × 300 compases (Apple M5, Release, .NET 10.0.11). En la ventana real, la preparación posterior a las primeras entradas midió normalmente menos de 1 ms y el primer dibujo 11–22 ms, según la fase del refresco de 60 Hz.
- El propietario aprobó medir F2.6 como procesamiento <16 ms y presentación en el siguiente cuadro. La meta original de <16 ms hasta verse sigue vigente para la 1.0 y requiere una medición adicional en F4.15/F5.9. `dotnet build Tessitura.sln --configuration Release` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --configuration Release --no-build` pasó 156 pruebas, sin fallos ni omisiones. Siguiente tarea: F2.7, estructura de la ventana.

## 2026-09-30 · F2.7 Estructura de la ventana

- La ventana organiza menú, selector de vista, transporte y zoom arriba; paletas a la izquierda; partitura en el centro; inspector y estilo a la derecha; mezclador y teclado en un panel inferior plegable; y estado del cursor abajo. Las vistas continua y de parte y el transporte aparecen deshabilitados hasta sus fases. Los paneles se pliegan con acciones registradas y atajos configurables.
- Se añadieron temas claro y oscuro para la interfaz y la ventana nativa. El espacio alrededor de la partitura cambia de color y el papel permanece blanco. La barra de estado sigue modo, duración, voz, compás, tiempo y número de elementos seleccionados con posiciones `Fraction`.
- Cuatro pruebas nuevas verifican distribución, acciones de plegado y tema, estado musical y colores de la página. `dotnet build Tessitura.sln --configuration Release` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --configuration Release --no-build` pasó 160 pruebas, sin fallos ni omisiones. Se inspeccionaron las capturas de la ventana de Tessitura por identificador de proceso, sin capturar la pantalla completa: `docs/capturas/f2.7-macos-dark-window.png` y `docs/capturas/f2.7-macos-light-window.png`.
- Siguiente tarea: F2.8, inspector editable mediante comandos.

## 2026-09-30 · F2.8 Inspector

- El inspector ahora muestra tipo, compás, voz, altura, duración y puntillos del evento seleccionado. Sus controles cambian duración, puntillo, alteración y ligadura mediante comandos existentes; la selección de acordes no inventa una nota única que editar.
- Tres pruebas nuevas verifican cambio de duración con deshacer y selección conservada, los controles de puntillo/alteración/ligadura y la selección de acordes. Una cuarta prueba verifica que el estado del cursor avance al compás siguiente al terminar el actual.
- Se probó la ventana de Tessitura en macOS: cambiar el silencio seleccionado de redonda a negra desde el inspector actualiza la partitura y conserva la selección. La captura corresponde únicamente a la ventana: `docs/capturas/f2.8-macos-inspector-window.png`.
- `dotnet build Tessitura.sln --configuration Release --no-restore` terminó con 0 advertencias y 0 errores; `dotnet test Tessitura.sln --configuration Release --no-build` pasó 164 pruebas, sin fallos ni omisiones.
- Tras la observación del propietario sobre el acabado visual, se añadió F5.10 para hacer explícito el pulido visual de la aplicación y revisar capturas en Windows, macOS y Linux. Siguiente tarea: F2.9, paletas básicas.

## 2026-09-30 · F2.9 Paletas básicas

- Se añadieron `Staff.InitialClef`, `Clef` y `Measure.KeySignature` en Core (ya previstos en la definición: clave inicial por pentagrama y armadura en la línea temporal global); `KeySignature` pasó de Engraving a Core. Nuevos comandos con historial: `ChangeClefCommand`, `ChangeKeySignatureCommand` (desde el compás seleccionado en adelante) y `ChangeTimeSignatureCommand` (reflujo de todas las voces con la normalización rítmica de F2.3).
- La paleta izquierda muestra claves (Sol, Fa, Do alto, Do tenor), las 15 armaduras, siete compases y bemol/becuadro/sostenido. Cada botón ejecuta una acción registrada `palette.*` con atajo configurable; la UI no modifica la partitura directamente.
- `ScorePageComposer` dibuja clave y armadura por pentagrama (posiciones según Behind Bars, con `KeySignaturePositions`), coloca las notas según la clave (`StaffPitchPosition`) y resuelve los accidentales con `AccidentalResolver` (armadura, compás y ligaduras). La entrada por teclado respeta la armadura y los accidentales previos del compás.
- Pruebas nuevas: dos en `ScorePaletteTests` (aplicar clave, armadura y alteración con un clic y deshacer; cambio de compás conserva eventos e invariante) y una en `ScorePageComposerTests` (cabecera Fa, armadura y accidentales). `dotnet build` 0 advertencias y 0 errores; `dotnet test` pasó 167 pruebas, sin fallos ni omisiones.
- Deuda: los cambios de armadura o clave a mitad de sistema no se dibujan aún (las notas sí usan la armadura correcta); la posición vertical de la clave de Sol (3,5) es anterior a esta tarea y se revisará en F5.10; la armadura de Do tenor con sostenidos sigue una disposición ascendente pendiente de contrastar con Behind Bars.
- Siguiente tarea: F2.10, formato .tess.

## 2026-09-30 · F2.10 Formato .tess

- `Tessitura.IO` ahora referencia Core y Engraving (para `Style`, cuyo contexto JSON ya existía); Engraving no depende de IO, así que la prueba de arquitectura sigue intacta. Sin dependencias nuevas: `System.IO.Compression` y `System.Text.Json` con generación de código en compilación.
- `TessFile.Save/Open`: ZIP con `manifest.json` (versión de formato 1 y de la app), `score.json` (mediante DTO explícitos, orden determinista, `Fraction` como numerador/denominador, alturas escritas) y `style.json`. El guardado es atómico: temporal hermano, vaciado a disco y `File.Move` con sobrescritura.
- `TessMigrator` aplica la cadena de migraciones sobre el JSON de `score.json` desde la versión del manifiesto hasta la actual y rechaza archivos más nuevos que la aplicación. `RecoveryAutosave` escribe `<archivo>.recovery` cada 2 minutos (intervalo configurable en pruebas) y permite descartarlo.
- Cinco pruebas nuevas: ida y vuelta idéntica (metadatos, claves, armaduras, ligaduras, `EventId`, `Fraction`, estilo), reemplazo atómico sin temporales, migración de un archivo de versión 0, rechazo de versión futura y autosave. `dotnet build` 0 advertencias; `dotnet test` pasó 172 pruebas sin fallos ni omisiones.
- Deuda: aún no existen migraciones reales (la versión 1 es la primera; la prueba usa una migración sintética de la 0); `thumbnail.png` y `parts/` se añadirán con la pantalla de inicio (F2.11) y las partes (F4); guardar/abrir y el autoguardado aún no están conectados a acciones de la interfaz (necesitan selector de archivos y documento activo, F2.11).
- Siguiente tarea: F2.11, inicio y asistente.

## 2026-09-30 · F2.11 Inicio y asistente

- `NewScoreFactory` (Editing) crea la partitura de cada plantilla —piano (Sol y Fa), cuarteto de cuerda (violín I y II, viola en Do alto, violonchelo) y coro SATB (cuatro pentagramas)— con ocho compases de silencios en el compás y la armadura elegidos. Reutiliza el reparto de silencios de la normalización rítmica, así que cualquier compás produce compases válidos.
- `RecentScores` (IO) guarda hasta diez partituras recientes sin duplicados, de forma atómica; un archivo ausente o corrupto se trata como lista vacía. `StartScreenController` y `StartScreen` (App) muestran recientes, plantillas y «Abrir archivo…», y el asistente pide título, compositor, compás y armadura. Todas las acciones están registradas (`start.*`, con atajos) en su propio archivo `start-shortcuts.json`.
- `TessituraApplication` arranca en la pantalla de inicio y abre el editor mediante `EditorSession`, que además añade `file.save`, `file.save-as` y `file.close` (Ctrl+S, Ctrl+Shift+S, Ctrl+W), registra el archivo en recientes y mantiene el autoguardado de recuperación de F2.10. Con esto quedan conectados guardar, abrir y autoguardado a la interfaz.
- Pruebas nuevas (6): cada plantilla se crea con dos clics (menos de cinco) y estructura y claves correctas; las opciones del asistente llegan a la partitura y todos los compases son válidos; recientes visibles y abribles; lista de diez sin duplicados que sobrevive al reinicio y a la corrupción. `dotnet build` 0 advertencias; `dotnet test` pasó 178 pruebas sin fallos ni omisiones. Captura solo de la ventana de Tessitura por identificador: `docs/capturas/f2.11-macos-start-window.png`.
- Deuda: las miniaturas de recientes (`thumbnail.png`) quedan pendientes; faltan tamaño de página, tamaño de pentagrama, tempo y buscador de instrumentos del asistente completo (los instrumentos se limitan a las tres plantillas); los errores al abrir o guardar se muestran en el título de la ventana hasta el pulido F5.10; el aspecto de la pantalla de inicio es provisional (F5.10) y no sigue aún el tema claro/oscuro; no se probó a mano el selector de archivos del sistema.
- Siguiente tarea: F2.12, paleta de comandos.

## 2026-09-30 · F2.12 Paleta de comandos

- `CommandSearch` hace la búsqueda difusa sobre el `ActionRegistry`: ignora mayúsculas y acentos, cada palabra de la consulta debe aparecer como subsecuencia del nombre o del identificador, y puntúa más los caracteres contiguos, los inicios de palabra y las coincidencias exactas; con consulta vacía lista todo por nombre. `CommandPalette` es el panel superpuesto (cuadro de búsqueda y lista) con flechas, `Enter` y `Esc`; se abre con la acción registrada `command-palette.open` (`Ctrl+K`), que también lo cierra, y devuelve el foco al lienzo. Ejecuta siempre mediante `ActionRegistry.TryExecute`, así que no toca la partitura directamente.
- Tres pruebas nuevas: todas las acciones registradas (editor y ventana) aparecen con consulta vacía y cada una se encuentra y se ejecuta desde la paleta; la búsqueda difusa tolera acentos, mayúsculas y letras salteadas; elegir «clave fa» aplica la clave de Fa a la selección con deshacer. `dotnet build` 0 advertencias; `dotnet test` pasó 181 pruebas sin fallos ni omisiones.
- Deuda: no se comprobó a mano en la ventana real cómo se ve el panel (la aplicación aún no carga ningún tema de controles, así que el cuadro de texto y la lista pueden verse sin estilo hasta F5.10); la paleta no existe en la pantalla de inicio; el texto de la búsqueda no sigue todavía el tema claro/oscuro.
- Siguiente tarea: F2.13, copiar y pegar interno.

## 2026-09-30 · F2.13 Copiar y pegar interno

- `ClipboardFragment` copia una selección (eventos, o una sola nota de un acorde) como duraciones y desplazamientos relativos, independiente de la partitura. `PasteCommand` (con historial) pega con el primer instante en una posición absoluta: el pentagrama superior copiado cae en el pentagrama de destino, un fragmento de una voz cae en la voz de destino (varias voces conservan su número), los eventos pegados reciben `EventId` nuevos, sustituyen lo que solapan y la normalización rítmica rellena huecos con silencios, divide con ligaduras las notas que cruzan barra y añade compases si el fragmento excede el final. Pegar dentro de una nota se rechaza. Acciones registradas `edit.copy` (`Ctrl+C`) y `edit.paste` (`Ctrl+V`): el destino es el primer evento seleccionado o, en modo entrada, el cursor.
- Se corrigió un fallo heredado de F2.9: los compases que añadía la normalización rítmica perdían la armadura del compás anterior.
- Cinco pruebas nuevas: pegar cuatro compases en otro pentagrama conserva ritmo y alturas (con acordes, silencios, puntillos, ligaduras y alteraciones), voz y posición de destino, ampliación de la partitura, rechazo dentro de una nota y acciones con deshacer. `dotnet build` 0 advertencias; `dotnet test` pasó 146 pruebas en Engraving y 187 en total, sin fallos ni omisiones.
- Observación: la normalización escribe los silencios pulso a pulso (una blanca en el tercer tiempo de 4/4 se convierte en dos negras); es el comportamiento de F2.3 y no se cambió.

## 2026-09-30 · Puerta de F2

- Para poder cumplir la puerta «sin ratón» hacían falta acciones que ninguna tarea de F2 había incluido: `score.cursor.staff-up/down` (`Alt+↑/↓`), `score.cursor.start` (`Ctrl+Inicio`) y, para el bajo, la octava inicial según la clave. También la exportación a PDF no estaba conectada a la interfaz: se añadieron `ScorePdfExport` (Rendering, compone todas las páginas con el compositor real) y la acción `file.export-pdf` (`Ctrl+E`) en la sesión del editor.
- `F2GateTests` recorre la puerta completa solo con atajos de teclado por el `ActionRegistry` (sin eventos de ratón): crea un coral SATB con la plantilla, escribe cuatro compases en cuatro pentagramas (`N`, `5`, letras, `Ctrl+Inicio`, `Alt+↓`), comprueba que cada compás es válido y que las notas coinciden, guarda `.tess`, lo reabre y obtiene una partitura idéntica (ids, posiciones `Fraction`, duraciones, alturas, claves), y exporta a PDF. Evidencia: `docs/capturas/f2-gate-coral-satb.pdf`, `.tess` y `.png` (primera página, convertida con `sips`).
- Lo que la puerta no cubre y queda para el propietario: comprobar a mano la ventana real (selectores de archivo de guardar, abrir y exportar, y el aspecto de la paleta de comandos), y una pasada visual: los cuatro pentagramas del coral quedan muy juntos y las plicas de tenor y bajo se solapan; se abordará en F5.10 y con el espaciado vertical.
- Deuda: sin acciones de cambio de voz ni de mover el cursor por eventos, ni selección por teclado (solo ratón para elegir destino de pegado fuera del modo de entrada); copiar y pegar no usa el portapapeles del sistema.

## 2026-09-30 · Tema Fluent (aprobado por el propietario)

- Se añadió la dependencia `Avalonia.Themes.Fluent` 12.1.3 (MIT, misma versión que Avalonia.Desktop) fijada en `Directory.Packages.props` y se carga `FluentTheme` en `TessituraApplication`. Los controles ya tienen estilo en claro y oscuro; el interruptor de tema del editor sigue funcionando y la pantalla de inicio sigue el tema del sistema (se quitaron sus colores fijos). Captura de la ventana de inicio: `docs/capturas/theme-fluent-start-window.png`.
- Al cerrar una sesión de forma normal se borra la copia de recuperación del autoguardado; solo un cierre inesperado la conserva.
- Pendiente: revisar el aspecto de la paleta de comandos y de los cuadros del asistente con el tema en la ventana real; el pulido general sigue en F5.10.

## 2026-09-30 · Tema visual elegido

- Tras probar Fluent, Classic.Avalonia, Material.Avalonia y WPFDarkTheme, el propietario eligió el tema de WPFDarkTheme (AngryCarrot789, MIT). No existe como paquete NuGet: sus estilos (`Colours`, `ControlStyles`, `Controls`, `Converters`, `ControlColours.axaml`, `Controls.axaml` y las clases `GroupBox`/`WindowEx`) se copiaron a `src/Tessitura.App/Themes/AngryCarrot/` junto con su `LICENSE.txt` (commit 892ea59 del repositorio original), y se carga sobre `Avalonia.Themes.Simple` 12.1.3. Se retiró `Window.axaml` porque dependía de piezas de Avalonia 11 que cambiaron en 12; la ventana conserva su barra de título nativa. Se eliminó la dependencia de Fluent, que ya no se usa.
- Obligación de distribución (MIT): conservar el aviso de copyright y la licencia con las copias del código; queda en `Themes/AngryCarrot/LICENSE.txt`. Este código es de un tercero y lo mantenemos nosotros.
- Deuda: solo se revisó la pantalla de inicio; el editor, la paleta de comandos y el asistente necesitan una pasada visual con este tema (F5.10); el código original está pensado para Avalonia 11.

## 2026-09-30 · F3.1 Corpus MusicXML

- Se dio por aprobada la puerta de F2 con la instrucción del propietario de continuar el desarrollo tras el tema visual (F2.1–F2.13 y la prueba de puerta están documentadas arriba).
- Corpus público: `tests/Tessitura.IO.Tests/Corpus/public/` contiene los 183 archivos de la MusicXML Test Suite (`w3c-cg/musicxmlTestSuite`, fork de la suite de Lilypond hoy en el W3C Music Notation Community Group, commit `77c19f7`, licencia MIT conservada en `LICENSE.txt`). `Corpus/real/{dorico,sibelius,musescore}/` está preparado pero vacío: esos archivos los tiene que aportar el propietario, y aparecerán automáticamente en el informe.
- Métrica (`MusicXmlSignature`): lee la música directamente del XML, sin usar el modelo de Tessitura (partwise, `.mxl`, divisions, acordes, backup/forward, silencios, adornos, ligaduras), y compara dos archivos como multiconjuntos de eventos (parte, compás, instante, duración, altura escrita, ligadura); voz y pentagrama no cuentan porque los exportadores pueden renumerarlos. Un archivo es «sin pérdidas» si no falta ni sobra ningún evento; el informe da también el porcentaje de eventos conservados. El objetivo de la definición es ≥ 95 % del corpus.
- `FidelityReport` mide un `IMusicXmlRoundTrip` sobre el corpus y escribe Markdown y JSON. Hoy se mide `NotYetImplementedRoundTrip` (todo «no soportado», 0 %): F3.2 y F3.3 lo sustituyen por el importador y exportador reales en `TessituraRoundTrip.Current`. El CI genera `f3.1-musicxml-fidelity.md/.json` en cada sistema operativo y los sube como artefacto (`musicxml-fidelity-<os>`).
- Alcance de F3 (propuesta pendiente de aprobación): 47 de los 183 archivos —alturas, silencios, ritmo, compases, claves, armaduras, acordes, ligaduras de unión, varias partes/voces/pentagramas y el formato comprimido—; quedan fuera microtonos, tresillos, adornos, dinámicas y otras notaciones, letra, repeticiones, compases incompletos, cifrado, transpositores, etc., cada uno con su motivo en el informe. Las reglas están en `MusicXmlCorpus` y se ajustan en un solo sitio.
- Pruebas nuevas (4): el corpus público está completo, clasificado y se lee entero; la firma lee correctamente acordes, backup, cambios de divisions, silencios, adornos y ligaduras; la métrica distingue copia exacta, pérdida, no soportado y fallo; el informe se genera para todo el corpus. `dotnet build` 0 advertencias; `dotnet test` pasó 191 pruebas sin fallos ni omisiones.
- Deuda: sin archivos reales de Dorico, Sibelius y MuseScore la tarea queda cumplida solo para el corpus público; la firma no cubre `score-timewise` ni transposición; los tresillos toman la duración de `<duration>`.

## 2026-09-30 · F3.2 Importador MusicXML

- `MusicXmlImporter` (IO) importa `score-partwise` en `.musicxml`, `.xml` y `.mxl` (contenedor comprimido) y devuelve la partitura y una lista de avisos (`MusicXmlImportWarning`) para mostrar al usuario. Es tolerante: solo lanza `InvalidDataException` si el archivo no es XML bien formado o no es una partitura por partes con al menos una parte; todo lo demás se omite o aproxima con aviso.
- Qué conserva: partes como instrumentos y pentagramas (`<staves>`), clave inicial (Sol, Fa, Do alto y tenor), armadura y compás compás a compás (los compases aditivos o mixtos se convierten en un único compás de la misma duración, con aviso), acordes, silencios, puntillos, ligaduras de unión (`<tie>` y `<tied>`), voces (hasta cuatro por pentagrama) y título y compositor. Los cambios de `divisions`, `backup` y `forward` se resuelven con `Fraction`, sin redondeos. Alteraciones no enteras se aproximan al semitono con aviso.
- Qué omite con aviso: notas de adorno, percusión sin altura, cambios de clave dentro de la pieza, armaduras no tradicionales, más de cuatro voces y notas más largas que una redonda; los tresillos se escriben con su valor gráfico (el modelo aún no tiene grupos irregulares). La colocación en la rejilla de compases reutiliza la normalización rítmica: para ello Editing expone dos piezas públicas nuevas, `NormalizeVoiceCommand` y `NewScoreFactory.CreateMeasureRests`; IO pasa a referenciar Editing.
- Prueba de fidelidad de importación: para los 44 archivos legibles dentro del alcance de F3, las notas de la partitura importada coinciden exactamente (compás, instante, duración, altura escrita y ligadura) con las del XML original, y todos los compases quedan cubiertos por completo. Se reajustó el alcance: `03a` y `11d` (notas más largas que una redonda) pasan a fuera de alcance. La firma de comparación se corrigió para cambios de `divisions` a mitad de compás.
- Seis pruebas nuevas: importa todo el corpus dentro del alcance sin excepciones; conserva todas las notas y compases válidos; partes, pentagramas, claves, armaduras, compases, voces y ligaduras en archivos concretos; `.mxl` y compás aditivo con aviso; material fuera del alcance se omite con avisos sin fallar; rechazo de archivos que no son MusicXML. `dotnet build` 0 advertencias; `dotnet test` pasó 197 pruebas sin fallos ni omisiones.
- Deuda: la comparación mide notas, no silencios (los silencios se recolocan al compás y a los pulsos); las voces con silencios en compases donde no había notas aparecen con silencios completos; sin archivos reales de Dorico, Sibelius y MuseScore solo se mide el corpus público; `score-timewise` no se importa.
- Siguiente tarea: F3.3, exportador MusicXML.

## 2026-09-30 · F3.3 Exportador MusicXML

- `MusicXmlExporter` (IO) escribe MusicXML 4.0 `score-partwise` (con su DOCTYPE), en `.musicxml` o comprimido `.mxl`, con guardado atómico. Una parte por instrumento; `divisions` elegido como el mínimo que hace enteras todas las duraciones; clave (Sol, Fa, Do alto y tenor), armadura y compás en el primer compás y cuando cambian; pentagramas con `<staves>` y `<staff>`; voces con `<backup>`; acordes; alteraciones; puntillos; tipos de figura; ligaduras de unión con su `stop` en la nota siguiente de la misma voz (con `<tie>` y `<tied>`); título, compositor y software de codificación. Las voces distintas de la primera que solo contienen silencios (restos de la maquetación) no se exportan.
- Validación: los esquemas oficiales 4.0 (`musicxml.xsd`, `xml.xsd`, `xlink.xsd`, etiqueta `v4.0` de w3c/musicxml, commit `799e2de`, W3C Community Final Specification Agreement) están en `tests/Tessitura.IO.Tests/Schema/` con su aviso; las pruebas validan con `XmlSchemaSet` resolviendo las URL oficiales a los archivos locales.
- Resultados sobre el corpus público: los 183 archivos se importan y se exportan a MusicXML que valida contra el esquema (los que la importación rechaza por no ser MusicXML utilizable se descartan de la prueba), y la ida y vuelta sin pérdidas dentro del alcance de F3 es del 95,5 % de los archivos (42 de 44) y del 98,8 % de las notas, por encima del 90 % exigido y del 95 % objetivo de la definición. Los dos archivos con pérdidas son `33i-Ties-NotEnded` y `33k-Tie-Types`, con ligaduras sin pareja o de tipos especiales (`let-ring`), que el modelo no representa.
- La métrica del informe se ajustó a notas (instante, duración, altura escrita y ligadura) y excluye los silencios: la normalización rítmica los reescribe a los pulsos, lo cual no es una pérdida musical. `TessituraRoundTrip.Current` ya usa el importador y el exportador reales, y el CI genera el informe con esas cifras.
- Seis pruebas nuevas: la exportación valida; ida y vuelta de acordes, ligaduras, pentagramas y voces; marcas de inicio y fin de ligadura; guardado `.musicxml` y `.mxl` atómico; todo el corpus exporta a MusicXML válido; al menos el 90 % del corpus dentro del alcance vuelve sin pérdidas. `dotnet build` 0 advertencias; `dotnet test` pasó 203 pruebas sin fallos ni omisiones.
- Deuda: la exportación no incluye dinámicas, articulaciones, letra, tresillos ni notas de adorno (aún no están en el modelo); tampoco distancias ni maquetación; sin archivos reales de Dorico, Sibelius y MuseScore solo se valida contra el corpus público; no hay acción de menú para importar o exportar (llegará con la interfaz de archivos).
- Siguiente tarea: F3.4, MIDI de archivo.

## 2026-09-30 · F3.4 MIDI de archivo

- `MidiExporter` (IO, con DryWetMIDI 8.0.3, MIT, ya incluida en el stack) escribe SMF tipo 1 a 960 pulsos por negra (permite una fusa con puntillo exacta): una pista de director con tempo, compás y armadura, y una pista por instrumento con su nombre y un canal propio (se salta el canal 10 de percusión). Las notas ligadas se funden en una sola nota sonora. El tempo es constante (120 por defecto y configurable) y la velocidad fija en 80 hasta que exista el modelo de interpretación (F3.5). El guardado es atómico.
- `MidiImporter` lee SMF con división en pulsos por negra: cada pista con notas (o cada canal de un archivo de tipo 0) es una parte, la clave es Fa si la mediana de alturas es grave, los compases y armaduras salen de los eventos de la pista (un cambio se aplica desde la siguiente barra), y los ataques y finales se cuantizan a una rejilla configurable (`MidiImportOptions.GridDivisor`, semicorcheas por defecto; una nota más corta que la rejilla dura una casilla). Las notas simultáneas con igual inicio y fin forman acordes, las que se solapan van a hasta cuatro voces, las largas se parten en las barras y en valores escritos con ligaduras, y las alturas se escriben con sostenidos o bemoles según la armadura. La percusión (canal 10) se omite con aviso.
- Reutiliza la colocación en la rejilla del importador de MusicXML mediante `VoiceWriter`, extraído para ambos.
- Siete pruebas nuevas, entre ellas el criterio «Hecho cuando»: para todos los archivos del corpus dentro del alcance con notas, exportar a MIDI y reimportar (rejilla de 128avos) conserva exactamente cada nota sonora —altura MIDI, instante y final, con ligaduras fundidas—; además estructura del SMF tipo 1, cuantización de tiempos humanizados a dos rejillas, ortografía por armadura y partición con ligaduras, voces y acordes, percusión y archivos rotos, y guardado atómico. `dotnet build` 0 advertencias; `dotnet test` pasó 210 pruebas sin fallos ni omisiones.
- Deuda: el modelo no guarda tempo ni dinámicas, así que la importación descarta el tempo y la velocidad y la exportación usa valores fijos; MIDI no distingue enarmónicos ni tresillos (los grupos irregulares se cuantizan a la rejilla); no hay acciones de menú para importar o exportar MIDI y MusicXML todavía.
- Siguiente tarea: F3.5, modelo de interpretación.

## 2026-09-30 · F3.5 Modelo de interpretación

- Decisión de alcance (sin tocar el modelo ni el formato `.tess`): la definición prevé dinámicas, articulaciones y tempo en el modelo (adjuntos, spanners y compases), pero esas piezas entran en F4.4, F4.5 y F4.7. Para no adelantar cambios de dominio, la interpretación recibe esas marcas por un objeto propio, `PerformanceHints` (dinámicas y articulaciones ancladas a un `EventId`, y marcas de tempo por posición `Fraction`), vacío por defecto. Cuando F4 añada los adjuntos al modelo, bastará con convertirlos a `PerformanceHints`. Playback sigue dependiendo solo de Core.
- `Interpreter.Interpret(score, hints, settings)` devuelve las notas a tocar (`PerformedNote`: instrumento, pentagrama, voz, altura MIDI, inicio y longitud escrita y sonora en `Fraction`, velocidad) ordenadas por inicio, instrumento y altura, con las ligaduras de unión fundidas en una sola nota, más un `TempoMap`. La altura MIDI sale de la altura escrita hasta que llegue la transposición (F4.12). `TempoMap` convierte posiciones exactas a segundos con tempos constantes por tramos (marcas desordenadas, la última del mismo punto gana); el único paso a `double` es la conversión final a tiempo real. Repeticiones, casillas, D.C. y D.S. se desplegarán en F4.9, y los reguladores y cambios graduales de tempo con los spanners.
- Valores (`InterpretationSettings`, todos ajustables): staccato 1/2 y tenuto 1 —fijados por la definición—; sin articulación 9/10, staccatissimo 1/4 y marcato 17/20 (elección de Tessitura); si se combinan articulaciones manda la puerta más corta; velocidades ppp 16, pp 33, p 49, mp 64, mf 80, f 96, ff 112, fff 126 (mf por defecto), acento +16 y marcato +24 con tope en 127. La dinámica rige desde el evento anclado hasta la siguiente en todos los pentagramas del instrumento.
- Nuevo proyecto de pruebas `Tessitura.Playback.Tests` (añadido a la solución). 14 pruebas: los valores de la definición (staccato 50 %, tenuto 100 %) y el resto de articulaciones, velocidades por dinámica y su alcance, límites de velocidad, combinación de articulaciones, fusión de ligaduras entre compases, orden de las notas y mapa de tempo con cambios, marcas desordenadas y tempos inválidos. `dotnet build` 0 advertencias; `dotnet test` pasó 224 pruebas sin fallos ni omisiones.
- Deuda: el exportador MIDI todavía usa velocidad fija y tempo constante; pasará a consumir esta interpretación con el secuenciador (F3.6).
- Siguiente tarea: F3.6, secuenciador y audio.

## 2026-09-30 · F3.6 Secuenciador y audio (parcial: sin marcar en el plan)

- `SequenceData` convierte una `Interpretation` en órdenes del sintetizador con el tiempo en muestras (canal por instrumento sin el 10, cambio de programa General MIDI, notas y liberaciones ordenadas por muestra). `Sequencer` (Playback) las ejecuta en el hilo de audio con reloj de muestras: divide cada bloque en el instante exacto de cada orden, reproduce, pausa, salta y sigue sonando la cola de liberación; se puede reprogramar mientras suena (`Load`) manteniendo la posición. Todo lo que toca el hilo de audio está preasignado y los controles solo publican valores con `Volatile`/`Interlocked`, sin bloqueos.
- `IAudioOutput` y `MiniAudioOutput`: implementación con la API de bajo nivel de MiniAudioExNET (`MaDevice`, instanciable, sin la API estática usada solo en el experimento de F0.11), estéreo `float` a 48 kHz, periodo de 240 fotogramas (5 ms) y dos periodos: 10 ms de búfer, por debajo del objetivo de 20 ms. Cuenta callbacks y callbacks tardíos (intervalo mayor que dos periodos) como indicio de cortes. Playback pasa a referenciar MeltySynth y MiniAudioEx (ya en el stack) y permite código `unsafe` para leer el búfer nativo.
- Siete pruebas nuevas (21 en Playback.Tests): órdenes en muestras exactas y en orden; canales sin la percusión; el primer ataque cae en la muestra exacta y no suena nada antes; el audio es idéntico bit a bit con bloques de 1, 7, 64, 100, 480, 1000 y 4096 fotogramas; **cero bytes asignados** en 1500 bloques con notas en marcha (medido con `GC.GetAllocatedBytesForCurrentThread`); transporte (reproducir, pausar, saltar, final de pieza, reinicio); reprogramación en marcha. `dotnet build` 0 advertencias; `dotnet test` pasó 231 pruebas sin fallos ni omisiones.
- Comando de medición con dispositivo real: `dotnet run --project tests/Tessitura.Benchmarks --configuration Release --no-build -- audio-play [segundos] [soundfont.sf2]` reproduce la partitura de referencia (30 pentagramas, 300 compases) y da el búfer, callbacks, callbacks tardíos, bytes asignados y colecciones del GC. En este Mac (macOS 27, Apple M5, altavoces integrados, SoundFont de prueba MIT): 6 s de reproducción, 1199 callbacks (5 ms cada uno), 1 callback tardío, 352 bytes asignados en todo el proceso y 0 colecciones del GC. Este número no es latencia acústica.
- **Pendiente para cerrar F3.6 (por eso sigue sin marcar):** (1) elegir e incluir el SoundFont de licencia libre —el de pruebas es solo un recurso de test—; (2) reproducción y latencia física en Windows y Linux, que el propietario no puede medir hasta disponer de esos equipos: el comando anterior está preparado para ejecutarlo allí; (3) medir la latencia hasta el altavoz de extremo a extremo.

## 2026-09-30 · F3.7 Cabeza de reproducción

- `PlaybackController` (App) reproduce la partitura desde el cursor con `Espacio` (`playback.toggle`) y detiene y rebobina con `Ctrl+Espacio`; al editar mientras suena reprograma el secuenciador sin cortar el sonido. Un temporizador de 16 ms llama a `Tick`, que mueve el cursor de la partitura a la posición audible (reloj de muestras menos el búfer del dispositivo, convertida con `TempoMap.PositionAt`); como el cursor decide qué sistema se compone, la vista sigue la reproducción sin código adicional. `ScoreInputController.SetCursorPosition` mueve el cursor sin tocar el historial.
- Tres pruebas nuevas con una salida de audio simulada: en 240 fotogramas de pantalla el desfase máximo entre el cursor y el sonido es inferior a un fotograma (16,7 ms); pausar conserva el cursor y detener lo rebobina; reproducir desde el cursor y parar solo al final. `IsPlaying` cuenta también la orden aún no recogida por el hilo de audio. `dotnet build` 0 advertencias; `dotnet test` pasó 234 pruebas sin fallos ni omisiones.
- El editor espera el SoundFont en `assets/soundfonts/default.sf2`; sin él muestra el aviso en el título. Incluirlo depende de la decisión pendiente de F3.6. El desfase con el altavoz real no se ha medido.

## 2026-09-30 · F3.8 Mezclador

- `Mixer` (Playback) guarda volumen, panorama, silencio y solo por instrumento; el secuenciador consulta su versión al inicio de cada bloque y, si cambió, envía los controladores MIDI 7 (volumen, a cero si está silenciado o hay otro en solo) y 10 (panorama) sin asignar memoria, de modo que cada control actúa en tiempo real. `GeneralMidiPrograms` asigna automáticamente el programa General MIDI según el nombre del instrumento (español o inglés; piano por defecto) y `PlaybackController` lo pasa a la secuencia.
- Acciones registradas por instrumento (los nueve primeros): silenciar (`Alt+Mayús+n`), solo (`Ctrl+Mayús+n`) y volumen (`Alt+Ctrl+n` y con Mayús); el panel inferior muestra una tira por instrumento con los mismos controles como botones. El panorama tiene API y prueba pero aún no acción ni control visual.
- 14 pruebas nuevas en Playback.Tests: volumen efectivo con silencio y solo; silenciar y reactivar durante la reproducción cambia la energía del audio en los bloques siguientes; solo silencia al resto; el panorama mueve el sonido entre canales; cero asignaciones al cambiar el mezclador con el audio en marcha; mapeo de nombres a programas. `dotnet build` 0 advertencias; `dotnet test` pasó 248 pruebas sin fallos ni omisiones.

## 2026-09-30 · F3.9 MIDI de dispositivos (parcial: sin marcar en el plan)

- `IMidiPort` (Playback) abstrae enumeración, entrada, salida, envío y desconexión; `DryWetMidiPort` lo implementa en Windows y macOS con DryWetMIDI 8.0.3 (detecta la desconexión con `PollConnection` y errores de envío) y `UnsupportedMidiPort` cubre Linux hasta que exista el adaptador ALSA aprobado en F0.12. `MidiStepInput` convierte las notas del teclado en notación en modo entrada: las teclas pulsadas dentro de 60 ms, o mantenidas, forman un acorde que se escribe al soltarlas (con ligadura a sostenidos o bemoles según la armadura) mediante `ScoreInputController.EnterChord`, deshacible paso a paso. Acciones `midi.connect` (`Ctrl+Alt+M`) y `midi.disconnect`; los mensajes se pasan al hilo de la interfaz.
- Cinco pruebas nuevas: acorde de teclado en un evento, notas sucesivas y ortografía por armadura, nada fuera del modo de entrada, deshacer, y puertos según sistema. `dotnet test` pasó 257 pruebas sin fallos ni omisiones. También se estabilizaron las pruebas de cero asignaciones (espera al JIT por niveles).
- **Pendiente para cerrar F3.9 (sin marcar):** el adaptador ALSA de Linux y las comprobaciones con teclados físicos en Windows y Linux (enumeración, recepción, envío, desconexión), que exigen esos equipos; en este Mac solo hay puerto virtual.

## 2026-09-30 · F3.10 Exportación SVG y PNG

- `ImageExporter` (Rendering) dibuja una página con el mismo paso de dibujo que el PDF: `ExportPng` con `SKSurface` sobre fondo blanco y resolución elegible de 36 a 1200 ppp, y `ExportSvg` con `SKSvgCanvas`, con el tamaño de página en puntos. Guardado atómico. A 72 ppp la imagen tiene exactamente los píxeles de la página del PDF.
- Dos pruebas: el PNG a 72 ppp coincide con el `MediaBox` del PDF y a 300 ppp con `PixelSize`; el SVG tiene el mismo ancho y alto en puntos y contiene los trazos; la tinta del PNG a 300 ppp reducida equivale a la de 72 ppp (misma pasada de dibujo); resoluciones fuera de rango se rechazan. `dotnet test` pasó 259 pruebas sin fallos ni omisiones. La coincidencia con el PDF sobre el catálogo completo de referencia de F1.16 y las acciones de menú de exportación quedan como deuda (F5).

## 2026-09-30 · Puerta de F3 (evidencia)

- Ida y vuelta MusicXML: 95,5 % de los archivos del corpus público dentro del alcance sin pérdidas (objetivo de la puerta: 90 %). La partitura de referencia suena con el cursor sincronizado en pruebas con salida simulada (desfase < 1 fotograma) y 6 s reales en este Mac con miniaudio; sin asignaciones en el hilo de audio.
- Pendiente que exige tu intervención, ya listado en F3.6 y F3.9: SoundFont incluido, aprobación del alcance de F3, archivos reales de Dorico/Sibelius/MuseScore y comprobaciones físicas en Windows y Linux (audio, teclado MIDI, adaptador ALSA). Por indicación del propietario se continúa con F4.
