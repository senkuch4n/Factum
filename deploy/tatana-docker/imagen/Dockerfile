# Imagen de "Tatana en Docker" (SDD instalacion-fiscalia §7): el agente Factum.Agent (sin la UI de Electron) con sus
# herramientas portátiles (adb, scrcpy, ffmpeg, Python + pymobiledevice3) y usbmuxd para el iPhone, sobre Ubuntu 24.04.
# Sirve en cualquier Linux x86-64 con Docker, aunque su glibc sea más vieja que la 2.35 que pide el AppImage.
#
# No se construye a mano: la arma deploy/tatana-docker/armar-tatana-docker.sh, que deja en el contexto de build
#   - agente/   el contenido de Tatana-Agent-vX.Y.Z-linux-x64.tar.gz (deploy/linux/armar-tatana-linux.sh, el MISMO que
#               va dentro del AppImage: versiones fijas y SHA-256 verificados ahí, no se descarga nada dos veces)
#   - entrypoint.sh y hornear-origenes-tatana.py
#
# Base fijada por digest (índice multi-arquitectura de ubuntu:24.04). Para actualizarla: cambiar el digest y volver a
# correr la verificación de docs/tatana-docker.md.
FROM ubuntu:24.04@sha256:68ee22004a9109b44973313b02ff7b1de250f1de0f5ecb628d73f579aaff33ac

ARG TATANA_VERSION=0.0.0-dev

# usbmuxd: servicio de iPhone (corre adentro como root). libicu74/libssl3t64: .NET sin modo invariante.
# tini: PID 1 que reenvía señales y junta procesos huérfanos (adb, usbmuxd). curl: HEALTHCHECK. tzdata: zona horaria
# del host (se monta /etc/localtime) para la hora de los registros forenses.
RUN apt-get update \
 && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      usbmuxd libicu74 libssl3t64 ca-certificates tzdata tini curl \
 && rm -rf /var/lib/apt/lists/*

COPY agente/ /opt/tatana/
COPY entrypoint.sh hornear-origenes-tatana.py /opt/tatana-docker/
RUN chmod 755 /opt/tatana-docker/entrypoint.sh \
 && test -x /opt/tatana/Factum.Agent \
 && test -x /opt/tatana/tools/platform-tools/adb \
 && test "$(cat /opt/tatana/version.txt)" = "$TATANA_VERSION"

LABEL org.opencontainers.image.title="Tatana en Docker (Factum.Agent)" \
      org.opencontainers.image.version="$TATANA_VERSION"

# El agente lee appsettings.json del directorio de trabajo (ContentRoot), como en el AppImage.
WORKDIR /opt/tatana
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    TATANA_PUERTO=8765

# Con --network host, 127.0.0.1 es el del host: es el mismo /health que ve la web.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
  CMD curl -fsS "http://127.0.0.1:${TATANA_PUERTO}/health" >/dev/null || exit 1

ENTRYPOINT ["/usr/bin/tini", "-g", "--", "/opt/tatana-docker/entrypoint.sh"]
CMD []
