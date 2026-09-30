# F0.11 · Comparación de salida de audio

Fecha: 29 de septiembre de 2026. Estado: **decisión pendiente**.

## Requisito y cambio de licencia

La definición fija un objetivo de búfer inferior a 20 ms y propone OpenAL Soft con Silk.NET o miniaudio detrás de `IAudioOutput`. [OpenAL Soft](https://github.com/kcat/openal-soft/blob/master/README.md) usa LGPL 2 o posterior; la regla original de Tessitura lo excluía. El propietario autorizó expresamente LGPL también para el producto el 29 de septiembre de 2026. Se actualizó `AGENTS.md` y la definición; las obligaciones de distribución se deberán comprobar antes de lanzar.

## Prueba reproducible

`tests/Tessitura.Benchmarks` genera la misma nota MIDI Do4 con MeltySynth 2.4.1 y el SoundFont de prueba MIT indicado en `tests/assets/README.md`. La señal es estéreo a 48 kHz. La prueba unitaria verifica que ambas vías del sintetizador contienen señal.

Comandos desde la raíz del repositorio:

```sh
dotnet build Tessitura.sln --nologo
dotnet test Tessitura.sln --no-build --nologo
dotnet run --no-build --project tests/Tessitura.Benchmarks -- audio-probe openal
dotnet run --no-build --project tests/Tessitura.Benchmarks -- audio-probe miniaudio
```

El primer candidato usa Silk.NET.OpenAL 2.23.0 y el paquete nativo OpenAL Soft 1.23.1; se comprobó en macOS que el proceso cargó `libopenal.dylib` del paquete, no el framework del sistema. El segundo usa el enlace [MiniAudioExNET 3.3.7](https://github.com/japajoe/MiniAudioExNET), con licencia MIT y binarios para los tres sistemas. [miniaudio](https://github.com/mackron/miniaudio/blob/master/LICENSE) permite elegir MIT No Attribution.

## Resultados disponibles

Equipo: MacBook Air arm64, macOS 27.0, altavoces integrados predeterminados; el dispositivo anuncia 96 kHz y la prueba solicita 48 kHz, por lo que puede haber remuestreo. Cinco ejecuciones por candidato:

| Candidato | Arranque API, mediana (rango) | Primera devolución de audio, mediana (rango) | Resultado |
| --- | ---: | ---: | --- |
| OpenAL Soft | 0,048 ms (0,036–0,061) | No disponible en esta prueba | Cinco llamadas sin error OpenAL |
| miniaudio | 0,056 ms (0,054–0,080) | 8,204 ms (6,258–9,236) | Cinco callbacks recibidos |

El tiempo de arranque API mide el retorno de `SourcePlay` o `Play`, **no la latencia hasta el altavoz**. El tiempo de primer callback de miniaudio tampoco incluye la cola del dispositivo ni el recorrido acústico. Estos valores no son directamente comparables y no demuestran aún el objetivo de 20 ms. La implementación OpenAL de la prueba carga una nota en un búfer estático; el flujo definitivo deberá usar una cola continua. La prueba miniaudio emplea su API estándar estática solo en este ejecutable experimental; la implementación del producto deberá encapsular la API avanzada en una instancia inyectable y evitar asignaciones en el callback.

Windows y Linux se comprueban mediante compilación y prueba del sintetizador en la matriz de CI; los runners no aportan una salida física fiable. No se han medido allí ni reproducción audible ni latencia. El propietario dispone solo de este Mac.

## Recomendación y decisión solicitada

**Recomiendo miniaudio** para la futura implementación de `IAudioOutput`: su callback entrega directamente los bloques PCM y un punto claro para contar muestras, y su licencia permisiva simplifica la distribución. Esta recomendación es provisional hasta medir latencia de extremo a extremo, cortes y estabilidad en hardware Windows y Linux. La licencia LGPL ya autoriza OpenAL Soft como alternativa si esas pruebas favorecen su rendimiento.

Se solicita decidir si se acepta miniaudio como candidato elegido y se traslada la validación física de Windows y Linux a F3.6, dejando explícita esa deuda en la puerta F0. Sin esa aprobación, F0.11 permanece sin marcar en `docs/plan.md`.
