# Samsung SM-M127F (Android 11, serie ADB R58RA3R5AGA) — salidas REALES

Capturadas con `subprocess.run([adb, "-s", serial, "shell", SCRIPT])` (el mismo vector de argumentos que usa
Tatana), solo lectura, el 2026-10-14 (metadatos-dispositivo §3.1). Copiadas byte a byte. Re-confirmadas por
implementer-backend el mismo día con los scripts de `AndroidDetailsReader` (salida idéntica salvo la hora).

- `props.txt`: salida de `PropsScript`.
- `settings.txt`: salida de `SettingsScript`.
- `wifi.txt`: salida de `WifiScript` (rama `cmd wifi status`), equipo sin wifi conectada.
- `wifi-dumpsys.txt`: rama `dumpsys` forzada a mano.
- `date-samples.json`: 3 muestras de `date +%s.%N`; `pc_*` en segundos Unix con µs (reloj de la PC).
