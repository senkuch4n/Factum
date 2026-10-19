// Tests de linux/desktop-entry.ts (SDD tatana-linux F4, §8.4).
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { desktopEntryText, quoteExecArg } from './desktop-entry'

test('entrada del menú: Exec entre comillas con %U, sin autoarranque', () => {
  const t = desktopEntryText({ exec: '/home/perito/Applications/Tatana.AppImage', version: '1.5.0', hidden: false })
  assert.equal(t, [
    '[Desktop Entry]',
    'Type=Application',
    'Name=Tatana',
    'Comment=Agente de Factum para capturar evidencia de celulares',
    'Exec="/home/perito/Applications/Tatana.AppImage" %U',
    'Icon=factum-tatana',
    'Terminal=false',
    'Categories=Utility;',
    'StartupWMClass=Tatana',
    'X-AppImage-Version=1.5.0',
    '',
  ].join('\n'))
})

test('entrada de autoarranque: --hidden y X-GNOME-Autostart-enabled', () => {
  const t = desktopEntryText({ exec: '/home/perito/Applications/Tatana.AppImage', version: '1.5.0', hidden: true })
  assert.match(t, /^Exec="\/home\/perito\/Applications\/Tatana\.AppImage" --hidden$/m)
  assert.match(t, /^X-GNOME-Autostart-enabled=true$/m)
  assert.doesNotMatch(t, /%U/)
})

test('HOME con espacios y acentos queda entre comillas, sin escapes de más', () => {
  const t = desktopEntryText({ exec: '/home/Juan Pérez/Applications/Tatana.AppImage', version: '1.5.0', hidden: false })
  assert.match(t, /^Exec="\/home\/Juan Pérez\/Applications\/Tatana\.AppImage" %U$/m)
})

test('quoteExecArg: caracteres reservados según la especificación', () => {
  assert.equal(quoteExecArg('/a b'), '"/a b"')
  assert.equal(quoteExecArg('/a"b'), '"/a\\\\"b"')    // \" → escape general → \\"
  assert.equal(quoteExecArg('/a$b'), '"/a\\\\$b"')
  assert.equal(quoteExecArg('/a\\b'), '"/a\\\\\\\\b"') // cuatro barras para una barra literal
  assert.equal(quoteExecArg('/100%'), '"/100%%"')
})

test('la versión no puede romper el archivo con saltos de línea', () => {
  const t = desktopEntryText({ exec: '/x', version: '1.0.0\nExec=malo', hidden: false })
  assert.equal(t.split('\n').filter((l) => l.startsWith('Exec=')).length, 1)
})
