# Resolución 1616 — ¿Hay que cambiar el texto del Manual?

**Fecha del análisis:** 2026‑09‑02
**Documento analizado:** `Resolución 1616 - Manual de uso EVIDENTIA.pdf` (Salta, 15/05/2025 — Anexo I: *"Manual de uso de la herramienta Evidentia para inspecciones forenses en dispositivos electrónicos"*).
**Comparado contra:** el estado actual de la aplicación (código y funcionalidad al día de hoy).

---

## 1. Veredicto

**Sí, la versión actual difiere lo suficiente como para justificar una modificación del Anexo I (el Manual).**

- La **parte resolutiva** de la Resolución (RESUELVE I / II / III) **no necesita cambios**: se limita a *aprobar* el Manual y a *encomendar* su implementación.
- El **Anexo I (Manual)** sí necesita actualizarse. Los cambios más importantes:
  1. **El Manual es 100 % Android.** La versión actual también inspecciona **iPhone / iOS**, con un stack técnico distinto.
  2. **El informe ya no se entrega en PDF, sino en Word (`.docx`).**
  3. **La foto de identificación se toma con la webcam de la PC forense, no "con la propia cámara del dispositivo".** Además cambió la terminología: *Fiscal* / *Denunciante* en lugar de *Funcionario* / *Usuario*.
  4. **Se incorporó un explorador de archivos del dispositivo** que trae archivos puntuales del almacenamiento a la evidencia — matiza el principio de *"no descarga de archivos / solo lectura de pantalla"*.
  5. **Grabación con audio del sistema** (Android ≥ 11) y opción de **mezclar el micrófono de la PC**.
  6. Hay **inconsistencias internas** en el Manual que conviene corregir de paso (contraseña de 24 vs 16 dígitos; "PDF" vs "python‑docx / DOCX").

| # | Tema | Sección del Manual afectada | Gravedad |
|---|------|-----------------------------|----------|
| A | Soporte iPhone / iOS | 1, 2.a‑e, 8 (a, c, d, e, f, h), 9 | **Alta** — el Manual no lo contempla |
| B | Informe en Word, no PDF | 2.g, 8.c | **Alta** — dato objetivo desactualizado |
| C | Foto de identidad con webcam de la PC | 2.b, 2.g | **Media** — descripción incorrecta |
| D | Explorador de archivos del dispositivo | 1, 2.d, 8.c‑d | **Media/Alta** — toca un principio forense |
| E | Grabación con audio del sistema + mic de PC | 1, 2.d, 8 (c, d) | **Media** — capacidad nueva no descripta |
| F | Terminología Fiscal/Denunciante | 2.b, 2.g | **Baja** — cosmético pero conviene |
| G | Inconsistencias internas ya presentes | 2.f vs 2.g; 2.g vs 8.d | **Baja** — corregir al pasar |
| H | Versiones fijadas (ADB 34.0.5, scrcpy) | 8.c, 8.d | **Baja** — verificar y/o quitar el número |

---

## 2. Cambios que requieren modificar el texto del Manual

### A. Alcance: la herramienta ya no es solo Android

**Qué dice hoy el Manual:**
- Sección **1 (Función):** *"…dispositivos electrónicos que cuenten con tecnología **Android**…"*.
- Sección **2.b:** *"al conectar el dispositivo… al cable USB, el sistema instalado en la computadora… automáticamente procede a registrar…"* (flujo ADB por USB).
- Sección **8 (Especificaciones técnicas):** describe **tres componentes esenciales — Python, ADB (Android Debug Bridge) y scrcpy** — y todo el *flujo de trabajo*, el *flowchart* y el *procedimiento detallado de adquisición* están descriptos para **"Dispositivo Android"** (`scrcpy-server.jar`, `adb exec-out screencap`, `scrcpy --record`, etc.).
- Sección **9:** guía de depuración USB solo para marcas Android.

**Qué hace la versión actual:**
- Además de Android, inspecciona **iPhone (iOS)** con un stack propio:
  - Preparación: instalar **"Apple Devices"** en la PC, conectar con el iPhone desbloqueado, **"Confiar en esta computadora"**, **desactivar el código de pantalla**, activar el **Modo Desarrollador** mediante el comando `pymobiledevice3 amfi enable-developer-mode`.
  - Captura de pantalla iOS y grabación con varios modos: `video_only`, `with_mic`, `on_device` (grabación nativa del iPhone) y **`airplay`** (espejado por AirPlay).
  - Sesión de **"Espejar para capturas"** por AirPlay (marcar varios momentos y extraer un PNG por marca).
  - Post‑proceso de video con **variantes de interpolación** (para elevar los fps del video de iOS).
- La guía de conexión dentro de la app tiene una **pestaña Android / iPhone** y muestra las **fichas oficiales del GIF** por marca.

**Sugerencia de redacción:**
- En **Sección 1** cambiar *"tecnología Android"* → *"sistemas operativos Android e iOS"* (o *"Android y, según su versión, iOS/iPadOS"*).
- Agregar en **Sección 2** un ítem o aclaración de que, para **iOS**, la preparación y el flujo técnico difieren (Modo Desarrollador de Apple en lugar de Depuración USB; herramientas `pymobiledevice3` / espejado por AirPlay en lugar de ADB/scrcpy).
- En **Sección 8** desdoblar las especificaciones en **8‑Android** (Python + ADB + scrcpy, ya redactado) y **8‑iOS** (Python + `pymobiledevice3` + captura por servicios de desarrollador / AirPlay). Aclarar que el **modo "solo lectura"**, el **hashing SHA‑256**, el **log** y el **empaquetado ZIP AES‑256** son comunes a ambas plataformas.
- En **Sección 9** aclarar que para iPhone no aplica la "Depuración por USB" sino la activación del **Modo Desarrollador de iOS**, y remitir a la guía in‑app.

---

### B. El informe se entrega en Word, no en PDF

**Qué dice hoy el Manual:**
- Sección **2.g:** *"el sistema, al finalizar la inspección, genera un **reporte en formato PDF**, a modo de informe técnico…"*.
- Sección **8.c** (*Reportes*): *"se genera un reporte forense en **formato PDF** con el detalle de lo ingresado…"*.
- (Nota: el propio Manual ya se contradice — la tabla de **8.d** dice *"Informe: **python‑docx**"* y la lista de archivos generados de **2.g** incluye *"DOCX"*.)

**Qué hace la versión actual:**
- El informe oficial se genera en **Word (`.docx`)**. En la app, la pantalla de resultado lo rotula *"Informe Word"* / *"Word oficial generado"* y la contraseña del ZIP se indica como *"Incluida en el informe Word"*.

**Sugerencia de redacción:**
- Reemplazar *"reporte en formato PDF"* por *"informe oficial en formato Word (`.docx`)"* en **2.g** y **8.c**.
- Dejar la mención de PDF solo si se sigue generando además un PDF (verificar — ver Sección 4).

---

### C. La foto de identificación se toma con la webcam de la PC forense

**Qué dice hoy el Manual:**
- Sección **2.b (último párrafo):** *"…el sistema permite la realización de una **toma fotográfica** de cada uno de ellos, **a través de la propia cámara del dispositivo**."*
- Sección **2.g:** *"datos del funcionario… nombre, apellido, DNI **y toma fotográfica**"* y *"datos del Usuario que aporta el dispositivo"*.

**Qué hace la versión actual:**
- La foto del **Fiscal** y del **Denunciante** se toma con la **webcam de la computadora forense** (el módulo abre `getUserMedia` con la cámara frontal de la PC), **no** con la cámara del teléfono inspeccionado.
- Es opcional y queda registrada en la evidencia (`foto_funcionario_*.jpg`, `foto_denunciante_*.jpg`).

**Sugerencia de redacción:**
- En **2.b** cambiar *"a través de la propia cámara del dispositivo"* → *"a través de la cámara de la computadora forense"* (o *"webcam del equipo del Ministerio Público"*).

---

### D. Explorador de archivos del dispositivo (adquisición de archivos puntuales)

**Qué dice hoy el Manual:**
- Sección **1:** *"…permite registrar adecuadamente la visualización de la información… **sin descargar los archivos o efectuar una extracción forense**."*
- Sección **2.d (Resguardo de la evidencia):** *"Dicha actividad **no permite la descarga de los archivos** contenidos en el dispositivo inspeccionado, **sólo permite capturar o registrar lo reproducido** a través de él."*
- Sección **8.f / 8.h:** el análisis de no‑intrusividad y la conformidad con ISO/IEC 27037 §7.3 se apoyan en que la operación es *"solo lectura"* y *"no invasiva"*, sin extracción lógica ni física.

**Qué hace la versión actual:**
- Existe la opción **"Explorar archivos del celular"** (para Android): abre un explorador del almacenamiento del dispositivo, permite navegar carpetas (Descargas, etc.) y **buscar por aplicación**, y **trae archivos concretos a la evidencia** (`agent.pullDeviceFiles` → `/devices/{serial}/explorer/pull`). Cada archivo traído conserva su **ruta de origen en el dispositivo**, que se registra en el informe.
- Es un método de respaldo, agrupado bajo *"Más formas de capturar"*, pero **es una adquisición lógica de archivos puntuales** — no solo captura de pantalla.

**Por qué importa:** cambia parcialmente la caracterización forense de la herramienta. Deja de ser exclusivamente *"duplicación / inspección de pantalla"* para incluir, opcionalmente, la **obtención selectiva de archivos** del almacenamiento accesible. Puede tener implicancias en el **encuadre jurídico** (Sección 3) y en las **formalidades** (Sección 4), y contradice literalmente lo que hoy afirman las Secciones 1 y 2.d.

**Sugerencia de redacción:**
- Agregar en **Sección 2** un ítem específico (p. ej. *2.h — "Obtención de archivos puntuales"*) que describa la función, su carácter **excepcional y a criterio del operador**, y el registro de la ruta de origen.
- Ajustar las frases absolutas de **1** y **2.d**: *"como regla general, no descarga archivos; excepcionalmente, mediante el explorador de archivos, puede incorporarse a la evidencia un archivo puntual del almacenamiento accesible, dejando constancia de su ruta de origen y de su hash"*.
- Revisar con el área jurídica si esto exige una mención en la **Sección 3 (Encuadre jurídico)** o en la **Sección 4 (Formalidades)**.

---

### E. Grabación con audio del sistema y micrófono de la PC

**Qué dice hoy el Manual:**
- El Manual describe la grabación como *"registros de video"* de la pantalla, sin referirse a la captura de audio (Secciones 2.d y 8). El *flowchart* usa `scrcpy --record … --no-control` (video).
- (La caracterización histórica de la herramienta como *"sin sonido ambiente ni el entorno del acto"* sigue siendo cierta y **no cambia**.)

**Qué hace la versión actual:**
- En **Android ≥ 11**, la grabación de pantalla **incluye el audio del sistema del dispositivo** (por ejemplo, reproducir una nota de voz de WhatsApp). En **Android 9–10** es solo video.
- Opción **"Mezclar micrófono de la PC"**: incorpora el micrófono de la computadora forense a la grabación (útil cuando el audio digital no se puede capturar). Esto puede introducir audio del ambiente del operador **de forma deliberada y controlada**.
- En iOS hay modos con audio (`with_mic`).

**Sugerencia de redacción:**
- En **Sección 2.d / 8** aclarar que la grabación puede incluir el **audio del sistema del dispositivo** (según versión de SO) y, opcionalmente, el **micrófono de la PC** cuando el operador lo active, y que ese hecho queda reflejado en el informe / observaciones.

---

### F. Terminología: "Fiscal" / "Denunciante"

**Qué dice hoy el Manual:** *"datos del **"Funcionario"** que lleva a cabo la operación y del **"Usuario"** que aporta el dispositivo"* (2.b, 2.g).

**Qué hace la versión actual:** la app rotula estos roles como **"Fiscal" (rol: Funcionario)** y **"Denunciante" (rol: Titular)**.

**Sugerencia:** unificar terminología. Si se mantiene "Funcionario/Usuario" en el texto legal, dejar una nota de equivalencia; si se adopta la de la app, actualizar 2.b y 2.g.

---

## 3. Inconsistencias internas que ya existen en el Manual (corregir de paso)

1. **Longitud de la contraseña del ZIP:**
   - Sección **2.f:** *"el sistema genera una contraseña de longitud de **veinticuatro dígitos**"*.
   - Sección **2.g** (último bullet): *"se documenta una **contraseña de 16 dígitos** y su Hash SHA‑256"*.
   → Debe unificarse a la longitud real que genera el sistema hoy (**verificar** — ver Sección 4).

2. **Formato del informe (ya mencionado en B):** la prosa dice *"PDF"* (2.g, 8.c) pero la tabla de **8.d** dice *"python‑docx"* y **2.g** lista *"DOCX"* entre los archivos generados. Unificar a **Word (`.docx`)**.

---

## 4. A verificar con el equipo / backend antes de redactar

Estos puntos no pude confirmarlos desde el front‑end; necesitan chequeo con el agente/backend:

- **Longitud exacta y método de derivación de la contraseña del ZIP** hoy (¿24? ¿16? ¿PBKDF2‑HMAC‑SHA‑256 con semilla de 128 bits, como dice 2.f?).
- **¿Se sigue generando también un PDF** además del `.docx`, o el `.docx` lo reemplazó por completo?
- **Versión de ADB** empaquetada hoy (el Manual fija *"Version 34.0.5"* en 8.c/8.d). Recomendación: o actualizar el número, o quitarlo y remitir al `evidentia.log`, que ya registra el versionado (8.e "Versionado").
- **Herramientas exactas del pipeline iOS** (nombre y versión de `pymobiledevice3`, del receptor AirPlay, del interpolador de video) para redactar la Sección 8‑iOS con precisión.
- **Formatos de archivo** que produce hoy la herramienta (el Manual lista *PNG, MKV, DOCX, ZIP* en 2.g; confirmar si iOS agrega `.mov`/otro y si el video sigue siendo `.mkv`).
- **Alcance del explorador de archivos**: qué orígenes permite (solo almacenamiento accesible por MTP/ADB shell, o también rutas de apps), para acotar bien la redacción del punto D.

---

## 5. Lo que **NO** necesita cambios

- **Parte resolutiva** (RESUELVE I/II/III).
- **Sección 3 — Encuadre jurídico** (art. 293 y 282 CPP, *"inspección de cosas"*). *Salvo* que jurídicamente se decida reflejar el punto D (explorador de archivos).
- **Sección 4 — Formalidades mínimas** (consentimiento del titular / autorización judicial). Igual salvedad por el punto D.
- **Sección 5 — Sugerencias prácticas para la inspección** (imágenes, videos, audio, redes sociales, geo‑referenciación, correo, documentos). Siguen vigentes.
- **Sección 6 — Recomendaciones para la audiencia de debate.**
- **Sección 7 — Recomendaciones para el usuario/titular.**
- **Garantías de integridad** (Sección 2.e y 8.e): hash SHA‑256 individual + hash maestro del ZIP + `evidentia.log` en ISO‑8601 + cifrado AES‑256. Se mantienen.
- **Sección 9 — Guía de depuración USB por marca** (Motorola, Samsung, Xiaomi, Huawei, LG): los pasos siguen siendo correctos y la app hoy muestra **exactamente esas fichas oficiales**. Solo faltaría **agregar la aclaración de iPhone** (ver punto A) y, si se quiere, mencionar el caso *"Otra marca"* (Android genérico) que la app ya contempla.

---

## 6. Confianza de este análisis

- **Alta confianza** (verificado en el código y/o en el uso de la app): soporte iOS y su flujo (Modo Desarrollador, AirPlay, modos de grabación, interpolación de video); informe en Word; foto de identidad con webcam de la PC; explorador de archivos que trae archivos del dispositivo; grabación con audio del sistema en Android ≥ 11 y mezcla de micrófono de PC; terminología Fiscal/Denunciante; guía de marcas Motorola/Samsung/Xiaomi/Huawei/LG + "Otra marca".
- **A confirmar** (Sección 4): longitud real de contraseña, si persiste generación de PDF, versión de ADB, nombres/versiones exactas del stack iOS, listado definitivo de formatos.

> Este README es un insumo técnico para que el área que redacta la Resolución evalúe los cambios. No modifica ningún documento oficial.
