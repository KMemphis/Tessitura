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

