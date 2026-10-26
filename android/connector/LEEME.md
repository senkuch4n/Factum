# Factum Connector

App auxiliar de Factum (parte de Factum, **GNU GPL-3.0-or-later**) que se instala y se desinstala de
forma temporal durante una extracción lógica de Android, **solo** para exportar el registro de llamadas
(`READ_CALL_LOG`). No tiene interfaz, no aparece en el cajón de apps y no pide permiso de red
(`INTERNET`): no puede sacar datos del dispositivo por ninguna otra vía.

## Qué hace

Expone un `ContentProvider` de solo lectura (`CallLogExportProvider`, authority
`app.factum.connector.calllog`) que:

- solo responde al shell de adb (uid `2000`) o a root (`0`); cualquier app del celular recibe
  `SecurityException`;
- `content://app.factum.connector.calllog/calls` devuelve el registro de llamadas completo ordenado por
  `_ID`;
- `content://app.factum.connector.calllog/info` devuelve una fila con `package`, `version_name`,
  `version_code`, `sdk_int` y `read_call_log_granted`.

Tatana la usa con `adb shell content query`, con el mismo formato y el mismo parser que el resto de las
categorías. No escribe nada en el dispositivo fuera de la propia instalación.

## Cómo se compila

Requisitos: JDK 17 y el Android SDK (compileSdk 35, build-tools 35). Sin dependencias externas.

```bash
cd android/connector
./gradlew :app:lintRelease :app:assembleRelease \
  -PconnectorVersionName=1.0.0 -PconnectorVersionCode=1
```

Sin las variables de firma (ver abajo), `assembleRelease` produce un APK **sin firmar**
(`app/build/outputs/apk/release/app-release-unsigned.apk`), que es lo que corre en los PR.

El CI (`.github/workflows/android-connector.yml`) hace exactamente esto en cada PR que toca
`android/connector/**` y además verifica que el único permiso declarado sea `READ_CALL_LOG` y que no
haya activity launcher.

## Cómo se firma y se publica (lo hace el dueño del producto, U1)

La clave de firma la **genera y custodia el dueño del producto**, fuera del repo y con copia offline.
El implementador deja todo listo pero **no puede firmar**: hasta que exista la release firmada, el
registro de llamadas queda deshabilitado en Factum (`connector.available = false`, `reason =
"not_released"`).

1. Generar la clave una sola vez (y guardarla fuera del repo, con copia offline):

   ```bash
   keytool -genkeypair -v -keystore factum-connector.jks -alias factum-connector \
     -keyalg RSA -keysize 4096 -validity 36500
   ```

2. Obtener el SHA-256 del certificado para publicarlo como variable del repo:

   ```bash
   keytool -list -v -keystore factum-connector.jks -alias factum-connector \
     | grep -i 'SHA256:' | sed 's/.*SHA256: //; s/://g' | tr 'A-F' 'a-f'
   ```

3. Cargar en el **environment** `android-connector` de GitHub Actions los 4 secrets:
   - `ANDROID_CONNECTOR_KEYSTORE_BASE64` (`base64 -w0 factum-connector.jks`),
   - `ANDROID_CONNECTOR_KEYSTORE_PASSWORD`,
   - `ANDROID_CONNECTOR_KEY_ALIAS` (`factum-connector`),
   - `ANDROID_CONNECTOR_KEY_PASSWORD`.

   Y la **variable** pública del repo `ANDROID_CONNECTOR_CERT_SHA256` (el valor del paso 2, 64 hex).

4. Cuando el código esté en `main`, empujar el tag `connector-v1.0.0`. El workflow firma, verifica que el
   SHA-256 del certificado coincida con la variable, chequea permisos, calcula el SHA-256 del APK y
   publica la **GitHub Release** `connector-v1.0.0` con `factum-connector-1.0.0.apk`, su `.sha256` y un
   `build-info.txt`. El job summary imprime el JSON listo para pegar en `connector-lock.json`.

## Cómo se actualiza `connector-lock.json`

`connector-lock.json` es **la única** fuente del hash fijo que verifica Tatana (va embebido en el binario
de Tatana). **No** lo actualiza el workflow: lo commitea una persona en un PR chico, así el cambio de hash
pasa por revisión.

Mientras `sha256` esté vacío (`""`), el paquete de Tatana sale **sin** el APK y el registro de llamadas
queda deshabilitado. Después del primer release, pegar el JSON del job summary:

```json
{
  "package": "app.factum.connector",
  "version_name": "1.0.0",
  "version_code": 1,
  "sha256": "<64 hex del APK>",
  "cert_sha256": "<64 hex del certificado>",
  "release_tag": "connector-v1.0.0",
  "asset": "factum-connector-1.0.0.apk"
}
```

Si se pierde la clave, hay que cambiar el paquete (`applicationId`) o desinstalar en cada celular, y
actualizar `sha256`/`cert_sha256` del lock. Por eso la copia offline es importante.

## Desarrollo en la Mac

No hay paquete de Tatana para macOS. En desarrollo, un APK de debug (firmado con la clave de debug) se
puede usar con `Agent:Extraction:ConnectorApkPath` y `Agent:Extraction:AllowUnpinnedConnector=true`,
que **solo** se respeta con `ASPNETCORE_ENVIRONMENT=Development` y deja en el ACTA "app auxiliar NO
verificada (modo desarrollo)".
