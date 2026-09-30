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
