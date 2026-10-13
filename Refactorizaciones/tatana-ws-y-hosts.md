# SDD: Tatana no se traba por un WebSocket lento, se apaga rápido y solo atiende pedidos dirigidos a esta PC

**Slug:** `tatana-ws-y-hosts` · **Issue:** #60 · **HU:** `docs/hu-tatana-ws-y-hosts.md`. Validada el 2026-10-13
por delegación del usuario: D1 a D6 quedaron con la opción recomendada (D1 = A cola por cliente, D2 = A solo filtro
de `Host`, D3 = B hosts locales + `Agent:AllowedHosts`, D4 = A ignorar el archivo entero, D5 = A bajas T-05, T-06,
T-07, T-08, T-09, T-13 y T-14, D6 = A sin cambios en `client/`).
**Fuente QA:** `docs/qa/2026-10-13/tatana.md` (T-01 a T-04, T-05 a T-09, T-13 y T-14).
**Base del análisis:** `develop` `36140d4`. Es una SDD **adelantada**: se escribió desde la rama
`feat/zip-verificacion-stored` sin tocar código (ver §12, "Concurrencia con otras HU").

---

## 1. Resumen funcional

Tatana deja de depender de la velocidad de sus clientes WebSocket. Emitir un evento pasa a ser encolarlo: cada cliente
tiene una cola acotada y una tarea propia que le envía en orden y con timeout. Un cliente que no lee se desconecta y
no traba ni a la request que emitió el evento, ni a los otros clientes, ni a la actualización automática. Al apagarse,
Tatana manda un close 1001 a cada WebSocket, corta el polling de dispositivos y termina en segundos, no en 30. Para
cerrar el DNS rebinding se activa el filtro de `Host` de ASP.NET: solo se atienden requests dirigidas a esta PC
(`localhost`, `127.0.0.1`, `[::1]`, la IP de `Agent:BindAddress` o las IP locales si escucha en `0.0.0.0`/`::`, más
la lista opcional `Agent:AllowedHosts`). Todo lo demás recibe 400 sin datos. Si `appsettings.Local.json` tiene JSON
válido pero valores inválidos, ahora se **ignora entero** y se avisa en `/agent/state.local_config.error`, igual que
con un JSON roto (D-T16): Tatana arranca igual. Además entran siete bajas del QA. Se borran al arrancar los `.part`
huérfanos del ZIP. El ZIP ya no deja carpetas vacías cuando falla. Hay códigos específicos para la evidencia que
desaparece a mitad del armado (409) y para el disco lleno (507). Se rechazan las contraseñas vacías o de más de 99
bytes y los nombres con controles bidi o C1. Detener una grabación inexistente devuelve 409 con `code`.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **no** | La copia de `EvidenceZip.cs` del backend **no** se toca: la validación de la contraseña va en `CaseZipService`, no en `EvidenceZip`, así las dos copias siguen idénticas. |
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | Hub WS, `Program.cs` (apagado, polling, filtro de `Host`, config local), `Common/` (`HostPolicy` y `LocalConfigValidator` nuevos, `LocalConfigInspector`, `AgentFileNames`, `AgentErrorCodes`, un helper de disco lleno), `CaseZipService`, `CaseEvidenceStore`, `AdbService`, `IosService`, `RecordingController`, `appsettings.json`, `Models/AgentModels.cs`. |
| **tests** `server/tests/Factum.Agent.Tests` | **sí** | Nuevos: `AgentWebSocketHubTests.cs`, `HostPolicyTests.cs`, `LocalConfigValidatorTests.cs`. Se amplían: `LocalConfigInspectorTests.cs`, `CaseZipServiceTests.cs`, `AgentFileNamesTests.cs`. |
| **docs** | **sí** | `README.md`: fila `Agent:AllowedHosts` en la tabla de `server/src/Factum.Agent/appsettings.json` y una nota sobre el filtro de `Host` en `Agent:BindAddress`. |
| **client** | **no** | D6 = A. Ya mapea `evidence_changed` e `insufficient_storage` (`client/src/lib/agent-messages.ts:81-101`) y reconecta el WS en `onclose`/`onerror` (`client/src/hooks/useAgentConnection.ts:48-57`). |
| **agent-ui** | **no** | El aviso "Configuración local con error" ya existe (`agent-ui/src/renderer/src/components/UpdateStatus.tsx:268-272`, `agent-ui/src/main/app-info.ts:77-86`). Solo cambia el texto de `error` que manda Tatana. |
| **Mongo** | **no** | No hay datos nuevos ni cambios de modelo. |

**Un solo implementador: `implementer-backend`.** No se lanza `implementer-frontend` en esta HU.

---

## 3. Modelo de datos

No hay cambios en MongoDB. Lo que cambia es la configuración de Tatana y lo que se toca en disco.

### 3.1 Configuración (`AgentOptions`, `Models/AgentModels.cs`)

| Clave | Tipo | Default | Nuevo / existente | Reglas |
|---|---|---|---|---|
| `Agent:AllowedHosts` | `string[]` | `[]` | **nuevo** | Hosts **extra** que puede traer el header `Host`, sin esquema, sin puerto, sin path y sin `*`: un nombre (`tatana.local`), una IPv4 (`192.168.1.10`) o una IPv6, con o sin corchetes. Inválido en `appsettings.json`, env o CLI → no arranca. Inválido en la config local → se ignora el archivo entero (D4). |
| `AllowedHosts` (raíz, de ASP.NET) | `string` | `"*"` hoy | **se borra** de `appsettings.json` | Tatana lo **ignora** venga de donde venga (D-A6). El filtro se arma solo con `HostPolicy`. |

El resto de las claves no cambia. La validación de la config local (§4.4) usa las mismas reglas que hoy aplica
`Program.cs` a la config completa: `MaxUploadBytes > 0`, `MinFreeBytes >= 0` y `OriginPolicy`. Suma tres más:
`Port` entre 1 y 65535, `BindAddress` que sea `localhost` o una IP, y `AllowedHosts` según `HostPolicy`.

### 3.2 Disco

| Ruta | Qué cambia |
|---|---|
| `<EvidenceDirectory>/<caso>/.factum/pendiente/evidencia_*.zip.part` | Se borra **al arrancar** (T-05). Solo archivos que cumplan exactamente ese patrón. Nunca se borran carpetas, `.zip` pendientes ni `.sha256`. |
| `<EvidenceDirectory>/<caso>/` y `.factum/pendiente/` | Se crean recién cuando el ZIP empieza a escribirse (T-06). Si el armado falla después, se borran **solo** las carpetas que esta misma llamada creó y **solo si quedaron vacías**. |

---

## 4. Diseño

### 4.1 T-01: envío WS por cola acotada por cliente (`WebSockets/AgentWebSocketHub.cs`)

**Constantes** (internas, sin clave de config, D-A1):

| Constante | Valor | Para qué |
|---|---|---|
| `QueueCapacity` | 256 mensajes | Cola por cliente. Con el polling cada 3 s y `zip_progress` cada 500 ms, un cliente sano nunca pasa de un puñado. |
| `SendTimeout` | 10 s | Tope de cada `SendAsync` individual. |
| `PingInterval` | 25 s | El `{"type":"ping"}` de hoy, que ahora pasa por la cola. |
| `CloseTimeout` | 1 s | Cuánto se espera a que cada enviador drene y mande el close al apagar o al desconectarse. |

Para los tests, el hub tiene un constructor `internal` que recibe esos cuatro valores. El público, el de DI, usa los
defaults.

**Estructura:**

```text
Client { Guid Id; WebSocket Ws; Channel<Outgoing> Queue; CancellationTokenSource Cts; Task Sender; int Dropped; }
Outgoing = (ReadOnlyMemory<byte> Payload) | Close(WebSocketCloseStatus, string reason)
_clients: ConcurrentDictionary<Guid, Client>
_closing: volatile bool        // ShutdownAsync en curso: los Broadcast se ignoran sin log
_stopping: CancellationTokenSource   // corta los loops de lectura al apagar
```

- La cola se crea con `Channel.CreateBounded<Outgoing>(new BoundedChannelOptions(QueueCapacity) { SingleReader = true,
  SingleWriter = false, FullMode = BoundedChannelFullMode.Wait })`. Se escribe **solo con `TryWrite`**: con `Wait`,
  `TryWrite` devuelve `false` cuando la cola está llena y nunca bloquea.
- **El único que llama a `SendAsync` o `CloseOutputAsync` sobre un socket es su `Sender`.** Con eso se cumple la
  regla de un solo envío a la vez por `WebSocket`, sin locks. El ping y el close también pasan por la cola.

**`BroadcastAsync(AgentEvent evt, CancellationToken ct = default)`:**
- Mantiene la firma de `IAgentEventSink` y deja de ser `async`.
- Serializa una sola vez, con el mismo `JsonOpts` snake_case de hoy, a un `byte[]` que comparten todos los clientes.
  Si la serialización tira, lo loguea como error y devuelve `Task.CompletedTask`, sin propagar.
- Si `_closing` está en `true`, vuelve sin hacer nada.
- Para cada cliente: si `Ws.State != Open`, hace `Drop(client, "cerrado")`. Si no, intenta `Queue.Writer.TryWrite`; si
  devuelve `false`, hace `Drop(client, "cola llena")`.
- **Devuelve `Task.CompletedTask` siempre.** El `ct` se ignora.
- **Cambio de semántica, queda escrito en el XML doc de la interfaz:** que la `Task` complete significa "encolado",
  no "entregado". Ningún llamador depende hoy de la entrega: los controllers devuelven la respuesta HTTP de todos
  modos, y `VideoPostprocessService:652` y `ZipProgress` ya tragan cualquier excepción.

**Enviador (`SendLoopAsync(client)`, uno por cliente, arrancado con `Task.Run`):**

```text
await foreach (item in client.Queue.Reader.ReadAllAsync(client.Cts.Token))
    using timeout = CTS.CreateLinkedTokenSource(client.Cts.Token); timeout.CancelAfter(SendTimeout)
    if item es Close → await Ws.CloseOutputAsync(status, reason, timeout.Token); return
    else             → await Ws.SendAsync(item.Payload, Text, endOfMessage: true, timeout.Token)
catch OperationCanceledException cuando venció el timeout (no client.Cts) → Drop(client, "timeout de envío")
catch WebSocketException / ObjectDisposedException / IOException          → Drop(client, "error de envío")
catch OperationCanceledException por client.Cts                            → salir en silencio
```

**`Drop(client, motivo)`:**
- Idempotente, con `Interlocked.Exchange(ref client.Dropped, 1)`.
- Saca al cliente de `_clients`, hace `Queue.Writer.TryComplete()`, `client.Cts.Cancel()` y `client.Ws.Abort()`.
- Loguea un **warning**: `"WS {Id} desconectado: {Motivo}"`, con `Motivo` = `cola llena` | `timeout de envío` |
  `error de envío` | `cerrado` | `apagado`. Cuando el motivo es `cerrado`, que es una desconexión normal, el nivel es
  `Debug`.
- **Decisión del arquitecto (D-A2):** a un cliente que se descarta por lento se le hace `Abort()`, no un close 1008.
  Cuando la cola se llena o vence el timeout, su enviador está trabado en un `SendAsync` con el buffer TCP lleno, así
  que el close frame no saldría nunca. El front ve `onclose`/`onerror` igual y reconecta (HU, "El front se recupera
  si Tatana lo desconecta").

**`HandleAsync(WebSocket ws, CancellationToken requestAborted)`:**
1. Si `_closing` está en `true`, manda `CloseOutputAsync(EndpointUnavailable, "going away")` con timeout de 1 s y
   vuelve.
2. Crea `linked = CreateLinkedTokenSource(requestAborted, _stopping.Token)` y el `Client`, arranca el `Sender` y lo
   agrega a `_clients`. Loguea `"WS client connected: {Id}"`, como hoy.
3. **Ping:** un `PeriodicTimer(PingInterval)` que, en cada tick y con el token `linked`, hace
   `TryWrite(PingPayload)` del `{"type":"ping"}` de hoy, que no cambia el contrato. Si `TryWrite` falla, hace
   `Drop(client, "cola llena")`.
4. **Lectura:** el loop de hoy con `ws.ReceiveAsync(buffer, linked.Token)`. Si recibe `Close` (el cliente cerró),
   encola `Close(NormalClosure, "")` para completar el handshake y sale.
5. **`finally`:**
   - Saca al cliente de `_clients` y hace `Queue.Writer.TryComplete()`.
   - Espera al `Sender` con `WaitAsync(CloseTimeout)`. Si no terminó, hace `Drop(client, "cerrado")`.
   - Espera la tarea de ping y loguea `"WS client disconnected: {Id}"`.

**`ShutdownAsync(TimeSpan timeout)`** es nuevo y público, y lo llama `Program.cs` (§4.2):
1. Pone `_closing = true`.
2. A cada cliente le encola `Close(EndpointUnavailable /*1001*/, "going away")` y hace `TryComplete()`. Si el close no
   entra en la cola, hace `Drop(client, "apagado")`.
3. Hace `await Task.WhenAll(senders).WaitAsync(timeout)` y se traga `TimeoutException`. A los que no terminaron les
   hace `Drop(client, "apagado")`.
4. Hace `_stopping.Cancel()`: los loops de lectura que quedaron esperando el close del cliente salen, y las requests
   `/ws` terminan.
- Los eventos que se encolaron antes del paso 1 (por ejemplo los `video_variant_status` `agent_stopping` del
  post-proceso) salen **antes** del close, porque el close va al final de la misma cola.

**`Dispose()`:** cancela `_stopping` y hace `Abort()`/`Dispose()` de los sockets que queden.

**`ZipProgress` (`CaseZipService.cs:391-457`)** no se toca. Ahora que `BroadcastAsync` vuelve sincrónicamente, la
lógica de `_sending` sobra, pero no hace daño. Se deja para no mezclar refactors.

**Llamadores:** ninguno cambia de firma. Los `await hub.BroadcastAsync(..., ct)` de `RecordingController`,
`ScreenshotController`, `WebcamController`, `DevicesController`, `IosService`, `CaseZipService`,
`VideoPostprocessService` y `Program.cs` siguen compilando y ahora completan al instante.

### 4.2 T-02: apagado rápido (`Program.cs`)

- **Un solo callback** de `app.Lifetime.ApplicationStopping` reemplaza al de las líneas 302-311. Hace, en este
  orden:
  1. `post.CancelAll("agent_stopping")` y `post.WhenIdleAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()`,
     que es lo de hoy. Encola los eventos terminales del post-proceso.
  2. `hub.ShutdownAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult()`: manda el close 1001 a todos, después de
     esos eventos.
  - Se usa un solo callback a propósito: con dos, el orden dependería del LIFO de `CancellationToken.Register`, que es
    frágil.
- **Polling de dispositivos (líneas 316-342):** queda `var stopping = app.Lifetime.ApplicationStopping;` y
  `while (!stopping.IsCancellationRequested)`.
  - Usa `await Task.Delay(3000, stopping)`, `adb.ListDevicesAsync(stopping)` y `ios.ListDevicesAsync(stopping)`.
  - Agrega `catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }` antes del
    `catch (Exception)` genérico, así no loguea warnings durante el apagado.
- `WebSocketController` no cambia: el hub linkea su propio `_stopping` con `RequestAborted`.
- Meta medida por la HU: proceso terminado en **menos de 2 s** con un WS abierto y sin post-proceso. El tope con
  post-proceso en curso es de 5 s más 1 s.

### 4.3 T-03: filtro de `Host` (`Common/HostPolicy.cs` nuevo, `Program.cs`, `appsettings.json`)

**`HostPolicy` es puro y testeable:**

```csharp
public static class HostPolicy
{
    public const string Key = "Agent:AllowedHosts";
    public static readonly IReadOnlyList<string> LocalHosts = ["localhost", "127.0.0.1", "[::1]"];

    /// Lista para HostFilteringOptions.AllowedHosts: LocalHosts + IP de bind (si es concreta)
    /// + IP locales (si bind es 0.0.0.0/::) + extras normalizados. Sin duplicados (OrdinalIgnoreCase).
    public static (IReadOnlyList<string> Hosts, List<string> Errors) Build(
        string bindAddress, IEnumerable<string?>? extra, Func<IEnumerable<IPAddress>> localAddresses);

    /// Valida y normaliza una entrada de Agent:AllowedHosts. IPv6 → "[addr]" sin scope id.
    public static bool TryNormalize(string? raw, out string host);
}
```

- **Reglas de `TryNormalize`:**
  - Se rechaza lo vacío, `*` o cualquier `*`, lo que tenga `://`, `/`, `?`, `#`, `@` o espacios, y un puerto
    (`host:123`). Una IPv6 sin corchetes se acepta porque tiene más de un `:`, y se normaliza a `[...]`.
  - Un nombre se valida con `Uri.CheckHostName(...) == UriHostNameType.Dns`.
  - Todo se guarda en minúsculas.
- **Mensajes de error** (en castellano, con el mismo estilo que `OriginPolicy`):
  - `Agent:AllowedHosts no admite "*": listá los hosts permitidos`.
  - `Agent:AllowedHosts: "<valor>" no es un host válido (nombre o IP, sin esquema, puerto ni path)`.
- **IP de bind:**
  - Si `bindAddress` es una IP concreta que no es loopback ni `Any`/`IPv6Any`, se suma formateada (IPv6 entre
    corchetes y sin scope id).
  - Si es `0.0.0.0` o `::`, se suman todas las `localAddresses()`: IPv4 cuando bind es `0.0.0.0`, IPv4 e IPv6 cuando
    es `::`.
- **`localAddresses` en producción** enumera `NetworkInterface.GetAllNetworkInterfaces()` con
  `OperationalStatus.Up` y toma sus `UnicastAddresses`. La lista se arma **al arrancar**: si la IP de la PC cambia
  (DHCP), hay que reiniciar Tatana o fijarla en `Agent:AllowedHosts` (D-A3, queda en el README).

**`Program.cs`**, después de calcular `bindAddress` (línea 153) y antes de `builder.Build()`:

```csharp
var (allowedHosts, hostErrors) = HostPolicy.Build(bindAddress,
    builder.Configuration.GetSection(HostPolicy.Key).Get<string[]>(), HostPolicy.MachineAddresses);
if (hostErrors.Count > 0)
    throw new InvalidOperationException(
        "Configuración inválida, Tatana no arranca:" + string.Concat(hostErrors.Select(e => "\n  - " + e)));
builder.Services.PostConfigure<HostFilteringOptions>(o =>
{
    o.AllowedHosts = allowedHosts.ToList();   // pisa el "AllowedHosts" de cualquier fuente (D-A6)
    o.AllowEmptyHosts = true;                 // default de ASP.NET; HTTP/1.1 sin Host ya lo rechaza Kestrel
});
```

- Después de `builder.Build()`, se agrega **`app.UseHostFiltering();` como primer middleware**, antes de la guarda de
  origen. ASP.NET ya lo agrega con un startup filter. Repetirlo explícito deja claro el orden y no depende de ese
  detalle; aplicarlo dos veces con las mismas opciones no tiene efecto.
- El `PostConfigure` que se registra en `Program.cs` corre **después** del que agrega
  `WebHost.ConfigureWebDefaults`, que solo completa la lista si está vacía. Con eso siempre gana la lista de
  `HostPolicy`, también cuando `appsettings.json` se recarga.
- Se loguea una línea de información al arrancar: `"Hosts permitidos: {Lista}"`.
- **`appsettings.json`:** se borra la línea `"AllowedHosts": "*"`.
- Un `Host` rechazado recibe **400** con el cuerpo estándar de `HostFilteringMiddleware` ("Bad Request - Invalid
  Hostname"), sin datos y sin headers CORS. Vale para **todas** las rutas, incluidas `/ws`, `/health`, `OPTIONS` y
  `/agent/maintenance`.
- `OriginPolicy` y la guarda de origen no cambian: el `Origin` dice de qué sitio viene la request y el `Host` a quién
  va dirigida.

**Quién llama hoy y con qué `Host`** (verificado en el repo, ninguno se rompe):
- El front usa `NEXT_PUBLIC_AGENT_URL`, con default `http://localhost:8765` en `docker-compose.yml:65`,
  `client/next.config.ts:9`, `deploy/windows/armar-paquete.sh:113` y `deploy/cloud/scripts/desplegar.sh:97`.
- `agent-ui` usa `http://localhost:<port>`.
- `deploy/windows/scripts/*.ps1`, `ci/tatana-windows/prueba-humo.ps1` y `ops/tatana/prueba-windows.ps1` usan
  `127.0.0.1` o `localhost`.

### 4.4 T-04: config local con valores inválidos se ignora (`Common/LocalConfigValidator.cs` nuevo, `LocalConfigInspector.cs`)

**`LocalConfigValidator`** es puro y testeable:

```csharp
internal static class LocalConfigValidator
{
    /// null = válido. Si no, texto en castellano que nombra la(s) clave(s), unidas con "; ", sin punto final.
    public static string? Validate(IConfiguration localOnly);
}
```

1. **Tipos.** Recorre por reflexión las propiedades públicas con setter de `AgentOptions`, y de
   `VideoPostprocessOptions` para `Agent:VideoPostprocess`. Para cada una, si `localOnly.GetSection("Agent:<Prop>")`
   existe, prueba `section.Get(prop.PropertyType)` dentro de `try`. Ante `InvalidOperationException` o
   `FormatException`, el error es:
   - numérico (`int`, `long`, `double`): `Agent:<Prop> no es un número: '<valor>'`;
   - `bool`: `Agent:<Prop> tiene que ser true o false: '<valor>'`;
   - otro: `Agent:<Prop> no tiene un valor válido: '<valor>'`.

   `<valor>` es `section.Value` cortado a 60 caracteres. Para `VideoPostprocess`, la clave sale como
   `Agent:VideoPostprocess:<Prop>`. Los **valores** de `VideoPostprocess` no se validan porque los corrige
   `Normalize()`; solo los tipos.
2. **Semántica.** Solo se revisan las claves presentes en el archivo local:
   - `Agent:AllowedOrigins`: los errores de `OriginPolicy.Parse(...)`, que ya vienen en castellano.
   - `Agent:MaxUploadBytes`: `Agent:MaxUploadBytes tiene que ser mayor que 0`.
   - `Agent:MinFreeBytes`: `Agent:MinFreeBytes tiene que ser mayor o igual que 0`.
   - `Agent:Port`: `Agent:Port tiene que estar entre 1 y 65535`.
   - `Agent:BindAddress`: `Agent:BindAddress inválido: '<valor>'. Usá localhost o una IP`, con la misma regla que
     `Program.cs:159-174`.
   - `Agent:AllowedHosts`: los errores de `HostPolicy.TryNormalize` sobre cada valor.
3. Un `"Agent": 3` (escalar) no es error: hoy el binder lo ignora y el test `OrigenesFueraDeAgentNoCuentan` lo cubre.
   El `AllowedHosts` de la raíz (de ASP.NET) **se ignora** sin error (HU, D4, duda derivada).

**`LocalConfigInspector.InspectText`**, después del chequeo de "es un objeto":
- Arma `new ConfigurationBuilder().AddJsonStream(new MemoryStream(UTF8(text))).Build()`.
- Si `Build()` tira `FormatException` o `InvalidDataException`, por ejemplo con claves duplicadas, que hoy pasan el
  `JsonDocument` y **tiran al arrancar** en el proveedor real, devuelve
  `new LocalConfigStatus(path, true, false, "JSON inválido: " + ex.Message, false)`.
- Si `LocalConfigValidator.Validate(...)` devuelve texto, devuelve `new LocalConfigStatus(path, true, false, error,
  false)`.
- Si no, sigue como hoy: `Loaded = true` y `OverridesAllowedOrigins` calculado.

**`Program.cs`** no cambia en este punto. Si `Loaded` es `false`, el archivo ya no se agrega como fuente, y el log
de error de las líneas 270-272 lo cubre. Las validaciones de las líneas 109-120 siguen tirando para
`appsettings.json`, las variables de entorno y la CLI (HU, "La config principal inválida sigue impidiendo
arrancar").

**Texto que ve el perito** en la ventana: `La configuración local (<ruta>) tiene un error y se ignoró: Agent:MaxUploadBytes
no es un número: '16GB'.` La frase de afuera la pone `UpdateStatus.tsx`, que le saca el punto final a `error`.

### 4.5 Bajas del QA (D5 = A)

**T-05: `.part` huérfano** (`CaseZipService`, `ICaseZipService`, `Program.cs`):
- Se agrega `int CleanupOrphanParts()` a `ICaseZipService`.
- Si `EvidenceDirectory` no existe, devuelve 0. Si existe, recorre `Directory.EnumerateDirectories(EvidenceDirectory)`
  y, para cada `<d>/.factum/pendiente/`, borra los archivos que cumplan
  `^evidencia_[\p{L}\p{N}_]{1,60}\.zip\.part$`. El patrón es `ZipNameRegex` con `.part`; conviene exponerlo como
  `AgentFileNames.IsZipPartFilename`.
- Cada error se loguea como warning y no corta. Devuelve la cantidad de archivos borrados.
- `Program.cs:278-288` lo llama junto con `CleanupOrphanUploads` y suma `"ZIP parciales huérfanos borrados: {Z}"` al
  log existente.
- No borra carpetas, `.zip` pendientes (esperan el commit) ni `.sha256`.

**T-06: carpetas vacías** (`CaseZipService.BuildAsync`):
- `Directory.CreateDirectory(p.PendingDir)` (línea 139) se mueve al principio del bloque `try` de los pasos 5 a 7,
  antes de `DeleteQuietly(p.PendingPart)`. Mantiene su mapeo a 500 `storage_error`.
- Antes de crearla se registra cuáles de `p.Final`, `meta` y `p.PendingDir` **no existían**.
- En el `catch` de los pasos 5 a 7, después de borrar `.part`, `.zip` y `.sha256`, se borran esas carpetas de la
  más profunda a la más alta, con `Directory.Delete(dir)` **no recursivo**, solo si están vacías. Un error se loguea
  como warning y se ignora.
- **El chequeo de espacio (línea 149) ya no puede medir `p.Final`**, porque todavía no existe. En macOS,
  `DriveInfo` sobre una ruta inexistente tira y el probe devuelve `null`, o sea que el chequeo se saltearía. Se
  agrega el helper `internal static string ExistingAncestor(string path)`, que sube por los padres hasta uno que
  exista y, como último recurso, devuelve la raíz. Se usa en el prechequeo y en T-07.

**T-07: códigos específicos del ZIP** (`CaseZipService.BuildAsync`, `catch` de los pasos 5 a 7):
- **Antes** de borrar `.part`, `.zip` y `.sha256`:
  1. Si `ex` es `FileNotFoundException` o `DirectoryNotFoundException`, se calcula
     `missing = paths.Where(p => !File.Exists(p))`. Si hay alguno, la respuesta es **409** `evidence_changed` con
     `files = [{ filename, reason: "missing" }]`, la misma forma que el 409 del paso 4.
  2. Si `ex` es `IOException` y `DiskErrors.IsDiskFull(ex)` es verdadero, o si el probe sobre `ExistingAncestor(p.Final)`
     da menos que `total`, se mide `available` **en ese momento**, todavía con el `.part` ocupando espacio. La
     respuesta es **507** `insufficient_storage` con `required_bytes = total + _minFreeBytes` y
     `available_bytes = available`. El mensaje es el del prechequeo: "No hay espacio en el disco de esta PC para
     guardar el ZIP (…)".
  3. Cualquier otra excepción sigue como hoy: 500 `zip_failed`, y `EvidenceZipVerificationException` conserva su
     mensaje.
- Después se limpia como hoy y se tira la excepción que corresponda.
- La clasificación va en un método `internal static` testeable,
  `ClassifyBuildFailure(Exception ex, IReadOnlyList<string> paths, Func<long?> freeBytes, long total, long minFree)`,
  que devuelve `AgentHttpException`.
- **`DiskErrors.IsDiskFull(IOException)`** (`Common/DiskErrors.cs`, nuevo) saca a un lugar común el chequeo de
  `HResult` de `CaseEvidenceStore.cs:303`: `28` (ENOSPC), `0x80070070` y `0x80070027`. `CaseEvidenceStore.IsDiskFull`
  pasa a usarlo y mantiene su fallback con el probe.

**T-08 y T-09: contraseña del ZIP** (`CaseZipService.BuildAsync`, al principio, antes del lock y de tocar disco):
- Si `request.Password is not null` y `request.Password.Length == 0`, responde **400** `invalid_password`: "La
  contraseña del ZIP no puede estar vacía".
- Si `request.Password is not null` y `Encoding.UTF8.GetByteCount(request.Password) > 99`, responde **400**
  `invalid_password`: "La contraseña del ZIP no puede superar los 99 bytes (7-Zip no abriría el ZIP)".
- `null` sigue siendo "sin contraseña". `" "` sigue siendo válida (el QA la verificó con 7-Zip).
- El mensaje **nunca** incluye la contraseña.
- `EvidenceZip.cs` **no se toca** (D-A5).
- El backend siempre manda `null` o 16 caracteres (`ReportService.GeneratePassword`), así que no rompe nada.

**T-13: controles bidi y C1** (`Common/AgentFileNames.cs`, `CaseEvidenceStore.cs`):
- Nuevo `AgentFileNames.HasUnsafeControls(string name)`, que da verdadero si algún `char` está en `U+0080` a `U+009F`
  (C1), es `U+061C`, `U+200E` o `U+200F`, o está en `U+202A` a `U+202E` o `U+2066` a `U+2069`.
- ZWJ (`U+200D`) y NBSP (`U+00A0`) **no** entran: los usan los emoji y nombres legítimos.
- Nuevo `AgentFileNames.IsValidNewName(name) => IsValidName(name) && !HasUnsafeControls(name)`.
- **Solo en la entrada:** `CaseEvidenceStore.CheckUpload`, `StageAndCommitUploadAsync` e `ImportAsync` usan
  `RequireNewName`, que tira el mismo 400 `invalid_filename` con el texto: "Nombre de archivo inválido: no puede tener
  rutas, caracteres de control ni los caracteres < > : \" | ? *".
- `IsValidName` **no cambia**: lo siguen usando GET, DELETE, el ZIP y el commit, así un archivo con esos caracteres
  que ya esté en la carpeta de un caso sigue pudiendo bajarse, borrarse o zipearse (D-A4).

**T-14: detener una grabación inexistente** (`AdbService`, `IosService`, `RecordingController`):
- Nueva excepción `RecordingNotActiveException : InvalidOperationException`, en `Services/` (o junto a
  `AgentErrorCodes`), con el mensaje "No hay una grabación en curso.".
- `AdbService.StopRecordingAsync` (líneas 278 y 286) e `IosService.StopRecordingAsync` (línea 778) la tiran en vez de
  la `InvalidOperationException` actual. Se unifica el texto: iOS hoy dice "Sin grabación iOS activa".
- `RecordingController.Stop` agrega `catch (RecordingNotActiveException ex)` **antes** de los genéricos:
  `return Conflict(new { error = ex.Message, code = AgentErrorCodes.NotRecording });` → **409**.
- El resto de los 500 de dispositivo no cambia (fuera de alcance).

**`AgentErrorCodes`:** se agregan `InvalidPassword = "invalid_password"` y `NotRecording = "not_recording"`.

---

## 5. Endpoints y mensajes WebSocket (cambios observables)

No hay rutas nuevas, y los payloads de los eventos WS no cambian.

| Ruta | Antes | Después |
|---|---|---|
| **Cualquier ruta**, incluidas `/ws`, `/health` y `OPTIONS`, con un `Host` que no está en la lista de `HostPolicy` | 200 con datos | **400** "Bad Request - Invalid Hostname", sin datos ni CORS |
| `/ws`: cliente que no lee | Trababa todo | Se le hace `Abort()` cuando la cola (256) se llena o un envío pasa 10 s. Los demás clientes siguen recibiendo. |
| `/ws`: apagado de Tatana | Sin close durante 30 s | Close **1001** "going away", después de los eventos que ya estaban encolados |
| `POST /cases/{id}/zip`: archivo borrado a mitad del armado | 500 `zip_failed` | **409** `evidence_changed`, `files: [{ filename, reason: "missing" }]` |
| `POST /cases/{id}/zip`: disco lleno a mitad del armado | 500 `zip_failed` | **507** `insufficient_storage`, `required_bytes`, `available_bytes` |
| `POST /cases/{id}/zip` con `password: ""` | 500 `zip_failed` | **400** `invalid_password` |
| `POST /cases/{id}/zip` con contraseña de más de 99 bytes UTF-8 | 200 (ZIP que 7-Zip no abre) | **400** `invalid_password` |
| `POST /cases/{id}/zip`, cualquier falla antes de escribir | Dejaba carpetas vacías | No crea carpetas |
| `PUT/POST .../evidence/upload`, `upload-check` e `import` con nombre con bidi o C1 | 200 | **400** `invalid_filename` |
| `POST /devices/{s}/record/stop` sin grabación (Android o iOS) | 500 `{error}` | **409** `{ error: "No hay una grabación en curso.", code: "not_recording" }` |
| `GET /agent/state` → `local_config` | `loaded: true`, después crash al arrancar | `loaded: false`, `error: "Agent:<Clave> …"` |

## 6. Contrato compartido

El JSON de Tatana va en **snake_case_lower**: `JsonNamingPolicy.SnakeCaseLower` está en
`server/src/Factum.Agent/Program.cs:100` y en `AgentWebSocketHub.JsonOpts`. Los cuerpos de error se arman con
claves literales (`error`, `code`, `files`, `required_bytes`, `available_bytes`).

| Campo / valor (JSON tal cual viaja) | Tipo | Lado Tatana (C#) | Lado consumidor (TS) | ¿Se toca el consumidor? |
|---|---|---|---|---|
| `code: "invalid_password"` (400) | string | `server/src/Factum.Agent/Common/AgentErrorCodes.cs` | `client/src/lib/agent.ts:223-233` (`AgentErrorCode`) | **No** (D6). Sin `case` en `agent-messages.ts`, cae al genérico "Tatana respondió con un error: <error>". El backend nunca dispara este caso. |
| `code: "not_recording"` (409) | string | `AgentErrorCodes.cs`, `Controllers/RecordingController.cs` | `client/src/lib/agent.ts:476-489` (`stopRecording` → `readAgentError`) | **No** (D6). Muestra el `error` del cuerpo. |
| `code: "evidence_changed"` + `files[].filename`, `files[].reason = "missing"` (409) | string / `{filename: string, reason: "missing"\|"size"\|"hash"}[]` | `Services/CaseZipService.cs` (`EvidenceChange`) | `client/src/lib/agent-messages.ts:96-99` | **No**: ya está mapeado y usa `files`. |
| `code: "insufficient_storage"` + `required_bytes`, `available_bytes` (507) | string / number / number | `Services/CaseZipService.cs` | `client/src/lib/agent-messages.ts:81-87` | **No**: ya está mapeado. |
| `local_config.error` en `GET /agent/state` | string \| null | `Common/LocalConfigInspector.cs` → `Controllers/AgentStateController.cs:40,118` (`LocalConfigDto`) | `agent-ui/src/main/app-info.ts:77-86`, `agent-ui/src/renderer/src/components/UpdateStatus.tsx:268-272` | **No**: solo cambia el texto, que va **sin punto final** y nombra la clave. |
| `local_config.loaded` | bool | ídem | ídem | No: ahora es `false` también con valores inválidos. |
| Eventos WS (`type`, `timestamp`, `data`) y `{"type":"ping"}` | — | `WebSockets/AgentWebSocketHub.cs` | `client/src/lib/agent.ts:716`, `client/src/hooks/useAgentConnection.ts` | No: no cambia ningún payload. |
| Close WS 1001 / abort por lento | — | `AgentWebSocketHub.cs` | `useAgentConnection.ts:55-56` (`onerror`/`onclose` → reintento en 3 s) | No. |
| `Agent:AllowedHosts` (config, no JSON de red) | `string[]` | `Models/AgentModels.cs`, `Common/HostPolicy.cs` | `README.md` (tabla de config de Tatana) | Solo docs. |

**Nota para una HU futura (no se hace ahora):** sumar `"invalid_password" | "not_recording"` a `AgentErrorCode` en
`client/src/lib/agent.ts` y, si se quiere, mensajes propios en `agent-messages.ts`.

---

## 7. Decisiones técnicas

| # | Decisión | ¿Necesita al usuario? |
|---|---|---|
| D1 | Cola acotada por cliente (`Channel` bounded, `TryWrite`), un enviador por cliente y timeout por envío (HU D1 = A). | No (validada) |
| D2 | Solo filtro de `Host` con `HostFilteringMiddleware`, sin header propio (HU D2 = A). | No (validada) |
| D3 | Hosts = locales fijos + IP de bind + IP de la PC si bind es `Any`, + `Agent:AllowedHosts` (HU D3 = B). | No (validada) |
| D4 | La config local con valores o tipos inválidos se ignora entera (HU D4 = A). El `AllowedHosts` de la raíz se ignora siempre. | No (validada) |
| D5 | Entran T-05, T-06, T-07, T-08, T-09, T-13 y T-14 (HU D5 = A). | No (validada) |
| D6 | Sin cambios en `client/` ni en `agent-ui/` (HU D6 = A). | No (validada) |
| **D-A1** | **Decisión del arquitecto:** cola de 256, timeout de envío de 10 s, ping cada 25 s por la cola y espera de 1 s al cerrar, como constantes internas **sin clave de config**. Más config local es más superficie para T-04. | No |
| **D-A2** | **Decisión del arquitecto:** un cliente lento se descarta con `Abort()`, no con close 1008. Con el buffer lleno el close frame no puede salir. El front reconecta igual. | No |
| **D-A3** | **Decisión del arquitecto:** con bind `0.0.0.0`/`::`, las IP de la PC se toman **al arrancar**. Si cambian (DHCP), hay que reiniciar o fijarlas en `Agent:AllowedHosts`. No se acepta el nombre de la máquina por defecto (conservador: solo lo que la HU pide). | No |
| **D-A4** | **Decisión del arquitecto:** el rechazo de bidi y C1 (T-13) aplica **solo a la entrada** (upload-check, upload, import). GET, DELETE y ZIP siguen aceptando nombres que ya estén en disco, así ningún caso existente queda trabado. La copia del backend (`EvidenceUpload.IsValidUploadName`) no se toca en esta HU (fuera de alcance: no hay cambios en `Factum.Backend`). | No |
| **D-A5** | **Decisión del arquitecto:** la validación de la contraseña va en `CaseZipService`, no en `EvidenceZip`, para no desincronizar la copia del backend ni pisar a `zip-verificacion-stored`. | No |
| **D-A6** | **Decisión del arquitecto:** se borra `"AllowedHosts": "*"` de `appsettings.json` y Tatana fuerza su lista con `PostConfigure<HostFilteringOptions>`. Ninguna fuente (appsettings, local, env) puede reabrir el filtro con `"*"`. | No |
| **D-A7** | **Decisión del arquitecto:** T-07 mapea a 409 solo el archivo **faltante** a mitad del armado. Un archivo **alterado** a mitad del armado sigue en 500 `zip_failed` con el mensaje de verificación, como lo dejó el QA en "lo que pasó bien". | No |
| **D-A8** | **Decisión del arquitecto:** T-05 borra solo `evidencia_*.zip.part` dentro de `<EvidenceDirectory>/*/.factum/pendiente/`. No borra carpetas vacías viejas de T-06: son de la PC del perito y no se tocan. | No |

No queda ninguna decisión pendiente del usuario.

---

## 8. Checklist atómico

### 8.1 `implementer-backend` (`server/src/Factum.Agent`, tests y `README.md`)

**Hub WS (T-01)**
- [ ] `WebSockets/AgentWebSocketHub.cs`: agregar las constantes (`QueueCapacity`, `SendTimeout`, `PingInterval`,
      `CloseTimeout`) y un constructor `internal` con esos valores. El público delega con los defaults.
- [ ] Agregar la clase interna `Client` y el tipo `Outgoing` (payload o close).
- [ ] Reescribir `BroadcastAsync`: serializar una vez, `TryWrite` por cliente, `Drop` si la cola está llena o el
      socket no está `Open`, devolver `Task.CompletedTask` y no propagar errores de serialización.
- [ ] Documentar en `IAgentEventSink.BroadcastAsync` que la `Task` completa al encolar, no al entregar.
- [ ] Implementar `SendLoopAsync` con timeout por envío y close por la cola.
- [ ] Implementar `Drop(client, motivo)`: idempotente, saca de `_clients`, completa la cola, cancela, `Abort()` y log
      warning (`Debug` si el motivo es `cerrado`).
- [ ] Reescribir `HandleAsync`: token linkeado con `_stopping`, ping por la cola, echo del close del cliente y
      `finally` con `WaitAsync(CloseTimeout)`.
- [ ] Implementar `ShutdownAsync(TimeSpan)` (§4.1) y actualizar `Dispose()`.
- [ ] Verificar con grep que fuera del `Sender` no queda ningún `SendAsync` ni `CloseOutputAsync` sobre los sockets
      del hub.

**Apagado (T-02)**
- [ ] `Program.cs`: un solo `ApplicationStopping.Register` con post-proceso y después `hub.ShutdownAsync(1 s)`
      (§4.2).
- [ ] `Program.cs`: polling con `stopping` (`while`, `Task.Delay`, `ListDevicesAsync`) y `catch` de cancelación
      silencioso.

**Filtro de `Host` (T-03)**
- [ ] `Models/AgentModels.cs`: `public string[] AllowedHosts { get; set; } = [];` con su XML doc.
- [ ] `Common/HostPolicy.cs` nuevo: `LocalHosts`, `TryNormalize`, `Build` y `MachineAddresses()` (§4.3).
- [ ] `Program.cs`: `HostPolicy.Build` con un error claro si es inválido, `PostConfigure<HostFilteringOptions>`,
      `app.UseHostFiltering()` como primer middleware y el log "Hosts permitidos".
- [ ] `appsettings.json`: borrar `"AllowedHosts": "*"`.
- [ ] `README.md`: fila `Agent:AllowedHosts` en la tabla de `server/src/Factum.Agent/appsettings.json`, y en
      `Agent:BindAddress` mencionar que Tatana solo atiende requests dirigidas a esta PC (400 si no) y lo de D-A3.

**Config local (T-04)**
- [ ] `Common/LocalConfigValidator.cs` nuevo (§4.4): validación de tipos por reflexión y validación semántica.
- [ ] `Common/LocalConfigInspector.cs`: `InspectText` arma la config aislada con `AddJsonStream`, captura
      `FormatException`/`InvalidDataException` y llama al validador. Actualizar el XML doc de `LocalConfigStatus.Error`.
- [ ] Confirmar que `Program.cs:63-81` no necesita cambios: ya saltea el archivo si `Loaded` es `false`.

**Bajas**
- [ ] T-05: `AgentFileNames.IsZipPartFilename`, `ICaseZipService.CleanupOrphanParts()` y su implementación, y la
      llamada con log en `Program.cs`.
- [ ] T-06: helper `ExistingAncestor`, prechequeo de espacio sobre él, `CreateDirectory` movido al `try` de los pasos
      5 a 7 y borrado de las carpetas creadas por la llamada si quedan vacías.
- [ ] T-07: `Common/DiskErrors.cs` (`IsDiskFull`), `CaseEvidenceStore.IsDiskFull` delegando,
      `CaseZipService.ClassifyBuildFailure` (409 missing / 507 / 500) llamado **antes** de borrar el `.part`.
- [ ] T-08 y T-09: validación de `Password` al principio de `BuildAsync` y `AgentErrorCodes.InvalidPassword`.
- [ ] T-13: `AgentFileNames.HasUnsafeControls` e `IsValidNewName`, y `CaseEvidenceStore.RequireNewName` en
      `CheckUpload`, `StageAndCommitUploadAsync` e `ImportAsync`.
- [ ] T-14: `RecordingNotActiveException`, uso en `AdbService` (líneas 278 y 286) e `IosService` (línea 778), `catch`
      → 409 en `RecordingController.Stop` y `AgentErrorCodes.NotRecording`.

**Tests (`server/tests/Factum.Agent.Tests`)**
- [ ] `AgentWebSocketHubTests.cs`, con un `FakeWebSocket : WebSocket` que puede tener `SendAsync` trabado
      (`TaskCompletionSource` que nunca completa), registrar lo enviado y devolver `Close` en `ReceiveAsync` cuando se
      le pide. Casos:
  - [ ] `BroadcastAsync` vuelve en menos de 50 ms aunque un cliente tenga el `SendAsync` trabado.
  - [ ] Con la cola chica (por ejemplo 4), el cliente trabado se descarta al llenarse (`Abort` llamado, fuera de la
        lista) y otro cliente sano recibe **todos** los eventos **en orden**.
  - [ ] Con `SendTimeout` chico (por ejemplo 100 ms), un envío que no completa descarta al cliente.
  - [ ] `ShutdownAsync` manda close 1001 después de los eventos encolados, vuelve en `CloseTimeout` o menos aunque un
        cliente esté trabado, y `HandleAsync` termina.
  - [ ] Nunca hay dos `SendAsync`/`CloseOutputAsync` concurrentes sobre el mismo socket (el fake cuenta los envíos en
        vuelo y falla si pasa de 1).
- [ ] `HostPolicyTests.cs`:
  - [ ] Los locales siempre están.
  - [ ] La IP de bind IPv4 e IPv6 queda con corchetes.
  - [ ] `0.0.0.0` suma las IPv4 del `Func` y `::` suma IPv4 e IPv6.
  - [ ] Los extras válidos se normalizan.
  - [ ] Son inválidos `*`, `*.x.com`, `http://x`, `x:8765`, `x/y` y `""`, con sus mensajes.
  - [ ] Una prueba del middleware: `HostFilteringMiddleware` con `Options.Create(new HostFilteringOptions {
        AllowedHosts = lista })` y un `DefaultHttpContext`. `Host: rebind.attacker.example:8765` da 400;
        `localhost:8765`, `127.0.0.1:8765` y `[::1]:8765` pasan.
- [ ] `LocalConfigValidatorTests.cs` y `LocalConfigInspectorTests.cs`, con los seis ejemplos del Gherkin. Cada uno da
      `Loaded = false`, `Error` que contiene la clave y `OverridesAllowedOrigins = false`:
  - [ ] `{"Agent":{"MaxUploadBytes":"16GB"}}`
  - [ ] `{"Agent":{"MaxUploadBytes":0}}`
  - [ ] `{"Agent":{"MinFreeBytes":-1}}`
  - [ ] `{"Agent":{"AllowedOrigins":["http://localhost:3000/app"]}}`
  - [ ] `{"Agent":{"AllowedOrigins":["*"]}}`
  - [ ] `{"Agent":{"Port":"ocho"}}`
  - [ ] Más: `{"Agent":{"AllowedHosts":["*"]}}`, `{"Agent":{"VideoPostprocess":{"TimeoutFactor":"x"}}}`, clave
        duplicada y `{"AllowedHosts":"*"}` en la raíz (este último **válido**, se ignora).
  - [ ] Los tests existentes siguen verdes sin cambios, y `InspeccionarNoModificaElArchivo` también con un archivo
        inválido por valor.
- [ ] `CaseZipServiceTests.cs`:
  - [ ] `password: ""` → 400 `invalid_password` y no se crea ninguna carpeta.
  - [ ] 100 bytes UTF-8 (por ejemplo 50 `ñ`) → 400; 99 bytes ASCII → OK.
  - [ ] Un ZIP que falla por `evidence_changed` en el paso 4 o por 507 en el prechequeo (probe falso) no deja ninguna
        carpeta en `EvidenceDirectory`.
  - [ ] `ClassifyBuildFailure`: `FileNotFoundException` con un path borrado → 409 con `files[].reason == "missing"`;
        `IOException` con `HResult = 28` → 507 con `required_bytes`/`available_bytes`; otra excepción → 500
        `zip_failed`.
  - [ ] `CleanupOrphanParts` borra `evidencia_X.zip.part` y deja `evidencia_X.zip`, `evidencia_X.zip.sha256`,
        `otro.part` y las carpetas.
- [ ] `AgentFileNamesTests.cs`: `IsValidNewName` rechaza U+202E, U+0085, U+2066 y U+061C, y acepta ZWJ en un emoji,
      NBSP, `ñ` y CJK. `IsValidName` sigue aceptando U+202E (D-A4).

**Cierre**
- [ ] Escribir `progress/impl_backend_tatana-ws-y-hosts.md`: archivos tocados, salida de los comandos de §10.1 y
      resultado de cada repro de §10.2 con números (tiempos y códigos HTTP).

### 8.2 `implementer-frontend`

- No aplica: esta HU no toca `client/` ni `agent-ui/` (D6). No se lanza.

---

## 9. Regla de datos (AGENTS.md), obligatoria para el implementador

- **No tocar** la instancia real de Tatana en `:8765`, `~/Factum/Evidencia`,
  `server/src/Factum.Agent/agent-data/`, su `bin/` ni `server/src/Factum.Agent/appsettings.Local.json`.
- Todas las pruebas manuales corren contra una **instancia propia**:
  - un build en el scratchpad: `dotnet build server/src/Factum.Agent/Factum.Agent.csproj -o <scratch>/agentbin`, para
    no recompilar el `bin/` de la instancia real;
  - `--mock --port 8766 --data <scratch>/tatana-qa-data/data --local-config <scratch>/qa/<archivo>.json`;
  - `Agent__EvidenceDirectory=<scratch>/tatana-qa-data/evidencia`.
- **T-05 borra `.part` en `EvidenceDirectory` al arrancar:** por eso es obligatorio que la instancia de prueba tenga
  **su propio** `EvidenceDirectory` y nunca el default.
- Los tests xUnit usan solo carpetas temporales propias (`Path.GetTempPath()` + Guid) y las borran en `Dispose`.
- Al terminar: matar la instancia de prueba, desmontar volúmenes `hdiutil` y borrar los datos de prueba del
  scratchpad. Dejar constancia en el progress.
- No se toca MongoDB.

---

## 10. Verificación

### 10.1 Comandos (antes de declararse `done`)

```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
./ops/harness/verify.sh
```

Los tres tienen que pasar sin errores ni warnings nuevos de nullable en los archivos tocados.

### 10.2 Reproducciones del QA (instancia propia en `:8766`, ver §9)

Los scripts del QA están en el scratchpad de esa sesión:
`/private/tmp/claude-501/-Users-joelmiguelserrudo-Projects-Factum/7b8e9155-f8ea-4033-8a4e-349a2f9fa9fa/scratchpad/qa/tatana-scripts/`
(`start.sh`, `wslib.py`, `ws_stall.py`, `ws_stall_zip3.py`, `shutdown_time.py`, `zipcase.py`). **Si ya no existen**,
se rearman con estas recetas. `wslib.connect(origin, rcvbuf, port)` es un handshake WS crudo por socket a
`127.0.0.1:<port>` con `Host: localhost:<port>`, y `read_frame` lee un frame.

1. **Cliente WS que no lee (T-01).**
   - Pasos:
     - Abrir un WS con `SO_RCVBUF=1024` y no leerlo nunca.
     - Abrir otro WS con `Origin: http://localhost:3000` que sí lee y cuenta los eventos `recording_*`.
     - Hacer 3.000 ciclos de `POST /devices/EMU001/record/start` (`{"android_version":13}`) y
       `POST /devices/EMU001/record/stop` (mock), con timeout de 15 s cada uno.
     - Después, `POST /cases/{id}/zip` sobre un caso de prueba propio, `POST /devices/EMU001/screenshot`,
       `GET /agent/state` y `POST /agent/maintenance` (sin `Origin`), seguido de `DELETE /agent/maintenance`.
   - Esperado:
     - **ninguna** request colgada (cada una en menos de 2 s);
     - el cliente lector recibe exactamente los `recording_*` emitidos y en orden;
     - el log tiene `WS … desconectado: cola llena` o `timeout de envío`;
     - `busy:false` al terminar;
     - maintenance da 200, no 409 `agent_busy`.
   - Antes del fix: stop colgado más de 300 s y maintenance 409.
2. **Apagado (T-02).**
   - Pasos: abrir 1 WS (y, en otra corrida, 3), mandar `kill -TERM <pid>` y medir hasta que el proceso termina.
   - Esperado:
     - **menos de 2 s**;
     - el cliente recibe un frame close (opcode 8) con código 1001;
     - el log no tiene "Error en polling de dispositivos" después del SIGTERM.
   - Sin clientes sigue en unos 0,1 s. Antes del fix: 30,1 s.
3. **Host falso (T-03).**
   - Con `Host: rebind.attacker.example:8766` y sin `Origin`, cada una de estas responde **400** sin datos:
     - `curl -s -o /dev/null -w '%{http_code}' -H 'Host: rebind.attacker.example:8766' http://127.0.0.1:8766/files`
     - lo mismo con `/devices`, `/devices/EMU001/explorer`, `/health`, `/cases/<id>/files`,
       `/cases/<id>/zip/file?case_ref=…&zip_filename=…`, y con `/ws` (handshake con ese `Host`).
   - Responden **200**: `/health` con `Host` `localhost:8766`, `127.0.0.1:8766` y `[::1]:8766`.
   - Con `--bind 127.0.0.1` sigue andando `http://127.0.0.1:8766/health`.
   - Con `Agent__AllowedHosts__0=tatana.local` y `Host: tatana.local:8766`, responde 200.
   - Con `Agent__AllowedHosts__0=*`, **no arranca** y muestra el mensaje claro.
   - El front local (`npm run dev` en `client/`, con `NEXT_PUBLIC_AGENT_URL=http://localhost:8766`) sigue viendo los
     dispositivos mock y el WS.
4. **Config local inválida (T-04).** Para cada uno de los seis JSON del Gherkin escritos en
   `<scratch>/qa/local-bad.json` y pasados con `--local-config`:
   - Tatana **arranca**.
   - `GET /agent/state` da `local_config.loaded == false` y un `local_config.error` que contiene la clave
     (`Agent:MaxUploadBytes`, `Agent:MinFreeBytes`, `Agent:AllowedOrigins` o `Agent:Port`).
   - El log tiene "La configuración local … tiene un error y se ignoró".
   - Control: con `Agent__MaxUploadBytes=0` como variable de entorno, **no arranca**, igual que hoy.
5. **Bajas.**
   - T-05:
     - Pasos: crear a mano `<scratch>/tatana-qa-data/evidencia/X_11111111/.factum/pendiente/evidencia_X.zip.part`,
       un `.zip` y un `.sha256` hermanos, y arrancar.
     - Esperado: solo se borra el `.part` y el log dice "ZIP parciales huérfanos borrados: 1".
   - T-06: pedir un ZIP con un archivo inexistente y otro con `case_ref` `../../../etc`. Esperado: 409 o 400 y
     `ls <scratch>/…/evidencia` sin carpetas nuevas.
   - T-07, disco lleno (macOS):
     - Pasos: crear con `hdiutil create -size 12m -fs HFS+ -volname qa12 <scratch>/qa12.dmg` y `hdiutil attach` un
       volumen. Arrancar con `Agent__EvidenceDirectory=/Volumes/qa12/evidencia` y `Agent__MinFreeBytes=0`. Armar un
       ZIP con un total apenas por debajo del espacio libre, como hizo el QA.
     - Esperado: **507** `insufficient_storage` con `required_bytes`/`available_bytes`, sin `.part` ni carpetas
       nuevas.
   - T-07, archivo que falta: un caso con un archivo de unos 500 MB primero y uno chico después. Durante la fase
     `zipping` (visible por el `zip_progress` del WS), borrar el chico. Esperado: **409** `evidence_changed` con
     `reason: "missing"`.
   - T-08 y T-09: `zipcase.py` con `"password": ""` da 400 `invalid_password`. Con 100 caracteres ASCII da 400. Con
     99 da 200, y `7zz t -p<pw>` responde "Everything is Ok".
   - T-13: `upload-check` y `upload` con `filename` `‮gpj.exe` (U+202E) y con U+0085 dan 400 `invalid_filename`. Con
     emoji + ZWJ da 200.
   - T-14: `POST /devices/mock-ios-udid-001/record/stop?platform=ios` dos veces, y lo mismo con Android `EMU001`. La
     segunda vez responde **409** `{"error":"No hay una grabación en curso.","code":"not_recording"}`.

### 10.3 Prueba manual para el usuario (después del review)

1. Con el stack de siempre (front en `http://localhost:3000` o la nube, y Tatana en `:8765` con la versión nueva):
   - entrar a un caso, ver los dispositivos, grabar y detener, sacar una captura, armar el ZIP y descargarlo;
   - todo tiene que andar igual que antes.
2. Cerrar y volver a abrir Tatana desde la ventana (o "Reiniciar y actualizar") con la web abierta:
   - tiene que tardar segundos, no medio minuto;
   - la web tiene que reconectar sola.
3. Opcional: poner en `appsettings.Local.json` `{"Agent":{"MaxUploadBytes":"16GB"}}` y reiniciar Tatana:
   - Tatana arranca;
   - la ventana muestra "Configuración local con error … Agent:MaxUploadBytes no es un número: '16GB'";
   - después hay que restaurar el archivo.

---

## 11. Riesgos y notas para el implementador

- **`HostFilteringMiddleware` y `[::1]`:** las entradas IPv6 van **con corchetes**. Los puertos no se ponen en la
  lista porque el middleware los ignora. Verificarlo con la prueba del middleware de §8.1, no darlo por supuesto.
- **`DriveInfo` sobre una ruta inexistente** en macOS tira y el probe devuelve `null`, que es fail-open. Por eso
  existe `ExistingAncestor` (§4.5, T-06). Sin él, el prechequeo de espacio quedaría muerto en el primer ZIP de cada
  caso.
- **Medición de espacio en T-07:** hay que medir **antes** de borrar el `.part`. Si se mide después, ya se liberó el
  espacio y el 507 no se dispara.
- **`IosService` y `CaseZipService` reciben `AgentWebSocketHub` concreto**, no `IAgentEventSink`. No hace falta
  cambiarlo: los métodos públicos se mantienen.
- **El `Dispose` del `WebSocket` en `WebSocketController`** (`using var ws`) puede correr al mismo tiempo que un
  `Abort()` del hub. Las dos operaciones son seguras de repetir, pero el `Sender` tiene que tolerar
  `ObjectDisposedException` (ya está en §4.1).
- **Concurrencia de `TryWrite` contra `TryComplete`:** después de `TryComplete`, `TryWrite` devuelve `false`.
  `BroadcastAsync` tiene que distinguir ese caso para no loguear "cola llena" de un cliente que ya se iba: alcanza
  con chequear `client.Dropped` o `_closing` antes de loguear.

## 12. Concurrencia con otras HU

- **`zip-verificacion-stored`** (rama actual, en curso) toca el `catch` de los pasos 5 a 7 de `CaseZipService.BuildAsync`
  (mensaje y log de `zip_failed`) y `EvidenceZip.cs`. Esta HU toca el mismo `catch` (T-06 y T-07). Lo que se mergee
  segundo resuelve el conflicto conservando las dos cosas: la clasificación 409/507 de esta HU **antes** del borrado,
  y el mensaje y log de `zip_failed` de la otra para el resto de los casos. Esta HU no toca `EvidenceZip.cs`.
- **`grabacion-android-robusta`** (HU afinada) toca `AdbService.StopRecordingAsync` y `RecordingController.Stop`. El
  cambio de esta HU ahí es mínimo: el tipo de excepción y un `catch` → 409. El segundo PR rebasa y conserva
  `not_recording`.
- `feat/tatana-ws-y-hosts` sale de `develop` actualizado, como siempre.

## 13. Fuera de alcance (recordatorio)

- T-10, T-11 y T-12, y las informativas T-15 a T-18.
- Header propio en los GET (D2).
- Autenticación local (T-17).
- Cambios en `Factum.Backend`, `client/` y `agent-ui/`.
- Validar `Logging`, `Kestrel` u otras secciones de la raíz de la config local: solo se valida `Agent`, más el
  `AllowedHosts` de la raíz que se ignora.
- El overhead de ZIP/AES en el prechequeo de espacio. `MinFreeBytes`, con 1 GiB por defecto, lo cubre.
