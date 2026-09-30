## Plan de ejecución

El plan divide las seis fases de la hoja de ruta en 75 tareas secuenciales, cada una de medio día a dos días de trabajo del agente y con un criterio verificable de «Hecho cuando».

- El agente toma siempre la primera tarea sin marcar y la marca con `[x]` al cumplir su criterio, con build y pruebas en verde.
- Cada fase termina en una **puerta**: el agente presenta la evidencia y se detiene hasta tu aprobación.
- Las tareas marcadas como **Decisión** terminan en un informe en `docs/decisiones/` y requieren que elijas antes de continuar.
- Si una tarea resulta demasiado grande, el agente la divide en subtareas (F1.8a, F1.8b) dentro del mismo archivo, sin cambiar el orden.

La reproducción básica que el alcance incluye en el MVP llega en F3; la versión interna de F2 es un MVP de escritura, todavía sin sonido.

## F0 · Fundamentos (semanas 1–6)

- [x] **F0.1 · Solución y estructura.** Crear `Tessitura.sln` con los 9 proyectos de `src/` y los 4 de `tests/`, `Directory.Build.props` (net10.0, nullable, TreatWarningsAsErrors) y `Directory.Packages.props`. *Hecho cuando:* `dotnet build` compila sin advertencias.
- [x] **F0.2 · Integración continua.** GitHub Actions con matriz Windows, macOS y Ubuntu que compila y prueba. *Hecho cuando:* el pipeline está en verde en los tres sistemas.
- [x] **F0.3 · Prueba de arquitectura.** Test que inspecciona las referencias de ensamblado de Core, Smufl y Engraving y falla si aparecen Avalonia o SkiaSharp. *Hecho cuando:* pasa, y falla al añadir una referencia prohibida a propósito.
- [x] **F0.4 · Fraction.** Struct siempre reducida con suma, resta, multiplicación, división y comparación. *Hecho cuando:* pruebas de propiedades con FsCheck verifican reducción, identidad y asociatividad.
- [x] **F0.5 · Pitch e Interval.** Altura escrita, `MidiNumber` y `Transpose` por intervalo diatónico y cromático. *Hecho cuando:* Do4 más tercera mayor da Mi4, Si#3 y Do4 comparten número MIDI pero no son iguales, y una transposición de ida y vuelta devuelve la altura original.
- [x] **F0.6 · Duration.** Figura más puntillos con longitud en `Fraction`. *Hecho cuando:* negra con doble puntillo vale 7/16.
- [x] **F0.7 · Modelo mínimo.** Records inmutables `Score`, `Instrument`, `Staff`, `Measure`, `StaffMeasure`, `Voice`, `Chord`, `Rest`, `Note` y `EventId`, más el validador de la invariante de compás. *Hecho cuando:* un compás de 4/4 con cuatro negras valida y uno con cinco falla.
- [x] **F0.8 · Tessitura.Smufl.** Incluir Bravura y sus metadatos en `assets/fonts`; cargar `engravingDefaults`, cajas de glifos, anclajes y el mapa de nombres de glifo. *Hecho cuando:* el anclaje `stemUpSE` de `noteheadBlack` coincide con el JSON.
- [x] **F0.9 · Ventana y ScoreCanvas.** Ventana Avalonia con un control propio que dibuja con `ICustomDrawOperation` e `ISkiaSharpApiLeaseFeature`, con zoom (`Ctrl+rueda`) y desplazamiento. *Hecho cuando:* se ve una página A4 blanca con sombra y el zoom es fluido.
- [x] **F0.10 · Primer dibujo musical.** Dibujar a mano, sin motor de grabado, un pentagrama con clave de sol, una negra con plica y un sostenido usando solo métricas SMuFL. *Hecho cuando:* la plica encaja en el anclaje y hay capturas de las tres plataformas en la bitácora.
- [x] **F0.11 · Decisión: salida de audio.** Tocar una nota con MeltySynth usando OpenAL Soft (Silk.NET) y usando miniaudio, en los tres sistemas, midiendo latencia. *Hecho cuando:* existe `docs/decisiones/audio.md` con la comparación y una recomendación; el agente se detiene. **Decisión aprobada:** miniaudio; reproducción y medición física en Windows/Linux trasladadas a F3.6 por aprobación del 29 de septiembre de 2026.
- [x] **F0.12 · Decisión: MIDI.** Listar dispositivos y recibir notas con DryWetMIDI en los tres sistemas, con alternativa para Linux si falla. *Hecho cuando:* existe `docs/decisiones/midi.md` con resultados; el agente se detiene. **Decisión aprobada:** DryWetMIDI en Windows/macOS y adaptador ALSA en Linux; las pruebas con teclado físico en Windows/Linux se trasladan a F3.9 por aprobación del 29 de septiembre de 2026.

**Puerta F0:** una nota con plica y alteración dibujada con métricas exactas en las tres plataformas, CI en verde, y las decisiones de audio y MIDI tomadas. La validación física de audio y MIDI en Windows/Linux, autorizada para F3.6 y F3.9 respectivamente, queda explícitamente pendiente.

## F1 · Motor de grabado v0 (semanas 7–16)

- [x] **F1.1 · Lista de dibujo.** Primitivas `Glyph`, `Line`, `Path`, `Text` y `Rect` con `ElementId` y caja, agrupadas en `Page`, dentro de Engraving y sin SkiaSharp. *Hecho cuando:* las primitivas se serializan y comparan por valor en pruebas.
- [x] **F1.2 · Renderer.** `DisplayListRenderer` en Rendering que dibuja una `Page` sobre un `SKCanvas` y la graba como `SKPicture`. *Hecho cuando:* la nota de F0.10 se dibuja desde una lista de dibujo con el mismo resultado.
- [x] **F1.3 · Imágenes de referencia.** Infraestructura que renderiza un ejemplo a PNG, lo compara con la versión aprobada con tolerancia y guarda la diferencia. *Hecho cuando:* un test falla al alterar un glifo y pasa al aprobar la nueva imagen.
- [x] **F1.4 · Style.** Objeto de estilo con valores por defecto de `engravingDefaults` y de Behind Bars (longitud de plica 3,5 espacios, separaciones mínimas). *Hecho cuando:* cada valor cita su fuente y se carga y guarda en JSON.
- [x] **F1.5 · Alteraciones.** Resolución por compás, armadura y ligaduras de unión. *Hecho cuando:* pasan al menos 15 casos, incluidos becuadros de cancelación y alteraciones que no se repiten tras una ligadura.
- [x] **F1.6 · Plicas y barras automáticas.** Dirección de plica según la línea central y agrupación de corcheas por pulsos en 2/4, 3/4, 4/4, 2/2, 6/8 y 9/8. *Hecho cuando:* los casos de cada compás agrupan como indica Behind Bars.
- [x] **F1.7 · Segmentos rítmicos.** Columnas que alinean los eventos simultáneos de todos los pentagramas. *Hecho cuando:* piano a dos pentagramas con ritmos distintos alinea cada instante común.
- [x] **F1.8 · Espaciado horizontal.** Fórmula logarítmica de la definición más ancho mínimo anticolisión; cabecera de sistema con clave, armadura y compás. *Hecho cuando:* una blanca ocupa entre 1,4 y 1,7 veces una negra y ninguna alteración toca la nota anterior.
- [x] **F1.9 · Colocación en el pentagrama.** Cabezas, plicas, alteraciones, puntillos, silencios y líneas adicionales. *Hecho cuando:* imágenes de referencia de una escala de cuatro octavas y de todos los silencios.
- [x] **F1.10 · Barras de corchea.** Inclinación limitada, grosor de SMuFL, barras múltiples y parciales. *Hecho cuando:* imágenes de referencia de grupos ascendentes, descendentes y mixtos.
- [x] **F1.11 · Saltos de sistema y justificación.** Programación dinámica tipo Knuth-Plass y reparto del sobrante por elasticidad. *Hecho cuando:* ningún sistema excepto el último queda por debajo del 80 % del ancho sin estirar en exceso.
- [x] **F1.12 · Páginas y espaciado vertical.** Saltos de página y separación de pentagramas con skylines básicos. *Hecho cuando:* una partitura de 10 páginas no tiene solapes entre pentagramas.
- [x] **F1.13 · Textos de página.** Título, compositor y números de compás con HarfBuzzSharp. *Hecho cuando:* imágenes de referencia de la primera página.
- [ ] **F1.14 · Exportación PDF.** `SKDocument.CreatePdf` con fuentes incrustadas. *Hecho cuando:* el PDF abre en tres visores y coincide con la pantalla.
- [ ] **F1.15 · Maquetación incremental y rendimiento.** Caché de anchos por compás, reflujo limitado, cancelación y un generador de la partitura de referencia (30 pentagramas × 300 compases). *Hecho cuando:* BenchmarkDotNet mide maquetación completa por debajo de 1,5 s y un cambio de una nota por debajo de 10 ms.
- [ ] **F1.16 · Catálogo de referencia.** 20 ejemplos (escalas, ritmos, armaduras, compases compuestos, piano, cuarteto). *Hecho cuando:* los 20 están aprobados por ti.

**Puerta F1:** 20 ejemplos de referencia aprobados y maquetación completa de la partitura de referencia dentro del presupuesto.

## F2 · Editor MVP (semanas 17–26)

- [ ] **F2.1 · ActionRegistry y atajos.** Registro central de acciones con identificador, nombre y atajo, y atajos configurables en JSON. *Hecho cuando:* cambiar un atajo en el JSON cambia el comportamiento sin recompilar.
- [ ] **F2.2 · Comandos e historial.** `IScoreCommand`, `History` y los comandos de insertar y borrar nota, cambiar altura, duración, alteración, puntillo y ligadura de unión. *Hecho cuando:* cualquier secuencia de comandos deshecha por completo devuelve una partitura idéntica a la inicial (prueba de propiedades).
- [ ] **F2.3 · Notación rítmica automática.** Relleno con silencios, división con ligadura al cruzar la barra y reescritura de silencios según el compás. *Hecho cuando:* la invariante de compás se cumple tras cualquier comando.
- [ ] **F2.4 · Cursor y modo de entrada.** Modo de entrada (`N`, `Esc`), cursor musical en la capa superpuesta, octava más cercana y atajos de la definición. *Hecho cuando:* se escribe una escala de Do mayor solo con el teclado.
- [ ] **F2.5 · Detección de clics y selección.** Índice espacial por página y selección de elemento, rango y lista. *Hecho cuando:* un clic sobre una cabeza selecciona su nota y `Shift+clic` extiende un rango.
- [ ] **F2.6 · Ciclo de actualización.** Comando, maquetación en segundo plano, invalidación de páginas y repintado. *Hecho cuando:* escribir una nota en la partitura de referencia tarda menos de 16 ms hasta verse.
- [ ] **F2.7 · Estructura de la ventana.** Barra superior, paneles laterales plegables, barra de estado y temas claro y oscuro. *Hecho cuando:* coincide con la distribución de la definición.
- [ ] **F2.8 · Inspector.** Propiedades del elemento seleccionado, editables mediante comandos. *Hecho cuando:* cambiar la duración desde el inspector se puede deshacer.
- [ ] **F2.9 · Paletas básicas.** Claves, armaduras, compases y alteraciones. *Hecho cuando:* cada elemento se aplica a la selección con un clic.
- [ ] **F2.10 · Formato .tess.** ZIP con manifiesto versionado, guardado atómico, marco de migraciones y autoguardado cada 2 minutos. *Hecho cuando:* guardar y abrir devuelve una partitura idéntica, y un archivo de versión anterior se migra.
- [ ] **F2.11 · Inicio y asistente.** Pantalla de inicio con recientes y asistente de nueva partitura con plantillas de piano, cuarteto de cuerda y coro SATB. *Hecho cuando:* se crea cada plantilla en menos de cinco clics.
- [ ] **F2.12 · Paleta de comandos.** `Ctrl+K` con búsqueda difusa sobre el ActionRegistry. *Hecho cuando:* toda acción registrada aparece y se ejecuta desde la paleta.
- [ ] **F2.13 · Copiar y pegar interno.** Copia de rangos y pegado respetando voz y pentagrama de destino. *Hecho cuando:* pegar cuatro compases en otro pentagrama conserva ritmo y alturas.

**Puerta F2:** escribir un coral SATB completo sin ratón, guardarlo, reabrirlo y exportarlo a PDF. Versión MVP interna.

## F3 · Interoperabilidad y sonido (semanas 27–34)

- [ ] **F3.1 · Corpus MusicXML.** Reunir la MusicXML Test Suite pública y archivos reales exportados desde Dorico, Sibelius y MuseScore, con una métrica de fidelidad de ida y vuelta. *Hecho cuando:* el informe de fidelidad se genera en CI.
- [ ] **F3.2 · Importador MusicXML.** Formatos `.musicxml` y `.mxl`, tolerante con errores y con avisos al usuario. *Hecho cuando:* importa sin excepciones el 100 % del corpus dentro del alcance de F3.
- [ ] **F3.3 · Exportador MusicXML.** Versión 4.0 validada contra el esquema. *Hecho cuando:* toda exportación valida y el 90 % del corpus vuelve sin pérdidas.
- [ ] **F3.4 · MIDI de archivo.** Importación con cuantización y exportación SMF tipo 1. *Hecho cuando:* una partitura exportada y reimportada conserva alturas y ritmos.
- [ ] **F3.5 · Modelo de interpretación.** Mapa de tempo, dinámicas a velocidad y articulaciones a duración. *Hecho cuando:* pruebas unitarias de los valores de la definición (staccato al 50 %, tenuto al 100 %).
- [ ] **F3.6 · Secuenciador y audio.** Secuenciador en muestras, `IAudioOutput` con miniaudio, MeltySynth y un SoundFont con licencia libre incluido. *Hecho cuando:* suena la partitura de referencia sin cortes y el hilo de audio no asigna memoria; además, se comprueban reproducción y latencia física en Windows y Linux y el búfer objetivo inferior a 20 ms.
- [ ] **F3.7 · Cabeza de reproducción.** Transporte, `Espacio`, cursor sincronizado con el reloj de audio y seguimiento de la vista. *Hecho cuando:* el desfase entre cursor y sonido es inferior a un fotograma.
- [ ] **F3.8 · Mezclador.** Volumen, panorama, silencio y solo por instrumento, y programa General MIDI automático. *Hecho cuando:* cada control actúa en tiempo real.
- [ ] **F3.9 · MIDI de dispositivos.** Salida MIDI externa y entrada MIDI paso a paso con `IMidiPort`, usando DryWetMIDI en Windows/macOS y un adaptador ALSA en Linux. *Hecho cuando:* un teclado MIDI escribe acordes en el modo de entrada y se comprueban enumeración, recepción, envío y desconexión con dispositivos físicos en Windows y Linux.
- [ ] **F3.10 · Exportación SVG y PNG.** `SKSvgCanvas` y `SKSurface` con resolución elegible. *Hecho cuando:* ambas coinciden con el PDF en las imágenes de referencia.

**Puerta F3:** 90 % del corpus MusicXML de ida y vuelta sin pérdidas, y la partitura de referencia suena con el cursor sincronizado.

## F4 · Notación avanzada y partes (semanas 35–46)

- [ ] **F4.1 · Varias voces.** Hasta 4 voces por pentagrama con plicas por voz y desplazamiento de cabezas en colisión. *Hecho cuando:* imágenes de referencia de dos y cuatro voces sin solapes.
- [ ] **F4.2 · Grupos irregulares.** Tresillos, cinquillos y anidados, con corchete o número. *Hecho cuando:* la duración de cada grupo cuadra exactamente con `Fraction`.
- [ ] **F4.3 · Acordes.** Cabezas en segundas, apilado de alteraciones en columnas y puntillos en acordes. *Hecho cuando:* imágenes de referencia de clusters y acordes con cinco alteraciones.
- [ ] **F4.4 · Articulaciones y ornamentos.** Colocación según plica y orden de apilado de Behind Bars. *Hecho cuando:* imágenes de referencia aprobadas.
- [ ] **F4.5 · Dinámicas y popovers.** Dinámicas, tempo y texto con `Shift+D`, `Shift+T` y `Shift+X`. *Hecho cuando:* escribir `mf`, `q=120` o `Cmaj7` crea el elemento correcto.
- [ ] **F4.6 · Ligaduras de expresión.** Curvas Bézier que esquivan cabezas, plicas y articulaciones, incluso entre sistemas. *Hecho cuando:* imágenes de referencia de ligaduras largas, cortas y partidas.
- [ ] **F4.7 · Líneas.** Reguladores, octavas y pedal como spanners anclados a eventos. *Hecho cuando:* sobreviven a cortes de sistema y a ediciones de la música anclada.
- [ ] **F4.8 · Skylines completos.** Colocación de todos los elementos contra los perfiles y distribución vertical con ellos. *Hecho cuando:* ningún elemento se solapa en el catálogo de referencia.
- [ ] **F4.9 · Repeticiones.** Barras de repetición, casillas, D.C., D.S. y coda, en grabado y en reproducción. *Hecho cuando:* la reproducción sigue el orden correcto en cinco estructuras de prueba.
- [ ] **F4.10 · Letras y cifrado.** Varias estrofas, guiones y extensores; cifrado de acordes. *Hecho cuando:* imágenes de referencia de un himno con tres estrofas.
- [ ] **F4.11 · Partes vinculadas.** Extracción de partes como vistas con maquetación propia y compases de espera agrupados. *Hecho cuando:* editar una nota en la partitura la cambia en la parte.
- [ ] **F4.12 · Transposición.** Partitura en concierto o transpuesta e instrumentos transpositores. *Hecho cuando:* un clarinete en Si bemol muestra la altura escrita correcta en ambas vistas.
- [ ] **F4.13 · Vistas continua y de parte.** Vista de galera sin saltos de página y selector de vista. *Hecho cuando:* cambiar de vista conserva la selección.
- [ ] **F4.14 · MIDI en tiempo real.** Grabación con metrónomo y cuantización posterior. *Hecho cuando:* una melodía tocada a tempo queda cuantizada a corcheas sin errores.
- [ ] **F4.15 · Rendimiento orquestal.** Perfilado y optimización con la partitura de referencia completa en notación avanzada. *Hecho cuando:* se cumplen todos los presupuestos de la definición.

**Puerta F4:** la partitura orquestal de referencia se edita a 60 fps, con partes vinculadas y todos los presupuestos de rendimiento cumplidos.

## F5 · Pulido y lanzamiento 1.0 (semanas 47–54)

- [ ] **F5.1 · Estilos de casa.** Editor de estilo con vista previa, guardado y carga de estilos. *Hecho cuando:* aplicar un estilo guardado a otra partitura reproduce el aspecto.
- [ ] **F5.2 · Ajustes manuales.** Saltos de sistema y página manuales y arrastre fino de elementos guardado como diferencia. *Hecho cuando:* un ajuste manual sobrevive a cambiar el tamaño de página.
- [ ] **F5.3 · Accesibilidad.** Peers de automatización, descripción de la selección para lector de pantalla, alto contraste y tamaño de interfaz. *Hecho cuando:* NVDA y VoiceOver leen «negra, Do 5, compás 12, tiempo 3».
- [ ] **F5.4 · Localización.** Todos los textos en recursos, en español e inglés. *Hecho cuando:* una prueba detecta cualquier texto de interfaz sin recurso.
- [ ] **F5.5 · Exportación por lotes.** PDF de todas las partes en una operación. *Hecho cuando:* exporta las partes de la partitura de referencia con nombres de archivo correctos.
- [ ] **F5.6 · Instaladores.** Velopack para las tres plataformas, firma de código en Windows y notarización en macOS, actualizaciones automáticas. *Hecho cuando:* se instala y se actualiza en las tres plataformas desde CI.
- [ ] **F5.7 · Decisión: informes de fallos.** Proponer mecanismo opcional de informes de fallos con consentimiento explícito. *Hecho cuando:* existe `docs/decisiones/fallos.md`; el agente se detiene.
- [ ] **F5.8 · Beta pública.** Publicar la beta, clasificar las incidencias y corregir las bloqueantes. *Hecho cuando:* cero incidencias bloqueantes abiertas durante dos semanas.
- [ ] **F5.9 · Verificación final.** Medir cada objetivo de la 1.0 de la definición y documentar los resultados. *Hecho cuando:* informe en `docs/decisiones/objetivos-1.0.md` con todos los objetivos cumplidos.

**Puerta F5:** objetivos medibles de la 1.0 cumplidos y aprobados por ti. Lanzamiento.
