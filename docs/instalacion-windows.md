# Factum en una PC con Windows — guía de instalación y uso

Esta guía tiene dos partes:

- **Parte A — Uso diario.** Para quien usa Factum todos los días: cómo abrirlo, cómo hacer
  un backup y qué hacer si no abre. No hace falta saber de computación.
- **Parte B — Instalación y mantenimiento.** Para quien instala Factum en la PC (una sola
  vez) o lo actualiza. Paso a paso.

---

## 1. Qué es cada pieza

Factum funciona entero en **una sola PC** del estudio, sin internet:

- **Factum** (la página web, el servidor y la base de datos) corre dentro de **Docker
  Desktop**, un programa que mantiene a Factum funcionando en segundo plano.
- **Tatana** es el programa que habla con los celulares conectados por **USB**. Corre
  aparte, directamente en Windows, y arranca solo al iniciar sesión.
- Todo se usa desde el navegador en **http://localhost:3000**. Nadie de afuera de esta PC
  puede entrar: ni desde otra computadora de la red ni desde internet.

---

# Parte A — Uso diario

## A1. Abrir Factum

1. Prendé la PC e iniciá sesión en Windows con **tu** usuario.
2. Doble clic en el acceso **"Factum"** del Escritorio.
3. Si la PC recién arranca, puede tardar **uno o dos minutos**: no aparece ninguna ventana
   mientras espera (Docker Desktop está arrancando). Cuando todo está listo se abre el
   navegador con la pantalla de ingreso de Factum.
4. Ingresá **siempre con tu propio DNI** (ver el aviso de seguridad de la sección 6).

Si aparece el cartel **"Factum todavía no arrancó"**: esperá un minuto y volvé a hacer doble
clic. Si vuelve a aparecer, seguí la sección A3.

> **Tatana no abre el navegador solo.** Al iniciar sesión Tatana arranca en silencio (una
> ventanita minimizada "Tatana Agent" en la barra de tareas: **no la cierres**). Factum se
> abre siempre con el acceso "Factum" del Escritorio.

## A2. Hacer un backup

Un backup es una copia completa de Factum: los casos, la evidencia (ZIP e informes), las
contraseñas de los ZIP y la configuración del estudio.

1. Doble clic en **"Backup de Factum"** del Escritorio.
2. El backup **pausa Factum unos minutos** (más si hay mucha evidencia) para que la copia
   quede coherente. Avisale a quien lo esté usando. Presioná **Enter** para empezar.
3. Al terminar dice **"Backup terminado"**, la carpeta donde quedó y cuántos casos tiene.
   Factum vuelve a funcionar solo.

Por defecto los backups quedan en `C:\Factum\backups\` (se guardan los 10 más nuevos).
**Copiá regularmente esa carpeta a un disco externo**, o hacé el backup directamente en el
disco (sección 10).

> **El backup tiene las contraseñas de los ZIP de evidencia.** Guardalo bajo llave: quien
> tenga el backup puede abrir toda la evidencia.

## A3. Si Factum no abre

1. Fijate que **Docker Desktop** esté abierto: tiene que estar el ícono de la **ballena** al
   lado del reloj. Si no está, abrí "Docker Desktop" desde el menú Inicio y esperá a que diga
   *Engine running*. Después volvé a abrir "Factum".
2. Si sigue sin abrir, abrí la carpeta `C:\Factum` y hacé doble clic en
   **"Diagnostico de Factum"**. Muestra el estado de todo y lo guarda en un archivo
   (`C:\Factum\logs\diagnostico-<fecha>.txt`). Mandale ese archivo al proveedor.
3. Si el celular no aparece en el asistente: ver la sección 12, "El celular no aparece".

---

# Parte B — Instalación y mantenimiento

## 2. Requisitos de la PC

| Requisito | Mínimo | Cómo verlo |
|---|---|---|
| Windows | **Windows 10 versión 22H2** o Windows 11, de **64 bits** | Tecla Windows + R, escribir `winver`, Enter. Dice la versión (22H2) y la compilación (19045 o mayor). |
| Memoria RAM | **8 GB** recomendado. Con **4 GB** funciona, más lento, con el perfil de poca memoria (sección 2.1). Si Windows reporta menos de 3,5 GB, no se puede. | Configuración > Sistema > Acerca de > "RAM instalada". |
| Disco libre | **50 GB** recomendado (mínimo 15 GB) en la unidad de `C:\Factum` | Explorador de archivos > Este equipo. |
| Virtualización | Habilitada en la BIOS/UEFI | Administrador de tareas > Rendimiento > CPU > "Virtualización: Habilitado". |
| Usuario | Una cuenta con permisos de **administrador**, solo para instalar Docker Desktop y WSL | — |

El instalador de Factum (`1-Instalar Factum.bat`) **revisa todo esto antes de tocar nada**.
Si falta algo, lista **todos** los problemas juntos, en castellano, y no instala nada. Qué
hacer con cada mensaje:

| # | Mensaje del instalador | Qué hacer |
|---|---|---|
| Q1 | "Factum necesita Windows de 64 bits." | La PC no sirve: hace falta un Windows de 64 bits (ver sección 13). |
| Q2 | "Tu Windows es … (build N). Hace falta Windows 10 versión 22H2 o posterior" | Configuración > Windows Update > buscar actualizaciones, hasta llegar a 22H2. Reiniciar y volver a probar. |
| Q3 | "Factum necesita al menos 4 GB instalados" | Windows reporta menos de 3,5 GB: Factum no se puede instalar en esa PC. |
| Q3 | Bloque "POCA MEMORIA: la PC tiene N GB de RAM…" + "¿Instalar igual con poca memoria? (S/N)" | PC de 4 GB (entre 3,5 y 7,5 GB reportados). Con **S** la instalación sigue y queda el perfil de poca memoria (sección 2.1); con **N** se cancela sin tocar nada. Es una pregunta aparte de la de los demás avisos. |
| Q3 | Aviso "recomendable 16 GB" | Entre 8 y 16 GB anda, pero cerrá programas pesados (por ejemplo, muchas pestañas del navegador) mientras uses Factum. |
| Q4 | "La virtualización está apagada en la BIOS/UEFI…" | Ver sección 3. |
| Q5 | "Quedan N GB libres…" | Liberá espacio (o instalá en otra unidad con `-Carpeta D:\Factum`, ver sección 5). |
| Q6 | "Docker Desktop no está instalado" / "WSL2 no está instalado" | Ver secciones 3 y 4. |
| Q7 | "Docker Desktop no responde" | El instalador intenta abrirlo solo y espera 2 minutos. Si no alcanza: abrilo desde el menú Inicio, esperá *Engine running* y volvé a ejecutar el instalador. |
| Q7 | "Tu usuario de Windows no está en el grupo docker-users" | Ver sección 4, paso 5. |
| Q8 | "Docker está en modo contenedores Windows" | Clic derecho en la ballena (junto al reloj) > **Switch to Linux containers**. |
| Q9 | "Docker Compose es muy viejo" | Actualizá Docker Desktop (desde el propio Docker Desktop: *Check for updates*). |
| Q10 | "El puerto 3000 / 8080 lo está usando el programa X" | Factum necesita esos puertos. Cerrá o desinstalá ese programa. Un aviso por el **8765** quiere decir que otro programa ocupa el puerto de Tatana. |
| Q11 | Aviso "Docker Desktop no está configurado para arrancar al iniciar sesión" | En Docker Desktop: Settings > General > tildar **Start Docker Desktop when you sign in**. Sin esto, Factum no arranca solo al prender la PC. |

### 2.1 PC con poca memoria (4 GB)

Si la PC tiene 4 GB de RAM (Windows reporta entre 3,5 y 7,5 GB), el instalador muestra el
bloque **POCA MEMORIA** y pregunta si instalar igual. Con **S**, `C:\Factum\config\.env` queda
con `FACTUM_PERFIL_MEMORIA=poca` y Factum se configura para usar la menor memoria posible. En
las PCs de 8 GB o más el perfil es `normal` y nada cambia.

**Qué hace el perfil.** Le pone un tope de memoria a cada parte de Factum (archivo
`C:\Factum\docker-compose.poca-ram.yml`). Son **máximos, no reservas**: en reposo las tres
partes usan bastante menos (≈ 35-90 MB cada una).

| Parte | Tope | Además |
|---|---|---|
| Base de datos (Mongo) | 512 MB | caché de datos de 0,25 GB |
| Backend (API e informes) | 768 MB | recolector de memoria de .NET en modo "estación de trabajo" |
| Web (frontend) | 384 MB | memoria de Node limitada a 256 MB |

**Qué esperar.** Abrir pantallas y generar informes es más lento, y Windows usa el disco como
memoria cuando no le alcanza. La PC del estudio tiene **SSD**, así que es tolerable; con un
disco mecánico (HDD) sería mucho más lento, y cambiarlo por un SSD es la mejora más barata.

**Consejos para que ande mejor:**

- Cerrá otros programas y pestañas del navegador mientras uses Factum.
- No dejes abierta la ventana de Docker Desktop: alcanza con la ballena junto al reloj.
- Reiniciá la PC al empezar el día.
- No tengas otras distribuciones de WSL abiertas (si no sabés qué es, no tenés ninguna).

**Si un informe falla o Factum se reinicia al generar:**

- Volvé a generarlo: la evidencia **no se pierde** (los archivos del caso se borran recién
  cuando el ZIP quedó verificado) y el caso se puede reintentar.
- Abrí "Diagnostico de Factum" y mirá la sección **Memoria**: "sin memoria: SÍ" en una parte
  quiere decir que se quedó sin memoria.
- Si se repite, avisale al proveedor y mandale el archivo del diagnóstico.
- Los casos con muchas capturas son los más exigentes. Medido con el perfil de poca memoria:
  un caso de **40 capturas** (≈ 77 MB de imágenes) más 750 MB de video generó el informe en
  ≈ 70 segundos y el backend usó como máximo ≈ 260 MB de sus 768 MB. Los casos de más de ~40
  capturas son los que más se acercan al tope.

**Cambiar de perfil** (por ejemplo, si le agregan RAM a la PC):

1. Abrí `C:\Factum\config\.env` con el Bloc de notas.
2. Cambiá `FACTUM_PERFIL_MEMORIA=poca` por `FACTUM_PERFIL_MEMORIA=normal` (o al revés) y
   guardá.
3. Abrí **"Diagnostico de Factum"** y respondé **S** a "¿Reiniciar Factum para aplicar
   cambios de configuración…?". Se recrean los contenedores con el perfil nuevo; los casos, la
   evidencia y la base **no se tocan**.

Para **revertir** lo que hace este perfil alcanza con lo mismo: `FACTUM_PERFIL_MEMORIA=normal`
y reiniciar desde el diagnóstico.

**`.wslconfig` (opcional, a mano).** Los scripts de Factum **nunca** lo escriben. Sin ese
archivo, WSL2 (donde corre Docker) ya usa como máximo la mitad de la RAM y ≈ 25 % de swap.
Solo si el diagnóstico muestra que la memoria no alcanza, se puede crear
`%UserProfile%\.wslconfig` con:

```ini
[wsl2]
memory=2GB
swap=2GB
```

y después, en una terminal, `wsl --shutdown`. Eso **apaga Docker Desktop**: hay que volver a
abrirlo. El archivo afecta a todas las distribuciones de WSL de esa cuenta. Para revertirlo:
borrar el archivo y repetir `wsl --shutdown`.

**Checklist después de instalar en la PC de 4 GB** (prueba final en la PC real):

1. En la Mac, `deploy/windows/armar-paquete.sh --version <X>`: `SHA256SUMS.txt` lista
   `docker-compose.poca-ram.yml`.
2. `1-Instalar Factum.bat`: aparece el bloque **POCA MEMORIA** con la RAM real (≈ 3,7-3,9 GB)
   y su pregunta S/N, separada de "¿Continuar igual…?" si hubo otros avisos. Responder **N**:
   dice "Instalación cancelada" y no existe `C:\Factum`. Volver a ejecutarlo y responder **S**.
3. Al terminar: `config\.env` tiene `FACTUM_PERFIL_MEMORIA=poca`, el resumen dice "Perfil de
   memoria: poca RAM" y `http://localhost:3000` muestra el login.
4. "Diagnostico de Factum": la sección **Memoria** muestra la RAM, el perfil y el uso
   "x / 512MiB", "x / 768MiB", "x / 384MiB"; reinicios en 0.
5. Caso real típico (15-20 fotos + video): generar el informe. Anda (lento), el ZIP verifica y
   el diagnóstico no muestra reinicios.
6. `scripts\instalar.ps1 -Reparar` desde el paquete: vuelve a preguntar por la poca memoria y
   el perfil sigue en `poca`.
7. (Si hay una PC de 8 GB o más a mano.) La instalación queda en `normal` y el diagnóstico
   muestra el uso sin topes propios.
8. Cambio de perfil: `poca` → `normal` en `config\.env` + diagnóstico → **S**. Los contenedores
   se recrean sin topes y los casos siguen ahí. Volver a `poca` de la misma forma.
9. Actualizar con un paquete nuevo: no pregunta por la RAM y el perfil se conserva.

## 3. Habilitar la virtualización y WSL2

Docker Desktop necesita la **virtualización** del procesador y **WSL2** (el "subsistema de
Windows para Linux").

**Virtualización en la BIOS/UEFI** (solo si el instalador dice que está apagada):

1. Reiniciá la PC y, apenas prende, apretá repetidamente la tecla del setup. Suele ser
   **F2** o **Supr (Del)**; en HP **F10** (o Esc y después F10), en Lenovo **F1** o **F2**,
   en Dell **F2**.
2. Buscá una opción llamada **Intel Virtualization Technology (VT-x)**, **Intel VT**,
   **SVM Mode** o **AMD-V** (suele estar en *Advanced*, *CPU Configuration* o *Security*).
3. Ponela en **Enabled**, guardá (normalmente **F10**) y reiniciá.

**WSL2:**

1. Clic derecho en el menú Inicio > **Terminal (Administrador)** o **Windows PowerShell
   (Administrador)**.
2. Escribí `wsl --install` y Enter. Al terminar, **reiniciá la PC**.
3. Si WSL ya estaba instalado, actualizalo con `wsl --update`.

## 4. Instalar Docker Desktop

1. Desde una PC con internet, descargá **Docker Desktop for Windows (AMD64)** de
   https://www.docker.com/products/docker-desktop/ y copiá el instalador a la PC.
2. Ejecutalo (pide permisos de administrador). En la primera pantalla dejá tildado **Use WSL
   2 instead of Hyper-V**.
3. Al terminar, reiniciá la PC (o cerrá sesión y volvé a entrar) y abrí **Docker Desktop**.
   Aceptá los términos. No hace falta crear una cuenta ni iniciar sesión (*Continue without
   signing in* / *Skip*).
4. En Docker Desktop: **Settings > General** > tildar **Start Docker Desktop when you sign
   in** > *Apply*.
5. Si Factum lo va a usar **una cuenta de Windows distinta** de la que instaló Docker, esa
   cuenta tiene que estar en el grupo **docker-users**: un administrador abre *Administración
   de equipos* > *Usuarios y grupos locales* > *Grupos* > **docker-users** > *Agregar…*, y
   después esa persona cierra sesión y vuelve a entrar.

> **Licencia de Docker Desktop.** Es **gratuita** para organizaciones de **menos de 250
> empleados y menos de USD 10 millones de facturación anual** (tienen que cumplirse las
> dos). El estudio cumple. Si algún día crece por encima de eso, Docker Desktop requiere una
> suscripción paga.

## 5. Instalar Factum

1. Copiá el paquete `Factum-Instalacion-vX.Y.Z.zip` a la PC (por ejemplo al Escritorio) y
   **descomprimilo** (clic derecho > *Extraer todo…*). No hace falta internet.
2. Abrí la carpeta descomprimida y hacé doble clic en **`1-Instalar Factum.bat`**.
3. Si Windows muestra **"Windows protegió su PC"** (SmartScreen): *Más información* >
   *Ejecutar de todas formas*.
4. El instalador muestra los pasos numerados (`[1/13] …`). Qué hace cada uno:
   1. Verifica que el paquete esté completo (si la copia por USB quedó a medias, lo dice).
   2. Revisa los requisitos (sección 2). Si hay avisos, pregunta si continuar (**S**/**N**).
      En una PC de 4 GB muestra además el aviso de **POCA MEMORIA** con su propia pregunta
      **S**/**N** (sección 2.1).
   3. Revisa que Factum no esté ya instalado.
   4. Crea `C:\Factum` con sus carpetas: `config`, `evidencia`, `backups`, `logs`, `scripts`.
   5. Restringe el acceso a `C:\Factum`: solo **tu usuario**, los administradores y el
      sistema pueden abrirla (ahí están la evidencia y las contraseñas de los ZIP). Otra
      cuenta de Windows de la misma PC **no** puede leer esa carpeta; Factum en el navegador
      sigue andando para todas.
   6. Crea la configuración (`C:\Factum\config\.env`) con una **clave secreta propia de esta
      PC** y el **perfil de memoria** (`FACTUM_PERFIL_MEMORIA`: `poca` o `normal`, sección 2.1),
      y `config\appsettings.Local.json` para la identidad del estudio.
   7. Copia los archivos de Factum.
   8. Carga las imágenes de Factum en Docker (**tarda unos minutos**).
   9. Arranca Factum y espera a que esté listo.
   10. Crea en el Escritorio los accesos **"Factum"** y **"Backup de Factum"**.
   11. Pregunta si programar un **backup automático todos los días a las 20:00** (**S**/**N**).
   12. Instala **Tatana** (copia a `%LOCALAPPDATA%\Programs\Tatana`, lo deja arrancando al
       iniciar sesión, y verifica que **no** esté en modo simulado).
   13. Muestra el resumen: la dirección `http://localhost:3000`, dónde queda la evidencia, el
       perfil de memoria ("poca RAM" o "normal"), el aviso de seguridad y los próximos pasos.
5. Todo queda registrado en `C:\Factum\logs\instalar-<fecha>.log`.

Opciones (desde una terminal en la carpeta del paquete):
`powershell -ExecutionPolicy Bypass -File scripts\instalar.ps1 -Carpeta D:\Factum` instala en
otra carpeta; `-Reparar` reinstala scripts e imágenes de la **misma** versión sin tocar la
configuración ni los datos; `-SinTatana` no instala Tatana; `-PerfilMemoria poca|normal`
fuerza el perfil de memoria en vez de elegirlo por la RAM (no saltea el mínimo de RAM y no se
combina con `-Reparar`, que conserva el perfil de `config\.env`).

## 6. Aviso de seguridad: cómo se entra a Factum

> **IMPORTANTE.** En esta versión Factum **no tiene usuarios con contraseña propia**: acepta
> **cualquier DNI y cualquier contraseña**. Solo se puede entrar desde esta PC, así que la
> protección real es **la contraseña de Windows**.
>
> - Cada perito tiene que **bloquear la sesión (Windows + L)** al levantarse de la PC.
> - Cada perito tiene que entrar **siempre con su propio DNI**: la autoría de los casos y la
>   auditoría dependen de eso.
>
> Una versión futura va a agregar usuarios con contraseña.

## 7. Identidad del estudio (nombre, logo, contacto, colores)

Los informes salen con el membrete del estudio. Se configura en
`C:\Factum\config\appsettings.Local.json` (abrilo con el Bloc de notas). Ejemplo completo con
**valores ficticios** (reemplazalos por los reales):

```json
{
  "Branding": {
    "OrganizationName": "Estudio Jurídico Ejemplo",
    "OrganizationLogo": "branding/logo.png",
    "OrganizationIsotype": "branding/isotipo.png",
    "ContactLines": [
      "Calle Ejemplo 123, Ciudad",
      "Tel. +54 9 000 000-0000 · contacto@ejemplo.com"
    ],
    "PrimaryColor": "#203040",
    "AccentColor": "#E8EEF4"
  },
  "Report": {
    "TimeZone": "America/Argentina/Buenos_Aires",
    "DomicilioConstituido": "Calle Ejemplo 123, Ciudad",
    "EncryptZip": true,
    "DefaultTexts": {
      "OperacionesRealizadas": "",
      "AseguramientoEvidencia": "",
      "NotasTecnicas": "",
      "Reserva": ""
    }
  },
  "Audit": {
    "AdminDnis": []
  }
}
```

1. **Logo:** copiá el archivo a `C:\Factum\config\branding\` con el nombre `logo.png` (y el
   isotipo, si hay, como `isotipo.png`). Tiene que ser **PNG o JPEG** (no SVG), de hasta
   **1 MB** y entre **16 y 4096 píxeles** por lado; para fondo transparente, PNG. Si el
   archivo no sirve, Factum sigue andando sin logo y lo anota en su registro.
2. **Colores:** formato `#RRGGBB`. El primario tiene que contrastar bien con blanco y el de
   acento con texto oscuro; si no, Factum usa sus colores por defecto (lo anota en el
   registro). Vacíos = colores de Factum.
3. Cuidá la sintaxis: comillas dobles, comas entre elementos y ninguna coma después del
   último. Un error de sintaxis impide que Factum arranque: al aplicar los cambios (punto 4), el
   "Diagnostico de Factum" lo muestra.
4. **Aplicar los cambios:** abrí `C:\Factum` > **"Diagnostico de Factum"** y respondé **S** a
   *"¿Reiniciar Factum para aplicar cambios de la identidad del estudio?"*. Si el backend no
   vuelve a arrancar, el diagnóstico muestra el registro con el valor que falla.
5. Generá un informe de prueba y revisá el membrete.

Estos datos quedan solo en esta PC (y en sus backups): no viajan dentro de ningún paquete.

## 8. Tatana y los drivers de los celulares

El instalador ya deja Tatana instalado y arrancando al iniciar sesión. Para que reconozca los
celulares:

**Android**

1. Instalá el **driver USB del fabricante** (Samsung, Motorola, Xiaomi…) o el **Google USB
   Driver**. Sin driver, Windows ve el celular pero Tatana no.
2. En el celular: activá las **Opciones de desarrollador** (Ajustes > Acerca del teléfono >
   tocar 7 veces "Número de compilación") y adentro **Depuración USB**.
3. Al conectarlo, en el celular aparece *"¿Permitir la depuración por USB?"*: tildá
   *Permitir siempre desde esta computadora* y aceptá.

**iPhone**

1. Instalá **Apple Mobile Device Support**: viene con iTunes o con la app **"Dispositivos de
   Apple"** (Microsoft Store). Instalarla una vez alcanza.
2. Al conectarlo, desbloqueá el iPhone y tocá **"Confiar en esta computadora"** (pide el
   código del iPhone).
3. En **iOS 17 o posterior** algunas funciones necesitan que Tatana corra con privilegios de
   administrador; el asistente de Factum lo indica cuando hace falta.

**Firewall:** Tatana ya no escucha en la red (solo en esta PC), así que Windows no muestra el
aviso del firewall.

**Comprobar Tatana:** "Diagnostico de Factum" muestra el estado de Tatana. Tiene que decir
**modo real**. Si dice **modo simulado (mock = true)**: **no usar para peritajes** y avisar
al proveedor.

Debajo, en **Herramientas de Tatana**, el diagnóstico lista `adb`, `scrcpy` (grabación de
pantalla de Android), `ffmpeg` y `python`, cada una con **OK** (versión y ruta) o **FALTA**.
Todas tienen que estar en OK, con su ruta dentro de la carpeta de Tatana (`tools\`). Si alguna
dice **FALTA** (por ejemplo `scrcpy: FALTA`), se arregla con **"Actualizar Factum"** usando el
paquete del proveedor. Si dice *"Esta versión de Tatana no informa sus herramientas"*, Tatana
es anterior a la 1.1.0: también se arregla con "Actualizar Factum".

**Micrófono de la PC** (grabar Android "con mic de PC"): Tatana usa el primer micrófono que
lista Windows. Si toma uno que no es, se puede fijar con `"Agent": { "MicDevice": "<nombre>" }`
en un `appsettings.Local.json` junto a `Factum.Agent.exe` (en
`%LOCALAPPDATA%\Programs\Tatana`); el nombre exacto sale de
`tools\ffmpeg\ffmpeg.exe -hide_banner -list_devices true -f dshow -i dummy` (usar el nombre que
aparece entre comillas antes de `(audio)`, por ejemplo `Micrófono (Realtek(R) Audio)`, y
reiniciar Tatana).

## 9. Prueba de humo (después de instalar)

1. Abrí Factum con el acceso del Escritorio e ingresá con tu DNI.
2. Creá un **caso de prueba**.
3. Conectá un Android (y, si hay, un iPhone): tiene que aparecer en el asistente. Hacé una
   **captura de pantalla**.
4. **Generá el informe** del caso. Anotá la contraseña del ZIP que muestra Factum.
5. Abrí el **DOCX** del informe en Word y revisá el membrete del estudio.
6. Verificá el SHA-256 del ZIP: en PowerShell,
   `Get-FileHash "C:\Factum\evidencia\cases\<carpeta del caso>\<archivo>.zip" -Algorithm SHA256`.
   El valor tiene que ser **igual** al que figura en la tabla de hashes del informe.
7. Borrá el caso de prueba si no lo querés conservar.

## 10. Backups y restauración

**Qué guarda:** la base de datos, toda la evidencia (`C:\Factum\evidencia`), la configuración
(incluida la clave secreta y la identidad del estudio), `backup-info.json` (versión, fecha,
equipo, cantidad de casos) y `manifiesto-sha256.txt` (el SHA-256 de **cada** archivo). Al
terminar compara la copia de cada archivo de evidencia con el original; si algo no coincide,
el backup queda marcado **`_FALLIDO`** y avisa.

**Pausa:** mientras copia, Factum queda pausado (la web no responde). Es para que la base de
datos y la evidencia queden coherentes entre sí. Vuelve solo, aunque el backup falle.

**A un disco externo:** conectá el disco (por ejemplo `E:`) y, desde `C:\Factum`, en una
terminal: `powershell -ExecutionPolicy Bypass -File scripts\backup.ps1 -Destino E:\`.
Para que sea siempre ahí, poné `FACTUM_BACKUP_DESTINO=E:/Backups-Factum` en
`C:\Factum\config\.env` (la carpeta tiene que existir).

**Backup automático:** si lo programaste al instalar, corre todos los días a las **20:00**
(tarea "Factum - Backup diario" del *Programador de tareas*; si la PC estaba apagada, corre al
prenderla). Su registro queda en `C:\Factum\logs\backup-<fecha>.log`.
Necesita que Docker Desktop esté abierto a esa hora: si no lo está, ese día no hay backup y
el registro dice *"No se pudo pausar Factum"*. Si un backup se corta a mitad de camino, su
carpeta queda marcada `_FALLIDO` para no confundirla con uno bueno.

**Retención:** en `C:\Factum\backups` se conservan los **10** backups más nuevos
(`FACTUM_BACKUP_CONSERVAR` en `config\.env`); los más viejos se borran solos. Nunca se borran
los marcados `_FALLIDO`, ni los de un disco externo, ni carpetas ajenas.

> El backup contiene las **contraseñas de los ZIP**. Guardalo bajo llave.

**Restaurar (en la misma PC o en otra):**

1. La PC tiene que tener Factum instalado en la **misma versión** del backup (si es una PC
   nueva: instalá con el paquete de esa versión; después podés actualizar).
2. Abrí `C:\Factum` > **"Restaurar Factum"** y elegí la carpeta del backup (la que tiene
   `manifiesto-sha256.txt`).
3. Primero verifica el SHA-256 de **cada archivo** del backup. Si alguno no coincide o falta,
   lo lista y **no toca nada**.
4. Si la instalación ya tiene casos o evidencia, avisa y pide escribir **SI** (en mayúsculas).
   Antes de pisar nada hace un backup del estado actual y dice dónde quedó.
5. Restaura la base, la evidencia (verificando cada archivo) y la identidad del estudio.
   Conserva la carpeta y la versión de esta PC.

## 11. Actualizar Factum

1. Copiá y descomprimí el paquete **nuevo** (`Factum-Instalacion-vNUEVA.zip`).
2. Doble clic en **`Actualizar Factum.bat`** de ese paquete.
3. Hace, en orden: verifica el paquete, hace un **backup automático** (si falla, no actualiza
   nada), guarda la configuración actual, carga las imágenes nuevas y arranca la versión nueva.
4. **Si la versión nueva no arranca en 3 minutos, vuelve sola a la anterior** y lo informa.
   Los datos que la versión nueva haya llegado a escribir no se deshacen: si notás algo raro,
   restaurá el backup que indica.
5. Si el paquete trae un Tatana nuevo, pregunta si actualizarlo.
6. Las imágenes de la versión anterior **no se borran** (sirven para volver atrás). Cuando ya
   no las necesites, liberá espacio con
   `docker image rm factum-backend:VIEJA factum-frontend:VIEJA`.

**Prueba de la vuelta atrás (solo para quien instala):** desde la carpeta del paquete nuevo,
`powershell -ExecutionPolicy Bypass -File scripts\actualizar.ps1 -SimularFalla` hace todo el
proceso pero trata la versión nueva como fallida: tiene que volver a la anterior, decirlo, y
los datos tienen que seguir ahí. Después corré `Actualizar Factum.bat` normalmente.

## 12. Problemas comunes

| Problema | Qué hacer |
|---|---|
| **Docker Desktop no arranca** o dice que WSL está desactualizado | Terminal como administrador: `wsl --update`, reiniciar. Revisar la virtualización (sección 3). |
| **"El puerto 3000/8080 lo está usando…"** | Cerrá ese programa (o desinstalalo). Los puertos de Factum son fijos. |
| **"Factum todavía no arrancó"** | Esperá un minuto. Si sigue: Parte A, sección A3. |
| **"El backend no arrancó"** (al instalar, actualizar o reiniciar) | El script muestra las últimas líneas del registro. Casi siempre es un error de sintaxis en `config\appsettings.Local.json` o un valor inválido en `config\.env` (por ejemplo `FACTUM_AUTH_MODE` distinto de `dev`/`external`/`local`). Corregilo y abrí "Diagnostico de Factum" (responder S a reiniciar). |
| **El celular no aparece** | Revisá el cable (que sea de datos), el driver (sección 8), la depuración USB / "Confiar en esta computadora", y que Tatana esté corriendo ("Diagnostico de Factum"). Si Tatana no responde: cerrá sesión de Windows y volvé a entrar, o abrí `%LOCALAPPDATA%\Programs\Tatana\launch-tatana.bat`. |
| **El diagnóstico dice `mock: true` / "modo simulado"** | Tatana está simulando celulares: **no usar para peritajes**. Avisá al proveedor. |
| **El informe falla o Factum se reinicia al generar** (PC con poca memoria) | Ver sección 2.1, "Si un informe falla o Factum se reinicia al generar". |
| **Se llenó el disco** | Borrá backups viejos de `C:\Factum\backups` (después de copiarlos a otro disco) e imágenes de versiones viejas (sección 11). La evidencia de `C:\Factum\evidencia` **no se borra a mano**. |

Todos los scripts dejan su registro en `C:\Factum\logs\`. Para pedir ayuda, mandá el archivo
de "Diagnostico de Factum".

## 13. Si la PC no soporta virtualización

Si la PC no tiene virtualización (o no se puede habilitar), Docker Desktop no funciona y
Factum no se puede instalar así. **Contactá al proveedor** para evaluar otra alternativa.

## 14. Para el desarrollador: cómo se arma el paquete

En la Mac, desde la raíz del repo:

```bash
deploy/windows/armar-paquete.sh --version 1.0.0
```

- Usa `git archive` de `--ref` (default `HEAD`): **lo que no está commiteado no entra** (avisa
  qué cambios quedan afuera). Verifica que `Agent:Mock` sea `false` (en la fuente y en el
  publish de Tatana).
- Construye `factum-backend:X` y `factum-frontend:X` para **linux/amd64** (el frontend se
  compila emulado: puede tardar más de 10 minutos), descarga `mongo:7.0.x` (el tag exacto sale
  de `deploy/windows/docker-compose.yml`), y verifica que el `.tar` traiga solo amd64.
- Arma Tatana con `deploy/windows/armar-tatana-portable.sh` (descarga ~300 MB: adb, Python
  embebido + pymobiledevice3, ffmpeg y **scrcpy (versión fija, verificada por SHA-256)**) o
  usa uno ya armado con `--tatana-zip <ruta>`. scrcpy va en `tools\scrcpy\` sin su `adb.exe`
  propio (Tatana usa el de `tools\platform-tools`), con su `LICENSE.txt` y un
  `THIRD-PARTY-NOTICES.txt`. Para actualizar scrcpy se cambian juntas `SCRCPY_VERSION` y
  `SCRCPY_WIN64_SHA256` al principio del script. Al final, el script verifica el contenido del
  zip y corta si falta algo o si viaja el adb de scrcpy.
- Deja `deploy/windows/dist/Factum-Instalacion-vX.Y.Z/` y su `.zip` (con `SHA256SUMS.txt`,
  verificable con `shasum -a 256 -c`). `dist/` está en `.gitignore`.

Requisitos en la Mac: Docker Desktop con buildx, `git`, `python3`, `zip`, `shasum`, `dotnet`
10 (para Tatana) e **internet** (el build descarga dependencias de npm/NuGet y las fuentes de
la web; la PC del estudio no necesita internet).

Tests de los scripts (Pester 5 + PSScriptAnalyzer), sin Windows:

```bash
docker run --rm -v "$PWD/deploy/windows:/w" mcr.microsoft.com/powershell:lts-ubuntu-22.04 pwsh -NoProfile -Command \
  "Install-Module PSScriptAnalyzer -Force -Scope CurrentUser; Install-Module Pester -MaximumVersion 5.99 -Force -Scope CurrentUser -SkipPublisherCheck; \
   Invoke-ScriptAnalyzer -Path /w/scripts -Settings /w/scripts/PSScriptAnalyzerSettings.psd1; Invoke-Pester /w/tests -Output Detailed"
```

Para probar el perfil de poca memoria en una VM o una PC con más RAM:
`powershell -ExecutionPolicy Bypass -File scripts\instalar.ps1 -PerfilMemoria poca`.

Para comparar la configuración efectiva de los dos perfiles sin levantar nada (en la Mac,
con un `.env` de prueba que tenga `COMPOSE_PROJECT_NAME`, `FACTUM_VERSION`, `FACTUM_HOME` y
`FACTUM_JWT_SECRET`; **nunca** el proyecto `factum` real):

```bash
C="docker compose -p factum-verif --project-directory <home de prueba> --env-file <.env de prueba>"
$C -f deploy/windows/docker-compose.yml config > normal.yml
$C -f deploy/windows/docker-compose.yml -f deploy/windows/docker-compose.poca-ram.yml config > poca.yml
diff normal.yml poca.yml   # solo mem_limit de los 3, command de mongo, DOTNET_gcServer y NODE_OPTIONS
```
