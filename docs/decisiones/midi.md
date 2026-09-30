# F0.12 · Dispositivos MIDI

Fecha: 29 de septiembre de 2026. Estado: **decisión pendiente**.

## Prueba reproducible

Se fijó Melanchall.DryWetMidi 8.0.3 (MIT). `tests/Tessitura.Benchmarks` enumera puertos con `InputDevice.GetAll()` y `OutputDevice.GetAll()`. En macOS también crea un `VirtualDevice`, escucha `EventReceived` y envía una nota Do4, velocidad 100, canal 2; la prueba exige recibir los tres valores correctos en menos de dos segundos. Una prueba unitaria comprueba la representación del mensaje MIDI.

```sh
dotnet build Tessitura.sln --nologo
dotnet test Tessitura.sln --no-build --nologo
dotnet run --no-build --project tests/Tessitura.Benchmarks -- midi-probe list
dotnet run --no-build --project tests/Tessitura.Benchmarks -- midi-probe loopback
```

`loopback` requiere macOS. No había teclado MIDI conectado al Mac de prueba; `list` devolvió cero entradas y cero salidas físicas. El puerto virtual recibió correctamente la nota. Esto demuestra la ruta de eventos CoreMIDI de DryWetMIDI en macOS, pero no valida conexión USB, desconexión ni un teclado real.

## Plataformas y alternativa Linux

| Sistema | Enumeración | Recepción de nota | Estado |
| --- | --- | --- | --- |
| macOS 27 arm64 | 0 entradas, 0 salidas físicas | Loopback virtual: Do4/canal 2/velocidad 100 recibido | Comprobado localmente |
| Windows | Por comprobar en CI; sin dispositivo físico | Sin prueba física | Pendiente |
| Ubuntu | La API de dispositivos de DryWetMIDI no está soportada | Sin prueba física | Requiere otro backend |

La [documentación de plataformas de DryWetMIDI](https://melanchall.github.io/drywetmidi/articles/dev/Supported-OS.html) limita su API Multimedia a Windows y macOS. Para Linux propongo un adaptador de la interfaz `IMidiPort` sobre el [secuenciador de ALSA](https://www.alsa-project.org/alsa-doc/alsa-lib/seq.html), que enumera clientes y puertos y recibe eventos MIDI. `alsa-lib` usa [LGPL 2.1](https://github.com/alsa-project/alsa-lib), cubierta por la autorización de licencias de F0.11. Esta sería una nueva dependencia de sistema y no se ha añadido ni implementado. Su empaquetado, recepción y estabilidad requieren validación física en Linux.

## Recomendación y decisión solicitada

Recomiendo DryWetMIDI para archivos MIDI y para puertos en Windows/macOS, con un adaptador ALSA detrás de `IMidiPort` en Linux. Esta recomendación queda condicionada a probar dispositivos físicos en Windows y Linux. El propietario indicó que solo dispone de este Mac; los runners de CI no sustituyen un teclado MIDI.

Se solicita aprobar el adaptador ALSA como dependencia de sistema y trasladar las pruebas físicas de Windows/Linux a F3.9. F0.12 permanece sin marcar hasta esa decisión.
