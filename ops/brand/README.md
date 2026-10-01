# ops/brand — assets de marca de Factum (logo "Sello")

Script reproducible que genera todos los assets del logo "Sello" a partir de
la geometría fija (marca de 64×64: cuadrado con `rx=14` + "F" maciza con la
esquina cortada) y del wordmark "Factum" en Sora Bold convertido a trazos.

## Cómo correrlo

```bash
cd ops/brand
npm ci
node build-brand.mjs
```

Imprime, por cada archivo, los primeros 16 hex del SHA-256, el tamaño y la
ruta. La salida es **determinista**: dos corridas seguidas dan los mismos
bytes (después de la segunda, `git status` queda limpio). No necesita nada
instalado en el sistema (ni `rsvg-convert`, ni `iconutil`, ni ImageMagick):
el texto pasa a trazos con `opentype.js`, el rasterizado lo hace
`@resvg/resvg-js` y los `.ico`/`.icns` se arman a mano con PNG embebidos.

## Qué genera

| Archivo | Contenido |
|---|---|
| `client/public/logo-theme-dark.svg` / `logo-theme-white.svg` | Logo horizontal (marca + "Factum" en trazos), variantes oscura y clara |
| `client/public/logo-mark-dark.svg` / `logo-mark-white.svg` | Solo la marca |
| `client/public/icon.svg` | Marca canónica de app (favicon SVG) |
| `client/public/logo-app.ico` | ICO 16/32/48 |
| `client/public/icon-512.png` | PNG 512×512, esquinas transparentes |
| `client/public/apple-icon.png` | PNG 180×180 a sangre completa (sin `rx`: iOS redondea solo) |
| `agent-ui/resources/icon.png` | PNG 1024×1024 (Linux) |
| `agent-ui/resources/icon.ico` | ICO 16/24/32/48/64/128/256 (Windows) |
| `agent-ui/resources/icon.icns` | ICNS (`icp4`…`ic14`), marca a 824/1024 con margen transparente (macOS) |
| `agent-ui/resources/tray/trayTemplate.png` + `@2x` | Template monocromo de macOS (silueta negra con la "F" calada), 16 y 32 px |
| `agent-ui/resources/tray/tray.png` + `@2x` | Bandeja a color, 32 y 64 px (Linux) |
| `agent-ui/resources/tray/tray.ico` | Bandeja a color, ICO 16/24/32/48 (Windows) |
| `server/src/Factum.Backend/Templates/factum-sello.png` | PNG 256×256 de la marca clara (pie "Realizado con Factum" del informe) |

Colores:

| Variante | Acento | "F" | Texto |
|---|---|---|---|
| Oscura | `#7fd34e` | `#0b0c0e` | `#f3f5f7` |
| Clara | `#2f6f12` | `#ffffff` | `#0e1013` |

Los íconos de app (favicon, `icon-512`, `apple-icon`, íconos y bandeja a
color de Tatana) usan la variante **clara**.

## Fuente

- `fonts/Sora-Bold.ttf`: TTF estático oficial del proyecto Sora.
  - Repo: <https://github.com/sora-xor/sora-font>, commit
    `7f9a9c5d0ccd1c099cfac420aa27133df1c5fdc4`, ruta `fonts/ttf/Sora-Bold.ttf`.
  - URL exacta:
    <https://raw.githubusercontent.com/sora-xor/sora-font/7f9a9c5d0ccd1c099cfac420aa27133df1c5fdc4/fonts/ttf/Sora-Bold.ttf>
  - SHA-256: `2ee62953668346b0aff83994a7864f75ee6b8443f99894e88f47559454291155`
- `fonts/OFL.txt`: licencia SIL Open Font License 1.1 del mismo commit
  (SHA-256 `ba0b9729c9428ba79a0459ab8ec575791b51509dbec213e383d0316d37fec299`).
  La OFL permite redistribuir la fuente junto con su licencia. El archivo se
  usa sin modificar, así que no aplica el renombrado por "Reserved Font Name".

La app no carga Sora: el wordmark de los SVG va convertido a trazos.
