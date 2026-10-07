# Compilar uxplay.exe para el portátil de Windows

`uxplay` (receptor AirPlay usado para el modo de grabación `"airplay"` y para el fallback de
screenshot vía AirPlay) no tiene binario oficial para Windows — [FDH2/UxPlay](https://github.com/FDH2/UxPlay)
solo documenta compilarlo a mano vía MSYS2. Como recompilar GStreamer en cada pipeline de CI
sería lentísimo y frágil, este build se hace **una sola vez a mano** y el resultado se sube a
una URL estable propia (ej. un release de GitHub) que `deploy/windows/armar-tatana-portable.sh`
descarga si está definida la variable de entorno `UXPLAY_WIN_ARTIFACT_URL`. Hoy no se usa: el
portátil y el instalador de Windows salen sin AirPlay (decisión D1 de ios-herramientas-windows).
(El `.gitlab-ci.yml` que lo descargaba se retiró en tatana-instalador-autoupdate.)

## Pasos (Windows 10/11, 64-bit)

1. Instalar [MSYS2](https://www.msys2.org/) y abrir la terminal **MSYS2 UCRT64**.
2. Instalar el compilador y dependencias:
   ```
   pacman -S mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-cmake mingw-w64-ucrt-x86_64-ninja \
             mingw-w64-ucrt-x86_64-gstreamer mingw-w64-ucrt-x86_64-gst-plugins-base \
             mingw-w64-ucrt-x86_64-gst-plugins-good mingw-w64-ucrt-x86_64-gst-plugins-bad \
             mingw-w64-ucrt-x86_64-gst-libav mingw-w64-ucrt-x86_64-openssl \
             mingw-w64-ucrt-x86_64-libplist git
   ```
3. Clonar y compilar:
   ```
   git clone https://github.com/FDH2/UxPlay.git
   cd UxPlay
   cmake -G Ninja -B build
   ninja -C build
   ```
4. El binario queda en `build/uxplay.exe`. Como está linkeado dinámicamente contra
   GStreamer/GLib/OpenSSL/libplist, hay que empaquetarlo junto con esas DLLs (no alcanza con
   copiar solo el .exe). La forma más simple es correr `ldd build/uxplay.exe` dentro de MSYS2
   para listar las dependencias y copiarlas todas desde `/ucrt64/bin/` a la misma carpeta que
   `uxplay.exe`, junto con el subdirectorio de plugins de GStreamer
   (`/ucrt64/lib/gstreamer-1.0/`).
5. Zippear `uxplay.exe` + DLLs + carpeta `gstreamer-1.0/` en un único `.zip` plano (sin
   subcarpeta contenedora — el CI lo extrae directo a `tools/uxplay/`).
6. Subir ese `.zip` a una URL estable (ej. un asset de un release de GitHub) y exportar esa URL
   como `UXPLAY_WIN_ARTIFACT_URL` antes de correr `armar-tatana-portable.sh` (en un workflow,
   como variable del repo pasada por `env:`).

## Cuándo repetir esto

Solo hace falta repetir el build si se quiere actualizar la versión de uxplay o de GStreamer.
No es parte del pipeline normal: `armar-tatana-portable.sh` solo descarga el artefacto ya
armado.
