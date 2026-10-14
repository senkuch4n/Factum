# Wifi — fixtures SINTÉTICOS

El SM-M127F no estaba conectado a una wifi: el formato "conectado" no se pudo capturar en vivo. Siguen el formato
de AOSP 11 (`WifiShellCommand`: `Wifi is connected to "<SSID>"`) y, para `__FX_DUMPSYS__`, la salida del `sed` de
`WifiScript` sobre la línea `mWifiInfo SSID: ..., BSSID: ..., Supplicant state: ...`.
`ssid-hostile.txt` trae un RLO (U+202E) y un BEL (U+0007) dentro del SSID.
