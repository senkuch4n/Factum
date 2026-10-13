# impl_frontend — informe-formato-fixes (#62)

**Estado:** done · **Fecha:** 2026-10-13 · **Rama:** `feat/informe-formato-fixes` (sin commit, lo hace el orquestador)
**App:** solo `client/`. Sin cambios visuales.

## Archivos tocados

- `client/src/lib/report-markdown.ts` — `cleanText` reescrita según SDD §5.3 + helper privado `isStrippedCodePoint`;
  JSDoc actualizado. Blob resultante: `68d66934e2c4e98b7eec4510d5c25491f365ca9b`.

No se tocaron `plainToMarkdown`, `catalogs.ts`, `admin-accounts.ts` ni `dashboard/page.tsx` (checklist §10.2).

## Qué hace `cleanText` ahora (espejo de `ReportValues.Clean` + `SafeText.Clean(MultiLine)`)

1. Recorre por code unit con detección de pares surrogate:
   - par alto+bajo válido → arma el code point; se descarta si es no-carácter (`(cp & 0xFFFE) === 0xFFFE`), si no se conserva;
   - surrogate sin pareja (alto sin bajo o bajo sin alto) → se descarta;
   - code point BMP → se descartan C0 salvo `\t \n \r`, U+202A-202E, U+2066-2069, U+FDD0-FDEF, U+FFFE y U+FFFF.
   - **DEL y C1 (U+007F-U+009F) se conservan**; también LRM/RLM, ALM, ZWJ/ZWNJ, NBSP, BOM, U+2028/2029.
2. Después `.replace(/\r\n?/g, "\n").replace(/\t/g, " ")` (mismo orden que el backend: `"\r￾\n"` da un solo salto).

Lista de caracteres quitados/conservados = tabla de la SDD §5.1 (columna "Render").

## Verificación

- `cd client && npx tsc --noEmit` → exit 0, sin errores.
- `npx eslint src/lib/report-markdown.ts` → **no aplicable**: `client/` no tiene ESLint instalado ni `eslint.config.*`
  (npx bajó ESLint 10 global y falló por falta de config). La nueva implementación ya no usa regex con caracteres de
  control, así que el `eslint-disable no-control-regex` anterior se fue con ella.
- No hay tests de `client/` para `report-markdown` (ni runner de tests en `client/`).
- Script descartable de paridad (fuera del repo, en el scratchpad, `npx tsx check.ts` importando
  `client/src/lib/report-markdown.ts`), `plainToMarkdown` contra F1-F24 con las salidas de
  `ReportMarkdownTests.Fixtures` (F1-F17) y la tabla de la SDD §5.3 (F18-F24), más extras de `cleanText`
  (cada uno también comprobando idempotencia `cleanText(cleanText(x)) === cleanText(x)`):

```
OK   F1 .. OK F17   (sin cambios respecto de antes)
OK   F18 "AB"
OK   F19 "xevily"
OK   F20 "abc‎d"            (LRM conservado)
OK   F21 "lone hi ok 😀"         (par válido conservado)
OK   F22 "del\u007Fc1\u0085"     (DEL/C1 conservados; comparación exacta por ===)
OK   F23 "fin"
OK   F24 "x"
OK   X1 C0 fuera, \t→esp, \r\n→\n         "a b\nc\nd"
OK   X2 conserva ZWJ/ZWNJ/NBSP/BOM/LS/ALM/RLM
OK   X3 surrogate alto al final           "fin"
OK   X4 surrogate bajo al principio       "ini"
OK   X5 no-carácter plano 16 (U+10FFFF/U+10FFFE)  "ab"
OK   X6 "a\r￾\nb"                    "a\nb"
todo OK
```

Nota de paridad F22: el recorte de fin de línea de `plainToMarkdown` usa `/\s+$/u` (JS: U+0085 no es `\s`) y el
backend usa `TrailingSpace` construido con `JsSpace` (mismo conjunto que el `\s` de JS), así que U+0085 al final se
conserva en los dos lados. No hay divergencia.

## Coherencia con el backend

`progress/impl_backend_informe-formato-fixes.md` **todavía no existía** al terminar, así que no se pudo cruzar con la lista
final del implementer-backend. La implementación sigue literalmente la tabla de §5.1/§5.3; el reviewer debería
confirmar que `SafeText.IsStrippedControl/IsBidiControl/IsNonCharacter` del backend coinciden con
`isStrippedCodePoint` de acá (los vectores F18-F24 del lado C# son los mismos).

## Contrato compartido

Sin campos JSON nuevos ni renombrados. El único contrato (comportamiento de `cleanText` ↔ `ReportValues.Clean`,
SDD §8) coincide con la SDD: mismos caracteres quitados y conservados, mismo orden.

## Decisiones no obvias

- Recorrido manual por code unit en vez de regex con flag `u`: así un surrogate suelto se detecta y descarta
  explícitamente igual que en C# (una regex `u` no matchea surrogates sueltos de forma portable), y se evita el
  `eslint-disable no-control-regex`.
- Se copia por tramos (`slice`) en vez de concatenar carácter a carácter: costo lineal y sin asignaciones cuando el
  texto no tiene nada que quitar (el caso normal).

## Skills invocados

- `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`, `web-design-guidelines`: invocados. Revisado con
  ui-ux-pro-max / senior-frontend / 3d-web-experience / web-design-guidelines: **sin hallazgos aplicables** (el cambio
  es una función pura de `lib/`, sin JSX, CSS, animaciones, 3D ni superficie de accesibilidad). `web-design-guidelines`
  no pudo descargar las guías remotas (no hay WebFetch en este entorno); como el archivo no tiene UI, no cambia la
  conclusión.
- `ui-styling` y `mblode-agent-skills-ui-animation`: no corresponden (sin Tailwind ni transiciones).
