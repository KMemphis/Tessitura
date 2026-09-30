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

