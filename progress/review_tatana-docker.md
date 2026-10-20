# Review — tatana-docker (HU #94, SDD instalacion-fiscalia §7)

**Veredicto:** APROBADA (con la salvedad de que nada con USB real fue probado; ver observaciones)

## Para el usuario en la fiscalía

Instalación (con el usuario normal de la PC, NO root, NO con sudo adelante; el script pide la contraseña cuando la necesita):

    tar xzf Tatana-Docker-v0.0.0-docker-prueba.tar.gz && cd Tatana-Docker-v0.0.0-docker-prueba
    ./diagnostico-distro.sh                                  # dice si conviene nativo o Docker
    ./instalar-tatana-docker.sh --origen https://factum.internal   # plan A
    ./instalar-tatana-docker.sh                                    # plan B (solo localhost:3000)
    ./estado-tatana-docker.sh                                      # si algo no anda, pegar esta salida

Workarounds por síntoma:

1. **iPhone no aparece (lo más probable en Ubuntu/Debian de escritorio).** El instalador solo detecta el usbmuxd del host
   si está *andando en ese momento*; en escritorio suele estar instalado pero dormido y arranca al enchufar el iPhone
   (regla udev) y le gana el dispositivo al del contenedor. Antes de instalar o si el iPhone no sale:

       sudo systemctl stop usbmuxd.service usbmuxd.socket 2>/dev/null; sudo systemctl mask usbmuxd.service
       docker restart tatana-docker        # (o sudo docker restart tatana-docker)
       # desenchufar y enchufar el iPhone, tocar "Confiar"
   Para revertir: `sudo systemctl unmask usbmuxd.service`. Si no hay systemd/usbmuxd instalado, no hace falta.
2. **Android no aparece / "falta el permiso".** Con el celular desenchufado y enchufado de nuevo tras instalar la regla
   (el instalador la pregunta; contestar `s`). Si sigue sin verlo, comprobar adentro del contenedor:

       docker exec tatana-docker /opt/tatana/tools/platform-tools/adb devices
       ls -l /dev/bus/usb/*/*          # el nodo del celular debería tener ACL (+) para tu usuario
   Plan B si el ACL `uaccess` no alcanza: poner `GROUP="plugdev"` (y `MODE="0660"`) en `/etc/udev/rules.d/70-factum-tatana.rules`,
   `sudo udevadm control --reload-rules && sudo udevadm trigger`, `sudo usermod -aG plugdev $USER`, cerrar sesión y
   volver a correr `./instalar-tatana-docker.sh` (el grupo se toma de `id -G` al instalar).
   Si el host tiene un adb abierto (Android Studio, `adb` suelto): `adb kill-server` en el host.
3. **"Docker no responde" / pide `sudo docker`.** `sudo systemctl enable --now docker`. Si el usuario no está en el grupo,
   el script usa `sudo docker` solo (aviso amarillo); para quedar fijo: `sudo usermod -aG docker $USER` y re-loguearse.
   Los comandos de arriba (`docker restart`, etc.) se escriben con `sudo` delante en ese caso.
4. **"El puerto 8765 ya está en uso".** Hay un Tatana nativo (AppImage) abierto: cerrarlo (`pkill -f Tatana`; `ss -ltnp | grep 8765`
   para ver quién es) y volver a correr el instalador. Nunca dejar los dos: el nativo y el contenedor no pueden convivir.
5. **La web no habla con Tatana (CORS / 403) en plan A.** Reinstalar con `--origen https://factum.internal` (sin barra final
   ni ruta). `./estado-tatana-docker.sh` muestra los orígenes. Probar: `curl -i -H 'Origin: https://factum.internal' http://localhost:8765/health`.
6. **Después de reiniciar la PC no está Tatana.** Necesita Docker habilitado al arranque: `sudo systemctl enable docker`;
   el contenedor tiene `--restart unless-stopped`. Verificar con `docker ps` (o `./estado-tatana-docker.sh`).
7. **El script dice que no corra como root.** Entrar con el usuario del perito (la evidencia queda a su nombre).
8. **Evidencia:** queda en `~/Factum/Evidencia` con dueño el perito (verificar con `ls -l` tras la primera adquisición).
   Desinstalar nunca la borra. Antes de una adquisición real, hacer una de prueba con un celular y abrir el ZIP.
9. **Plan C:** si algo de lo anterior no se resuelve en 10 min, `./desinstalar-tatana-docker.sh` y usar el AppImage
   o seguir con el Tatana de Windows; la web no cambia.

## Checkpoints
(Los de CHECKPOINTS.md que aplican a esta parte; revisión de scripts y artefacto, sin celular.)
- Build/sintaxis: [x] `bash -n` OK en los 7 scripts (armar, instalar, estado, desinstalar, diagnostico, comun, entrypoint). `shellcheck` no está en esta Mac: no verificado por mí (el informe dice limpio).
- `deploy/linux/armar-tatana-linux.sh` sin cambios: [x] `git diff -- deploy/linux` vacío.
- Alcance (solo `deploy/tatana-docker/`, `docs/tatana-docker.md`, progress): [x]
- Artefacto `deploy/fiscalia/dist/Tatana-Docker-v0.0.0-docker-prueba.tar.gz`: [x] SHA-256 coincide con el `.sha256`; contiene imagen, scripts, IMAGEN.txt, SHA256SUMS, VERSION, LEEME.
- Permisos/uid de evidencia: [x] el agente corre con `setpriv` al uid/gid/grupos del host (rechaza uid 0, `--no-new-privs`, sin caps); evidencia y datos se crean con el usuario antes de montarse y se montan en la misma ruta absoluta; `Agent:EvidenceDirectory` existe en `AgentModels.cs:30` y se pasa por `Agent__EvidenceDirectory`.
- Orígenes plan A: [x] `hornear-origenes-tatana.py` siempre antepone localhost:3000/127.0.0.1:3000 y valida estricto; rechaza ruta/barra antes de tocar el archivo.
- No borra datos ajenos: [x] instalar/desinstalar solo tocan el contenedor con etiqueta `factum.tatana-docker=1`; no borran evidencia ni datos.
- Seguridad: [x] sin `--privileged`, USB solo con cgroup rule `c 189:*`; reglas udev del host montadas `:ro`; no hay secretos ni logs sensibles.
- Mock: [x] `--mock` no usa USB ni usbmuxd.
- Acceso USB real (adb + iPhone) desde el contenedor: [ ] **no verificado por nadie** (Mac sin USB). El diseño es razonable, pero no hay evidencia empírica.

## Cambios requeridos
Ninguno bloqueante.

## Observaciones no bloqueantes
- `comun.sh:usbmuxd_del_host_activo` (líneas ~96-106) y `instalar-tatana-docker.sh` paso 3: solo detecta un usbmuxd *activo*. En Ubuntu de escritorio el del host está inactivo y arranca por udev al enchufar el iPhone, compitiendo con el del contenedor; el instalador dice "no hay usbmuxd en el host" aunque esté instalado. Conviene detectar `systemctl cat usbmuxd.service`/`command -v usbmuxd` y ofrecer el `mask` igual. Cumple la letra de la SDD ("activo"), pero es el fallo más probable en campo. Workaround 1 arriba.
- `instalar-tatana-docker.sh:~148`: el chequeo de puerto ocupado ocurre después de `docker rm -f` del contenedor anterior; está bien (el viejo no cuenta), pero `ss` ausente lo saltea y el error solo sale por el log del contenedor tras 2 min.
- Nada impide que un Tatana nativo con autoarranque pelee el 8765 en el siguiente inicio de sesión.
- `--platform linux/amd64` en `docker run` pide Docker >= 18.x; en Docker más viejo hay que quitarlo a mano. Improbable.
- adb server compartido por `--network host` (puerto 5037): un adb del host con otra versión se reinicia mutuamente.
- shellcheck no corrido por mí (no instalado); sintaxis sí.
