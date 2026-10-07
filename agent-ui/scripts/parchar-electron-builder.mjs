// Parche de la plantilla NSIS de electron-builder 24.x (corre en `postinstall`, también con `npm ci`).
//
// Problema: en `.onInit`, `multiUser.nsh` (macro setInstallModePerUser) lee el resultado de
// SHGetKnownFolderPath con `System::Call '*$2(&w${NSIS_MAX_STRLEN} .s)'`. Eso copia siempre
// NSIS_MAX_STRLEN WCHAR desde un buffer que solo mide lo que mide la ruta. Según cómo quede el heap,
// lee memoria no mapeada, y el instalador se cae con 0xC0000005 en System.dll antes de extraer
// nada. Es intermitente.
// Arreglo de upstream: electron-builder PR #9769 (commit a356198e), incluido desde 26.12.0:
// https://github.com/electron-userland/electron-builder/pull/9769
// Este script corrige node_modules sin cambiar el comportamiento (ver NUEVO). Es idempotente: si ya está parchado, no
// hace nada. Si no encuentra ni el texto viejo ni el nuevo, falla, porque cambió la versión de
// electron-builder y hay que revisar. Cuando se actualice electron-builder a >= 26.12.0, borrar
// este script y el `postinstall`.
import { readFileSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, join } from 'node:path'

const require = createRequire(import.meta.url)
const ebDir = dirname(require.resolve('electron-builder/package.json'))
const ablDir = dirname(require.resolve('app-builder-lib/package.json', { paths: [ebDir] }))
const archivo = join(ablDir, 'templates', 'nsis', 'multiUser.nsh')

const MARCA = '# Parche Factum: electron-builder PR #9769'

const VIEJO = [
  '      System::Store S',
  '      # Win7 has a per-user programfiles known folder and this can be a non-default location',
  `      System::Call 'SHELL32::SHGetKnownFolderPath(g "\${FOLDERID_UserProgramFiles}", i \${KF_FLAG_CREATE}, p 0, *p .r2)i.r1'`,
  '      ${If} $1 == 0',
  "        System::Call '*$2(&w${NSIS_MAX_STRLEN} .s)'",
  '        StrCpy $0 $1',
  "        System::Call 'OLE32::CoTaskMemFree(p r2)'",
  '      ${endif}',
  '      System::Store L',
].join('\n')

// El bloque original nunca cambiaba el resultado: `System::Store L` restauraba $0 a
// "$LocalAppData\Programs" (upstream lo señala en el PR #9769). El parche saca la llamada y deja
// ese valor, así INSTDIR queda en %LOCALAPPDATA%\Programs\Tatana, la carpeta que asume
// migrar-portable.ps1. Upstream, en cambio, sí empieza a usar FOLDERID_UserProgramFiles: tenerlo en
// cuenta al actualizar.
const NUEVO = [
  `      ${MARCA}: sin SHGetKnownFolderPath (ver agent-ui/scripts/parchar-electron-builder.mjs).`,
  '      # El bloque original leía de más (0xC0000005 en System.dll) y su resultado se descartaba',
  '      # con System::Store L, así que $0 queda en "$LocalAppData\\Programs" como antes.',
].join('\n')

const original = readFileSync(archivo, 'utf8')
const crlf = original.includes('\r\n')
const texto = crlf ? original.replace(/\r\n/g, '\n') : original

if (texto.includes(MARCA)) {
  console.log(`parchar-electron-builder: ${archivo} ya está parchado.`)
} else if (texto.split(VIEJO).length === 2) {
  const parchado = texto.replace(VIEJO, NUEVO)
  writeFileSync(archivo, crlf ? parchado.replace(/\n/g, '\r\n') : parchado)
  console.log(`parchar-electron-builder: parchado ${archivo} (PR #9769).`)
} else {
  console.error(
    `parchar-electron-builder: no se encontró el bloque esperado en ${archivo}.\n` +
      'Cambió la versión de electron-builder: si es >= 26.12.0 ya trae el arreglo (PR #9769) y ' +
      'hay que borrar este script y el postinstall; si no, revisar el parche.',
  )
  process.exit(1)
}
