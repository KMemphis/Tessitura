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
