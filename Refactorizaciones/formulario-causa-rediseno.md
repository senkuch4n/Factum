# SDD: Rediseño del formulario "Datos de la causa" (paso 2 del wizard)

**Slug:** `formulario-causa-rediseno` · **Issue:** #56
**HU:** `docs/hu-formulario-causa-rediseno.md` (validada el 2026-10-13: D1 A, **D2 B**, D3 A, D4 A, D5–D11 A).
**Rama:** `feat/formulario-causa-rediseno`, apilada sobre `feat/formulario-inspeccion-ux` (HU #52,
commit `e705e3d`). La base de este análisis es `62c6ba5` (números de línea aproximados; guiarse
por el contenido). **No se deshace nada de la SDD `Refactorizaciones/formulario-inspeccion-ux.md`**
(sus D1–D11 siguen vigentes; ver §3).
**Implementador:** `implementer-frontend` (solo `client/`), Opus.
**Skill del catálogo (`skills/CATALOGO.md`):** ninguno aplica (el catálogo está vacío). Las skills de
UX/UI son obligatorias por `AGENTS.md`; ver §9.

---

## 1. Resumen funcional

El paso 2 del wizard de `/dashboard` (`CaseFormStep`) pasa de "equipo a la izquierda +
formulario a la derecha" a **"formulario a la izquierda + Ficha viva de la causa a la derecha"**
(`lg+`). La ficha es una tarjeta `sticky` con aspecto de carátula que muestra en vivo
expediente, tipo, fecha en palabras, carátula, "Trámite ante … con la integración de …",
partes y proponente con las mismas frases que el informe; un índice de secciones con los
obligatorios que faltan (cada ítem lleva al primer faltante), y el equipo compacto con un
**modelo 3D del teléfono (React Three Fiber)** que lleva la captura como textura de pantalla,
cargado en diferido, solo en `lg+`, con fallback 2D (`PhoneShell` actual) si hay movimiento
reducido, si no hay WebGL acelerado o si algo falla. Por debajo de `lg` la ficha se abre desde
una barra `sticky` inferior ("1234/2026 · Faltan 3 · Ver ficha") en un panel lateral, y el
estado de conexión + aviso de IMEI quedan arriba del formulario. El formulario se reordena en
**Causa → Destinatario → Partes → Equipo**; la precarga desde causas anteriores se vuelve
**visible** (aviso con la causa de origen, marca por campo, "Empezar en blanco" con "Deshacer",
"Cambiar causa de origen" con buscador sobre el historial ya cargado); la fecha suma atajos
"Hoy"/"Ayer", la fecha en palabras y una advertencia no bloqueante si es futura; el expediente
avisa (sin bloquear) si ya existe en el historial; integrantes ofrece "Separar en N" al pegar
varios nombres. Sin backend: los cuerpos de `POST`/`PUT /api/cases` no cambian.

## 2. Toca

- **backend (API):** no
- **backend (Tatana):** no
- **client:** **sí** (`client/`)
- **agent-ui:** no

Dependencias nuevas en `client/` (D2): `three`, `@react-three/fiber`, `@types/three` (dev).
**No** se agrega `@react-three/drei` (D3).

## 3. Lo que ya resolvió la #52 y se respeta

| De la SDD #52 | Cómo queda en esta HU |
|---|---|
| D1 `useDeviceConnection` en `page.tsx`, prop `connection` | Igual. |
| D2 indicador de 3 estados, **única** región `role="status"` de conexión | Se mueve de lugar (ficha en `lg+`, franja superior en `< lg`) pero sigue siendo **una sola instancia montada** (§6.4). Mismo `CONNECTION_UI`. |
| D3 `FxBanner tone="warn" role="note"` de desconexión | Igual, mismo texto, junto al indicador. |
| D4 captura en flancos de conexión (`takeShot`, `prevConnection`, `shotInFlight`, `mountedRef`) | **Sin cambios**: el estado de la captura sigue en `CaseFormStep`. |
| D5/D6 estados de error/"Captura anterior"/`disabledReason` en `PhoneFrame` | Se conservan; `PhoneFrame` se parte en `PhoneScreen` + `CaptureControls` sin cambiar su API pública (§6.6). |
| D7 `<form noValidate>` real, `submitInFlight`, todos los botones internos `type="button"` | Igual. Toda botonera nueva dentro del `<form>` lleva `type="button"`. Los diálogos nuevos quedan **fuera** del `<form>` en el árbol de React. |
| D8 guards de Enter (`CatalogAutoComplete`, `Calendar`, integrantes) | Igual. Nuevos: Enter en la carátula (textarea) envía; Enter en el buscador del diálogo de origen no hace nada (§6.9, §6.11). |
| D10/D11 `useCatalogs.reload()` + banner "Reintentar" | Igual; el foco post-reintento va a `case-nombre_tribunal` en la sección **`destinatario`**. |
| Panel compacto `< lg` con "Ver equipo" | Lo reemplaza la franja superior + ficha en panel (la HU lo pide). El aviso de IMEI sigue visible sin desplegar nada. |
| Desconectado no bloquea guardar; sin refactor de `page.tsx`/`CaptureStep`; no tocar `--fx-text-3` | Igual. `page.tsx` solo suma un estado chico y props (§6.2). `CaptureStep` no se toca. |

## 4. Modelo de datos / Endpoints / Contrato

**No aplica.** Ningún campo, colección, índice ni endpoint nuevo. `formToCaseRequest`,
`validateCaseForm`, `CASE_REQUIRED_KEYS`, `CASE_MESSAGES` y `handleSaveCase` **no cambian**.
La causa de origen, las marcas de "precargado", la advertencia de fecha y el aviso de
expediente son estado solo de cliente (no se guardan).

### Contrato compartido (solo lectura de lo existente)

No se agrega nada. Se consume lo que el cliente ya tiene:

| Fuente | Campos JSON leídos (snake_case, `JsonNamingPolicy.SnakeCaseLower`) | Archivo cliente | Uso |
|---|---|---|---|
| `GET /api/cases` (ya cargado por `useAuth` → `historyCases`) | `id`, `created_at`, `schema_version`, `nro_referencia`, `caratula`, `nombre_tribunal`, `organismo_tribunal`, `sala_tribunal`, `integrantes`, `integrantes_tribunal`, `tipo_causa`, `nombre_proponente`, `profesion_proponente`, `matricula_proponente`, `device.manufacturer`, `device.model` | `client/src/lib/api.ts` (`interface Case`) | buscador de origen, aviso de expediente, precarga |
| Tatana `GET /files/{filename}` (PNG de la captura) | binario; con header `Origin` Tatana responde `Access-Control-Allow-Origin: <origen permitido>` (`server/src/Factum.Agent/Program.cs` ~L175-200) | `agentFileURL` en `client/src/lib/agent.ts` | textura del modelo 3D con `crossOrigin="anonymous"` (D7) |

Reglas de frases replicadas del servidor (comentario obligatorio en el helper: "tiene que
coincidir con `server/src/Factum.Backend/Services/Reports/ReportValues.cs` `Placeholders`"):

- `{tramiteAnte}` = `sala.Length > 0 ? $"{sala} de {tribunal}" : tribunal` (ambos `Trim()`).
- `{fraseIntegracion}` = integrantes no vacío ? `", con la integración de " + integrantes` : `""`;
  del lado cliente, `integrantes = joinIntegrantes(cleanIntegrantes(rows))` (ya es idéntico a
  `IntegrantesFormatter.Join`).
- `{datosProponente}` = `nombre` + (profesión ? `" – " + profesión` : "") + (matrícula ? `", M.P. " + matrícula` : "")
  (el guion es **en dash U+2013** con espacios, como en el servidor).
- `{fechaIntervencion}` = `dd/MM/yyyy` (la ficha muestra además la fecha en palabras).

## 5. Decisiones de la HU (validadas, no re-preguntar)

| Duda | Decisión |
|---|---|
| 1 | **A** — Formulario + Ficha viva. |
| 2 | **B** — 3D acotado con React Three Fiber: teléfono en la ficha, `dynamic import`, solo `lg+`; fallback 2D con `prefers-reduced-motion` o sin WebGL. |
| 3 | **A** — precarga visible + "Empezar en blanco" con deshacer + "Cambiar causa de origen" (mismos campos que hoy). |
| 4 | **A** — microinteracciones con tokens `--fx-*`, solo transform/opacity, nunca por tecla; variante reducida. |
| 5 | **A** — sin diálogo de revisión; "Crear caso y continuar" crea directo. |
| 6 | **A** — Perfil → Causa → Destinatario → Partes → Equipo. |
| 7 | **A** — fecha futura = advertencia no bloqueante. |
| 8 | **A** — aviso de expediente repetido no bloqueante, al salir del campo. |
| 9 | **A** — nada de backend. Sin `localStorage` del borrador. |
| 10 | **A** — carátula literal, sin generador. Campo multilínea con contador. |
| 11 | **A** — una sola HU. |

## 6. Decisiones técnicas

Todas son del arquitecto salvo la marcada **[usuario]** (ver §11, con default conservador).

### 6.1 Archivos

Nuevos (todos `"use client"`):

| Archivo | Qué es |
|---|---|
| `client/src/components/case-form/CaseSummaryCard.tsx` | La ficha (variante `aside` en `lg+`, `drawer` en `< lg`). |
| `client/src/components/case-form/CaseSummaryBar.tsx` | Barra `sticky` inferior `< lg`. |
| `client/src/components/case-form/ConnectionIndicator.tsx` | Indicador de 3 estados + banner de desconexión (extraído tal cual de `CaseFormStep`; mueve `CONNECTION_UI` acá y lo exporta). |
| `client/src/components/case-form/ImeiMissingNotice.tsx` | El `FxBanner` de "IMEI no detectado" extraído tal cual. |
| `client/src/components/case-form/PrefillNotice.tsx` | Aviso de precarga / "Empezaste en blanco" con acciones. |
| `client/src/components/case-form/PrefillSourceDialog.tsx` | `Dialog` con buscador de causa de origen. |
| `client/src/components/case-form/DuplicateExpedienteNotice.tsx` | Aviso de expediente repetido. |
| `client/src/components/case-form/DeviceStage.tsx` | Elige 3D o 2D, capas y fundido. **Único** archivo que importa (vía `next/dynamic`) el modelo 3D. |
| `client/src/components/case-form/PhoneModel3D.tsx` | Escena R3F. **Único** archivo del repo que importa `three` / `@react-three/fiber`. |
| `client/src/components/case-form/phone3d-support.ts` | `canUsePhone3D()` (WebGL), flag de sesión `disablePhone3D()`, `readFxColor()`, `readFxDurationMs()`. Sin imports de `three`. |
| `client/src/components/case-form/Phone3DBoundary.tsx` | ErrorBoundary de clase (React 19) para el 3D. |
| `client/src/hooks/useMediaQuery.ts` | `useMediaQuery(query: string): boolean` con `useSyncExternalStore` (server snapshot `false`), mismo patrón que `useIsSheet` de `CaseDetailModal.tsx`. |
| `client/src/lib/case-summary.ts` | Helpers puros de la ficha y de las ayudas (ver 6.3). Sin React. |

Modificados: `client/src/components/CaseFormStep.tsx`, `client/src/components/PhoneFrame.tsx`,
`client/src/components/FormField.tsx` (prop `labelBadge`), `client/src/components/form/IntegrantesField.tsx`,
`client/src/lib/pericial.ts`, `client/src/app/dashboard/page.tsx` (mínimo),
`client/src/app/globals.css` (un keyframe), `client/package.json` + `client/package-lock.json`.

**No se tocan:** `CaptureStep.tsx`, `DesignSystemShowcase.tsx` (sigue compilando con la API
pública de `PhoneFrame`/`PhoneShell`), `validateCaseForm`, `formToCaseRequest`, `REQUIREMENT_META`,
los ids de campo `case-<clave>` / `profile-<clave>` / `case-imei` / `case-integrante-*`.

### 6.2 Estado de la precarga (dónde vive)

`CaseFormStep` se **desmonta** al ir al paso 1 y volver (`{step === 2 && …}` en `page.tsx`), así
que lo que tiene que sobrevivir vive en `page.tsx`, y es mínimo:

```ts
// client/src/lib/pericial.ts
export interface CasePrefill {
  /** id del caso de origen; null = el perito eligió "Empezar en blanco". */
  sourceId: string | null;
  /** El perito tocó "Mantener": se ocultan aviso y marcas. */
  confirmed: boolean;
}
```

`page.tsx`:
- `const [casePrefill, setCasePrefill] = useState<CasePrefill | null>(null);`
- `startWizard()`: después de `setCaseForm(newCaseForm(historyCases))`,
  `const src = lastPericialCase(historyCases); setCasePrefill(src ? { sourceId: src.id, confirmed: false } : null);`
- `proceedResume()` (incluye el flujo legacy) y `resetWizard()`: `setCasePrefill(null)`.
  **El flujo legacy sigue con la precarga silenciosa de hoy** (es modo edición: la HU dice
  "en edición no aparece el aviso ni las marcas").
- Props nuevas a `<CaseFormStep>`: `historyCases={historyCases}`, `caseId={currentCase?.id ?? null}`,
  `prefill={currentCase ? null : casePrefill}`, `onPrefillChange={setCasePrefill}`.
- No se toca nada más de `page.tsx` (`handleSaveCase`, `go`, `handleWsEvent`, `newCaseForm` siguen igual).

Las **marcas por campo se derivan** (sin `Set` de "tocados"): un campo reutilizable está
"precargado sin revisar" si `prefill && !prefill.confirmed && prefill.sourceId` y su valor actual
es **igual y no vacío** al que da `prefillFromCase(source)`. Editarlo lo cambia → la marca se va
sola; si vuelve a escribir exactamente lo mismo, la marca vuelve (aceptable). El snapshot para
"Deshacer" es **efímero**, en estado local de `CaseFormStep` (si se desmonta, el aviso ya no está
visible, que es lo que pide la HU).

### 6.3 Helpers puros

En `client/src/lib/pericial.ts`:

```ts
/** Campos que se copian de la causa de origen (los mismos que hoy; D3 A). */
export const PREFILL_KEYS = [
  "nombre_tribunal", "organismo_tribunal", "sala_tribunal", "integrantes",
  "tipo_causa", "nombre_proponente", "profesion_proponente", "matricula_proponente",
] as const satisfies readonly (keyof CaseFormData)[];
export type PrefillKey = (typeof PREFILL_KEYS)[number];

/** Primer caso pericial (schema_version >= 1) de un historial ordenado por created_at desc. */
export function lastPericialCase(historyCases: Case[]): Case | null;
/** Causas elegibles como origen: schema_version >= 1 (mismo criterio de hoy). */
export function prefillSources(historyCases: Case[]): Case[];
/** Valores reutilizables de una causa (refactor del cuerpo actual de prefillFromLastCase). */
export function prefillFromCase(cas: Case): Pick<CaseFormData, PrefillKey>;
/** Se conserva con la misma firma: `const c = lastPericialCase(h); return c ? prefillFromCase(c) : {};` */
export function prefillFromLastCase(historyCases: Case[]): Partial<CaseFormData>;
/** Claves con valor igual y no vacío al de la causa de origen (integrantes: joinIntegrantes(cleanIntegrantes(...)) iguales y no vacíos). */
export function prefilledKeys(form: CaseFormData, source: Pick<CaseFormData, PrefillKey>): Set<PrefillKey>;
```

`CASE_FORM_FOCUS_ORDER` pasa al orden visual nuevo (es lo único de validación que cambia; la HU
lo permite):

```ts
export const CASE_FORM_FOCUS_ORDER: string[] = [
  ...PROFILE_REQUIRED_KEYS.map(profileErrorKey),
  "nro_referencia", "caratula", "tipo_causa", "fecha_intervencion",   // Causa
  "nombre_tribunal",                                                   // Destinatario
  "parte_denunciante", "parte_denunciada", "nombre_proponente",        // Partes
  "nombre_denunciante", "tipo_dispositivo", "imei",                    // Equipo
];
```

En `client/src/lib/case-summary.ts` (puro, importable sin React):

```ts
/** "Sala II de Tribunal X" | "Tribunal X" | "" — espejo de {tramiteAnte}. */
export function tramiteAnte(tribunal: string, sala: string): string;
/** ", con la integración de A y B" | "" — espejo de {fraseIntegracion}. */
export function fraseIntegracion(rows: IntegranteRow[]): string;
/** "Nombre – Profesión, M.P. 123" | "" — espejo de {datosProponente}. */
export function datosProponente(nombre: string, profesion: string, matricula: string): string;

/** "lunes 12 de octubre de 2026" (sin coma), o null si `iso` no es una fecha válida (isValidIsoDate). */
export function fechaEnPalabras(iso: string): string | null;
/** iso válido y > todayIso() (comparación de strings yyyy-MM-dd). */
export function isFutureIsoDate(iso: string): boolean;
/** todayIso() menos un día, en local. */
export function yesterdayIso(): string;

/** Clave de comparación de expedientes (D8 A). */
export function normalizeExpediente(s: string): string;
/** Casos del historial con el mismo expediente normalizado, excluyendo `excludeId`, más reciente primero. Vacío si la clave es "". */
export function findSameExpediente(historyCases: Case[], nro: string, excludeId: string | null): Case[];

/** Partes de un pegado de integrantes; [] o [uno] si no hay nada que separar. */
export function splitIntegrantes(text: string): string[];

export type SummarySectionId = "perfil" | "causa" | "destinatario" | "partes" | "equipo";
export interface SummarySection { id: SummarySectionId; label: string; missing: number; firstMissingFieldId: string | null }
/** Índice de la ficha (puro). */
export function summarySections(args: {
  form: CaseFormData; profileForm: ProfileFormData; profileIncomplete: boolean; imeiManual: boolean;
}): SummarySection[];
```

Reglas exactas:

- **`fechaEnPalabras`**: `Intl.DateTimeFormat("es-AR", { weekday: "long", day: "numeric", month: "long", year: "numeric" }).formatToParts(date)`
  y armar `${weekday} ${day} de ${month} de ${year}` (verificado: `format` devuelve
  "lunes, 12 de octubre de 2026" con coma; la HU pide sin coma). `date` se construye local
  (`new Date(y, m-1, d)`), nunca con `new Date("yyyy-mm-dd")` (UTC).
- **`normalizeExpediente`**: `s.normalize("NFKC").toLowerCase()`, sacar **todos** los espacios
  (`/\s+/g` → ""), y quitar ceros a la izquierda de cada corrida de dígitos
  (`/\d+/g` → `String(Number(run))` solo si `run.length <= 15`; si no, `run.replace(/^0+(?=\d)/, "")`).
  Ejemplos que el implementador verifica a mano: `"1234/2026"`, `" 01234 / 2026 "`, `"1234/2026 "`
  → misma clave; `"EXP-0012/26"` ≡ `"exp-12/26"`; `"1234/2025"` ≢ `"1234/2026"`; `""` → `""`.
- **`findSameExpediente`**: compara contra **todos** los casos del historial (también
  `schema_version` 0), excluye `excludeId`, ordena por `created_at` desc.
- **`splitIntegrantes`**: (1) separar por `\r?\n`, `;` y `,`; (2) sobre el **último** fragmento,
  separar una sola vez por la **última** ocurrencia de `/\s+(?:y|e)\s+/i`; (3) `cleanCatalogValue`
  a cada parte y descartar vacías. Ejemplos: `"Dr. Juan Pérez, Dra. Ana Gómez y Dr. Luis Díaz"` → 3;
  `"Dr. A\nDra. B"` → 2; `"Dr. A y Dra. B"` → 2; `"Dr. Juan Pérez"` → 1; `"A; B; C e Ignacio"` → 4.
  Nunca se aplica sin confirmación (los nombres pueden llevar comas).
- **`summarySections`** (usa los mismos criterios que el `n/m`: `trim().length > 0`):

  | Sección | Etiqueta | Claves obligatorias contadas |
  |---|---|---|
  | `perfil` | "Perfil" | `PROFILE_REQUIRED_KEYS` **solo si** `profileIncomplete`; si no, 0 |
  | `causa` | "Causa" | `nro_referencia`, `caratula`, `tipo_causa`, `fecha_intervencion` |
  | `destinatario` | "Destinatario" | `nombre_tribunal` |
  | `partes` | "Partes" | `parte_denunciante`, `parte_denunciada`, `nombre_proponente` |
  | `equipo` | "Equipo" | `nombre_denunciante`, `tipo_dispositivo`, + `imeiOverride` (id `case-imei`) si `imeiManual` |

  `firstMissingFieldId` = id del primer vacío en el orden de `CASE_FORM_FOCUS_ORDER` (perfil:
  `profileFieldId(k)`; resto: `caseFieldId(k)`).

### 6.4 Layout de `CaseFormStep`

`const isLg = useMediaQuery("(min-width: 1024px)");` (= breakpoint `lg` de Tailwind). Se monta
**o** la ficha `aside` **o** la barra + panel, nunca las dos (evita ids y regiones vivas
duplicadas).

```
<div className="space-y-4">
  {!isLg && <DeviceStatusStrip/>}              // fila compacta: ícono plataforma + Tag SO + "Marca Modelo" + <ConnectionIndicator/>,
                                               // debajo: banner de desconexión (si aplica) + <ImeiMissingNotice/> (si imeiManual)
  <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_17.5rem] xl:grid-cols-[minmax(0,1fr)_20rem]">
    <form …>…</form>                           // primero en el DOM (orden de foco: formulario → ficha)
    {isLg && (
      <aside aria-label="Resumen de la causa"
             className="min-w-0 lg:sticky lg:top-6 lg:self-start lg:max-h-[calc(100dvh-3rem)] lg:overflow-y-auto overscroll-contain">
        <CaseSummaryCard variant="aside" …/>
      </aside>
    )}
  </div>
  {!isLg && <Dialog position="right" …><CaseSummaryCard variant="drawer" …/></Dialog>}
  <CatalogManageDialog …/>   <PrefillSourceDialog …/>   <Lightbox …/>     // fuera del <form> (D7 #52)
</div>
```

- `lg:top-6` = el mismo offset que el `sticky` del paso 3 (`CaptureStep.tsx` ~L797) y del
  `aside` del wizard (`page.tsx` ~L873). La `fx-card` contenedora no recorta (comentario de
  `page.tsx` ~L933), así que el `sticky` funciona.
- `DeviceStatusStrip` puede ser JSX inline en `CaseFormStep` o un componente local; usa
  `<ConnectionIndicator>` (la región `role="status"` de #52 D2) e `<ImeiMissingNotice>`.
  En `lg+` esos mismos dos componentes se renderizan **dentro de la ficha** (bloque Equipo).
  Como solo una de las dos ramas está montada, sigue habiendo una sola región de conexión.
- El `StepHeader` "Datos de la causa" con barra + `n/m` **se conserva** (la ficha lo complementa;
  no se duplica la región viva porque el `n/m` no es `role="status"`).
- El ancho mínimo de la columna del formulario en 1024 px queda ≈ igual que hoy
  (antes `1.75fr` de ≈ 740 px; ahora `1fr` − 17.5 rem). Verificar 1024 px sin scroll horizontal.

### 6.5 Orden y secciones del formulario

`SectionId` = `"perfil" | "causa" | "destinatario" | "partes" | "equipo"` (= `SummarySectionId`).
Reemplazar `ACTUACION_A/B/ACTUACION` por:

```ts
const CAUSA: FieldDef[] = [
  { key: "nro_referencia",     label: "Número de causa / expediente", icon: Hash, required: true, placeholder: "Ej.: 1234/2026", mono: true },
  { key: "caratula",           label: "Carátula", icon: FileText, required: true, placeholder: "Carátula completa de la causa…", maxLength: MAX_LEN_LONG, wide: true, multiline: true },
  { key: "tipo_causa",         label: "Tipo de causa", icon: Scale, required: true, hint: "Ej.: disciplinaria, civil, penal" },
  { key: "fecha_intervencion", label: "Fecha de intervención", icon: CalendarIcon, required: true, type: "date" },
  { key: "objeto_causa",       label: "Objeto de la causa", icon: FileText },
  { key: "ambito_causa",       label: "Ámbito", icon: MapPin },
];
const DESTINATARIO: FieldDef[] = [
  { key: "nombre_tribunal",    label: "Destinatario", sublabel: "(tribunal, fiscalía, persona…)", icon: Landmark, required: true, placeholder: "Tribunal, fiscalía o persona…", catalog: "destinatarios" },
  { key: "organismo_tribunal", label: "Organismo", icon: Building2, placeholder: "Organismo del que depende…" },
  { key: "sala_tribunal",      label: "Sala", icon: Landmark, hint: "Ej.: Sala II" },
];  // + <IntegrantesField> a continuación, dentro de la misma grilla
// PARTES y EQUIPO: sin cambios de contenido ni orden.
```

- Etiquetas, hints, placeholders y obligatorios: **idénticos a hoy** (solo cambia el orden).
- `SECTION_OF`, `sectionOfErrorKey`, `sectionOfFieldId`: mapear a las secciones nuevas;
  `integrantes` y `case-integrante*` → `"destinatario"`; fallback `"causa"`.
- Estado inicial de plegado: `{ perfil: false, causa: true, destinatario: true, partes: true, equipo: true }`
  (la lógica de "perfil incompleto lo despliega" no cambia).
- Títulos de `FormSection`: "Causa", "Destinatario", "Partes", "Equipo". Los ids
  `case-section-<id>` cambian (`actuacion` → `causa`/`destinatario`); verificado que nada fuera de
  `CaseFormStep.tsx` los referencia (`grep -rn "actuacion" client/src`).
- `FormSection` suma un contador en el encabezado: si `missing > 0`, a la derecha del título
  `<span className="ml-auto text-xs normal-case text-fx-text-2">Faltan {n}</span>`; si 0 y la
  sección tiene obligatorios, `CheckCircle2` `text-fx-success` + `sr-only` "completa". Texto, no
  solo color. (Se calcula con `summarySections`.)
- `FieldDef` suma `mono?: boolean` (expediente: `font-mono` + `translate="no"` en el input, vía pt
  como el resto) y `multiline?: boolean` (6.9).

### 6.6 `PhoneFrame` partido (API pública intacta)

En `client/src/components/PhoneFrame.tsx`:

- `export function PhoneScreen({ src, platform, loading, error, stale, className })`: el
  `<PhoneShell>` con los cuatro estados actuales (Capturando… / imagen / imagen atenuada +
  "Captura anterior" / "No se pudo capturar" / "Sin captura"). Prop nueva opcional
  `className` para el ancho (default `max-w-[160px]`).
- `export function CaptureControls({ loading, onRefresh, error, disabledReason })`: el
  contenedor `role="status"` del motivo + botón "Actualizar captura"/"Reintentar" + `disabledReason`
  (exactamente el JSX de hoy, mismo `useId`).
- `PhoneFrame` = `<div className="flex flex-col items-center gap-3"><PhoneScreen/><CaptureControls/></div>`,
  misma firma de props. `PhoneShell` sin cambios (lo usa `CaptureStep`).

### 6.7 La ficha — `CaseSummaryCard`

Props:

```ts
interface CaseSummaryCardProps {
  variant: "aside" | "drawer";
  form: CaseFormData;
  sections: SummarySection[];               // de summarySections (memo en CaseFormStep)
  onGoToSection: (s: SummarySection) => void;
  flashKey: { key: string; n: number } | null; // 6.13
  device: Device; osLabel: string; isIOS: boolean;
  imeiDisplay: string; imeiManual: boolean;
  connection: DeviceConnection;
  shot: { src: string | null; loading: boolean; error: string | null; stale: boolean; disabledReason: string | null; showCaptured: boolean };
  onRefreshShot: () => void;                // useCallback estable
  onZoomShot: () => void;                   // abre Lightbox con shot.src
}
```

Estructura (de arriba abajo), tarjeta `rounded-fx-lg border border-fx-border bg-fx-surface-1 shadow-fx-1`
(en `drawer` sin borde ni sombra: la pone el panel):

1. **Encabezado** (`px-4 pt-4`): etiqueta `text-fx-label uppercase text-fx-text-2` "Expediente";
   el número en `font-mono text-fx-h3 text-fx-text` con `translate="no"` y `break-all`; a la derecha
   `Tag` con el tipo de causa (si hay). Debajo, la fecha en palabras (`text-fx-body-sm text-fx-text-2`)
   y, si es futura, ` · ` + `AlertTriangle` + "posterior a hoy" en `text-fx-warning` (texto, no solo color).
2. **Faltantes** (`role="status"`, `aria-atomic="true"`, siempre montado): "Faltan N obligatorios"
   o "Todos los obligatorios completos" (`CheckCircle2` `text-fx-success`). Su texto solo cambia
   cuando cambia N → anuncia una vez por cambio, no por tecla. **Es la única región viva de la ficha.**
3. **Índice de secciones**: `<nav aria-label="Secciones del formulario"><ul>` con un `<button type="button">`
   por sección con obligatorios (Perfil solo si `profileIncomplete`, y además siempre que haya error de
   perfil): etiqueta + a la derecha `CheckCircle2` + `sr-only` "completa", o `Faltan N` (texto). Clic →
   `onGoToSection` → `focusField(firstMissingFieldId ?? <primer campo de la sección>, id)` (la función
   existente; despliega si está plegada). `min-h-9`, `[@media(pointer:coarse)]:min-h-11`, `fx-focus-ring`.
4. **Carátula**: `<p>` con `line-clamp-4 whitespace-pre-line break-words`; si `caratula.length > 180`
   botón texto "Ver todo"/"Ver menos" (`aria-expanded`) que quita el clamp. Regla de longitud y no
   medición del DOM (nada de efectos por tecla).
5. **Trámite**: "Trámite ante " + `tramiteAnte(...)` + `fraseIntegracion(...)`; organismo debajo en
   `text-fx-text-2` si hay.
6. **Partes**: `<dl>` "Denunciante" / "Denunciada" / "Propone" (`datosProponente`) / "Titular del dispositivo".
7. **Equipo** (`border-t`, `bg-fx-surface-2`): grilla `grid-cols-[6.5rem_minmax(0,1fr)] gap-3`:
   - izquierda: `<DeviceStage>` (6.8) dentro de un `<button type="button" aria-label="Ver captura ampliada">`
     cuando hay `shot.src` (abre `Lightbox`); sin captura, un `<div>` no interactivo.
   - derecha: ícono de plataforma (`/apple.svg`|`/android.svg`, `dark:invert`, como hoy) + `Tag` SO;
     "Marca Modelo" (`font-semibold`, `truncate` con `title`); IMEI en mono o "IMEI no detectado"
     (`text-fx-warning` + ícono); `<ConnectionIndicator>` **solo en `variant="aside"`**;
     `<CaptureControls>`; disclosure "Especificaciones" (botón `aria-expanded`, plegado por defecto)
     con los `SpecRow` actuales (IMEI, Serial, Nombre).
   - debajo, a todo el ancho: banner de desconexión + `<ImeiMissingNotice>` **solo en `aside`** (en
     `< lg` ya están en la franja superior).
   - el "Pantalla capturada" `role="status"` de #52 se conserva junto a `CaptureControls`.

Vacíos: marcador `italic text-fx-text-3` ("Sin expediente", "Sin carátula", "Sin destinatario",
"Sin fecha", "Sin denunciante", "Sin denunciada", "Sin proponente", "Sin titular"); nunca texto de
ejemplo. Valores con `whitespace-pre-line`? **No**: todos son de una línea salvo la carátula.

`memo(CaseSummaryCard)` no es necesario (se re-renderiza con el form a propósito y es DOM barato);
lo que **sí** se memoiza es `DeviceStage` (6.8).

### 6.8 Modelo 3D (D2 B) — `DeviceStage` + `PhoneModel3D`

**D2.1 Versiones (verificado el 2026-10-13 con `npm view`, React instalado 19.0.0, Next 16.2.10):**

| Paquete | Versión exacta | Peer relevantes |
|---|---|---|
| `three` | `0.186.1` | — |
| `@react-three/fiber` | `9.8.1` | `react >=19 <19.4`, `react-dom >=19 <19.4`, `three >=0.156` (los peers `expo*`/`react-native` son opcionales) ✔ |
| `@types/three` (dev) | `0.186.0` | — |
| `@react-three/drei` | **no se instala** (10.7.9 sería la compatible: `react ^19`, `fiber ^9`) | ver D3 |

```bash
cd client
npm install --save-exact three@0.186.1 @react-three/fiber@9.8.1
npm install --save-dev --save-exact @types/three@0.186.0
```

Si `npm install` reporta conflicto de peers, **parar y reportar `blocked`** (no usar
`--legacy-peer-deps` sin avisar). Ojo con `react` `^19.0.0` en `package.json`: si algún día
resuelve a ≥ 19.4, fiber 9.8.1 queda fuera de rango — dejarlo anotado en el progress.

**D3 Sin drei.** Lo único que daría (`RoundedBox`, `useTexture`, `Environment`) se resuelve con
`three` puro en ~60 líneas, y drei arrastra `three-stdlib` y otras dependencias. Menos bundle,
menos superficie.

**D4 Carga diferida y elegibilidad.** `DeviceStage` (exportado con `memo`) decide:

```ts
const PhoneModel3D = dynamic(() => import("./PhoneModel3D"), { ssr: false, loading: () => null });
```

Se renderiza `<PhoneModel3D>` **solo si todo** esto es verdadero (hook `usePhone3DEligible()` en
`DeviceStage`):
1. `variant === "aside"` (la ficha `aside` solo existe con `isLg`; el panel `< lg` usa 2D siempre).
2. `!useMediaQuery("(prefers-reduced-motion: reduce)")` (reactivo: si cambia en caliente, se desmonta y queda 2D).
3. `canUsePhone3D()` (6.8 D5) y el flag de sesión no está apagado.
4. `process.env.NEXT_PUBLIC_DEVICE_3D !== "off"` (interruptor de despliegue para clientes con
   PCs problemáticas; se lee en build, documentarlo en el progress; **no** se agrega a
   `next.config.ts`: las `NEXT_PUBLIC_*` se inlinean solas).
5. `!(navigator as any).connection?.saveData`.
6. `idle === true`: un efecto al montar programa `requestIdleCallback(cb, { timeout: 1500 })`
   (fallback `setTimeout(cb, 300)`), con cancelación en el cleanup. Así el primer render del
   formulario y la ficha no esperan ni el chunk ni el contexto WebGL.

Como `next/dynamic` solo pide el chunk cuando el componente se renderiza, en `< lg`, con
movimiento reducido o sin WebGL **el chunk de three nunca se descarga**.

**D5 `canUsePhone3D()`** (`phone3d-support.ts`, resultado cacheado a nivel módulo):
crear un `<canvas>` fuera del DOM, `getContext("webgl2", { failIfMajorPerformanceCaveat: true })`
→ si no, `getContext("webgl", { failIfMajorPerformanceCaveat: true })`; si hay contexto,
`getExtension("WEBGL_lose_context")?.loseContext()` y devolver `true`; cualquier excepción → `false`.
`failIfMajorPerformanceCaveat` hace que en PCs con renderizado por software (sin aceleración,
escritorio remoto, SwiftShader) se use el 2D: es el escenario "PC de oficina modesta" de la HU.
`disablePhone3D()` pone el flag de sesión en `false` (lo llaman el ErrorBoundary, la pérdida de
contexto y el error de textura) para que remontar el paso no reintente.

**D6 Capas sin salto de layout.** `DeviceStage` es una caja `relative w-full` con
`aspectRatio: "236 / 470"`:
- Capa 1: `<PhoneScreen className="max-w-none">` (2D, siempre montada). Cuando el 3D avisa
  `onReady`, pasa a `invisible` (visibility hidden → fuera del árbol de accesibilidad).
- Capa 2 (si elegible): `<Phone3DBoundary onError={fail}><PhoneModel3D …/></Phone3DBoundary>` en
  `absolute inset-0`, `opacity-0` → `opacity-100` al `onReady` con
  `transition-opacity duration-fx-base ease-fx`.
- Capa 3 (solo con 3D listo): chips DOM `aria-hidden` abajo al centro, mismos textos que el 2D:
  "Capturando…" (con `Loader2` animado solo `motion-safe`), "Sin captura", "No se pudo capturar",
  "Captura anterior". El motivo del error sigue en `CaptureControls` (`role="status"`).
- `fail()` → `disablePhone3D()` + estado local `failed=true` → se desmonta la capa 2, la 1 vuelve a ser visible.
- `DeviceStage` es `memo` y recibe solo `{ src, platform, loading, error, stale, variant }`: tipear
  en el formulario **no** re-renderiza el árbol R3F.

**D7 `PhoneModel3D` — escena** (export default; único import de `three`/`@react-three/fiber`):

```ts
interface PhoneModel3DProps {
  src: string | null;            // URL de la captura (agentFileURL)
  dim: boolean;                  // stale → pantalla atenuada
  platform: "android" | "ios";
  onReady: () => void;           // después del primer frame dibujado
  onError: (reason: string) => void;
}
```

- `<Canvas frameloop="demand" dpr={[1, 1.5]} flat
   gl={{ antialias: true, alpha: true, powerPreference: "low-power", failIfMajorPerformanceCaveat: true, preserveDrawingBuffer: false }}
   camera={{ fov: 28, position: [0, 0, 3.4], near: 0.1, far: 20 }}
   style={{ pointerEvents: "none" }} aria-hidden="true">`.
  `flat` = sin tone mapping (los colores de la captura y de los tokens salen fieles).
  Sin `OrbitControls` ni handlers de puntero (ver §11 P1).
- **Geometría procedural** (todo con `useMemo` y `dispose()` en el cleanup):
  - `roundedRectShape(w, h, r): THREE.Shape` (helper local con `absarc`/`quadraticCurveTo`).
  - Cuerpo: `ExtrudeGeometry(roundedRectShape(0.72, 1.44, platform === "ios" ? 0.12 : 0.11),
    { depth: 0.06, bevelEnabled: true, bevelThickness: 0.012, bevelSize: 0.012, bevelSegments: 3, curveSegments: 10 })`,
    centrada (`geometry.center()`), `MeshStandardMaterial({ color: --fx-surface-3, roughness: 0.55, metalness: 0.15 })`.
  - Pantalla: `ShapeGeometry(roundedRectShape(0.66, 1.38, 0.085), 10)` en `z = depth/2 + bevelThickness + 0.0015`;
    **normalizar UV** al bounding box (las UV de `ShapeGeometry` vienen en coordenadas de la forma):
    `uv = ((x - minX) / w, (y - minY) / h)`. `MeshBasicMaterial({ map, color: dim ? 0x666666 : 0xffffff, toneMapped: false })`;
    sin `map`: `color = --fx-bg`.
  - Isla de cámara: iOS cápsula `roundedRectShape(0.17, 0.042, 0.021)`; Android círculo `CircleGeometry(0.018, 24)`;
    a `0.045` del borde superior de la pantalla, `z` +0.001, color `--fx-surface-3`.
  - Sin botones laterales ni detalles extra (cada malla es costo y mantenimiento).
- **Luces**: `<ambientLight intensity={0.9} />` + `<directionalLight position={[2, 3, 4]} intensity={1.6} />`.
  Ajustar a ojo en claro y oscuro; sin mapas de entorno ni sombras.
- **Pose**: `rotation = [0.06, -0.28, 0]` (rad). **Entrada única** al montar: `rotation.y` de `-0.7`
  a `-0.28` en `readFxDurationMs("--fx-dur-slow")` ms (320 ms hoy) con ease-out cúbico (aprox. de
  `--fx-ease-out`), dentro de `useFrame` llamando `invalidate()` mientras dura. Si
  `document.visibilityState !== "visible"` al montar, se arranca en la pose final. Nada en bucle,
  nada que siga al mouse.
- **Colores desde tokens**: `readFxColor(name)` = `getComputedStyle(document.documentElement).getPropertyValue(name).trim()`
  → `new THREE.Color().setStyle(value)` (hex/rgb/hsl). Se releen cuando cambia `useTheme().theme`
  (`client/src/lib/theme.ts`) y se hace `invalidate()`. Ningún hex en el código.
- **Textura** (sin `useLoader`, para controlar caché y errores): en un `useEffect` sobre `src`:
  - `const url = src + (src.includes("?") ? "&" : "?") + "tex=1";` **(D7a)** El `<img>` 2D pide la
    misma URL sin CORS (sin header `Origin`, Tatana no manda `Access-Control-Allow-Origin` ni `Vary`)
    y ese response puede quedar en la caché HTTP; el query distinto fuerza un request CORS propio.
    `FilesController.Download` ignora el query (verificado: ruta `files/{filename}`).
  - `new THREE.TextureLoader().setCrossOrigin("anonymous").load(url, onLoad, undefined, onErr)`.
  - `onLoad`: `tex.colorSpace = THREE.SRGBColorSpace`; **"cover"** como el `object-cover` del 2D:
    `imgAspect = image.width / image.height`, `screenAspect = 0.66 / 1.38`; si `imgAspect > screenAspect`
    → `repeat.x = screenAspect / imgAspect`, `offset.x = (1 - repeat.x) / 2`; si no →
    `repeat.y = imgAspect / screenAspect`, `offset.y = (1 - repeat.y) / 2`. Reemplazar la anterior
    con `prev?.dispose()`; `invalidate()`. Ignorar resultados de un `src` viejo (flag `cancelled` en el cleanup).
  - `onErr` → `onError("texture")` (el padre cae a 2D, donde el `<img>` sí se ve). Mientras
    `loading` la pantalla conserva la textura anterior (el chip "Capturando…" lo informa).
- **`onReady`**: en `onCreated` registrar `gl.domElement.addEventListener("webglcontextlost", e => { e.preventDefault(); onError("context_lost"); })`
  y llamar `onReady` en el primer `useFrame` (una vez, con ref).
- **Pausa**: con `frameloop="demand"` no se dibuja nada salvo tras `invalidate()` (entrada, textura
  nueva, cambio de tema, `dim`, resize que R3F maneja solo). Pestaña oculta → `requestAnimationFrame`
  no corre. No hace falta `IntersectionObserver` (la ficha es `sticky`, siempre a la vista).
- **Desmontaje / paso 3**: `CaseFormStep` se desmonta al pasar al paso 3 → se desmonta el `Canvas`;
  R3F libera el renderer y fuerza la pérdida del contexto. Además, cleanup propio:
  `texture.dispose()`, `geometry.dispose()` de las geometrías hechas con `useMemo`.
  **Ningún archivo fuera de `case-form/DeviceStage.tsx` importa `PhoneModel3D`**, y nada fuera de
  `PhoneModel3D.tsx` importa `three`/`@react-three/fiber` (criterio con grep en §8).

**D8 `Phone3DBoundary`**: clase con `static getDerivedStateFromError() { return { failed: true } }`,
`componentDidCatch(err) { this.props.onError(String(err)) }`, render `null` si falló. Atrapa errores
de render del chunk, de `new WebGLRenderer` (lo crea R3F dentro del árbol) y de import dinámico
(un chunk que no carga lanza en render). `console.warn` una sola vez con el motivo (sin datos de la causa).

**D9 Presupuesto de bundle.**
- Chunk(s) del 3D (three + fiber + `PhoneModel3D`): **≤ 260 KB gzip** en total
  (referencia: three ≈ 170-190 KB gz con el namespace completo que registra fiber; fiber ≈ 45-55 KB gz).
  Si se pasa, reportarlo en el progress con la cifra; no "optimizar" importando internals de three.
- Chunks que carga `/dashboard` al entrar: **0 bytes de three** (ninguno contiene `WebGLRenderer`).
- Cómo medir: §8.2. Next 16 ya **no** imprime "First Load JS" en `next build`
  (`node_modules/next/dist/docs/01-app/02-guides/upgrading/version-16.md` ~L1000); se mide sobre
  `.next/static/chunks` y, opcionalmente, con `npx next experimental-analyze --output`
  (Turbopack, v16.1+, escribe en `.next/diagnostics/analyze`).

### 6.9 Carátula multilínea

`multiline: true` → `InputTextarea` de Prime (pt global `inputtextarea` ya existe) con
`autoResize`, `rows={2}`, `maxLength={MAX_LEN_LONG}`, mismas props ARIA que el `InputText`
(`aria-required`, `aria-invalid`, `aria-describedby`). Debajo, contador `"{n}/500"`
(`text-xs tabular-nums text-fx-text-3`, `aria-hidden`; el límite ya lo impone `maxLength`).
Para no cambiar el dato que viaja (hoy es un `<input>` de una línea):
- `onChange`: `value.replace(/\s*\r?\n\s*/g, " ")` (un salto pegado se vuelve un espacio; el
  `<input>` actual directamente los borraba).
- `onKeyDown`: `Enter` sin `Shift` y sin `isComposing` → `preventDefault()` +
  `e.currentTarget.form?.requestSubmit()` (mismo "Enter envía" de #52; `requestSubmit` pasa por
  `handleFormSubmit` y su guard de doble envío). `Shift+Enter` tampoco inserta salto (lo
  normaliza `onChange`); no se anuncia como multilínea: `aria-multiline` no se pone.
- Check verde de "completo": igual que en los inputs (arriba a la derecha, `top-3` en vez de centrado).

### 6.10 Precarga visible

**Marca por campo.** `FormField` suma prop opcional `labelBadge?: React.ReactNode`, renderizada
dentro del `<label>` después del `sublabel` (contenido no interactivo, válido dentro de `label`).
`renderField` pasa, si `prefilled.has(f.key)`:

```tsx
<span className="ml-1.5 inline-flex align-middle text-fx-info" title="Tomado de la causa anterior">
  <History className="h-3.5 w-3.5" aria-hidden="true" />
  <span className="sr-only">(tomado de la causa anterior)</span>
</span>
```

Forma de ícono + texto accesible + `title` → no depende solo del color. Antecede al check verde
(que sigue apareciendo igual). `IntegrantesField` suma prop `prefilled?: boolean` y la pasa a su
`FormField` como `labelBadge`.

**`PrefillNotice`** (primer hijo del `<form>` después del aviso legacy y antes del `StepHeader`,
`FxBanner tone="info"` compacto; `role="status"`). Se muestra si `mode === "create"` y
(`prefill && !prefill.confirmed && (source || undo)`). Variantes:

| Estado | Texto | Acciones (`Button text size="small" type="button"`) |
|---|---|---|
| origen activo | "Datos tomados de la causa **{nro}** — {carátula abreviada a 60 + "…"}" (sin nro: "…de tu causa del {formatDate(created_at)}"). Segunda línea `text-xs text-fx-text-2`: "Revisá destinatario, sala, integrantes, tipo y proponente." | "Cambiar causa de origen" · "Empezar en blanco" · "Mantener" (`aria-label="Mantener los datos precargados"`) · si hay `undo`: "Deshacer" |
| en blanco (`sourceId === null`, con `undo`) | "Empezaste en blanco: se vaciaron {n} campos de la causa {nro}." | "Deshacer" · "Elegir causa de origen" · cerrar (`onClose` de `FxBanner` → descarta `undo`; aviso desaparece) |

- `source = historyCases.find(c => c.id === prefill.sourceId) ?? null`; si no se encuentra, no hay aviso.
- `prefilled = source && !prefill.confirmed ? prefilledKeys(form, prefillFromCase(source)) : ∅` (`useMemo`).
- **Empezar en blanco**: `undo = { values: pick(form, PREFILL_KEYS), prefill }`; vaciar **solo** las
  claves de `prefilled` (`""` o `[]`); `onPrefillChange({ sourceId: null, confirmed: false })`.
  `tipo_dispositivo` y `fecha_intervencion` no están en `PREFILL_KEYS` → conservan sus defaults.
  Lo escrito a mano (claves fuera de `prefilled`) no se toca.
- **Deshacer**: restaurar de `undo.values` **solo las claves que hoy están vacías** (no pisar lo que
  el perito escribió después); `onPrefillChange(undo.prefill)`; `undo = null`. Foco al botón que
  quede ("Empezar en blanco") o al primer campo de Destinatario.
- **Mantener**: `onPrefillChange({ ...prefill, confirmed: true })` → aviso y marcas desaparecen. Foco a
  `case-nro_referencia`.
- **Cambiar / Elegir causa de origen** → abre `PrefillSourceDialog`. Al elegir `cas`:
  `undo = { values: pick(form, PREFILL_KEYS), prefill }`; reemplazar **las 8 claves** con
  `prefillFromCase(cas)` (es una elección explícita: pisa también ediciones manuales de esas
  claves, y queda "Deshacer"); `onPrefillChange({ sourceId: cas.id, confirmed: false })`.
  **Nunca** se copian expediente, carátula, partes, titular, DNI, línea, IMEI, objeto ni ámbito.
- Todos los cambios pasan por `onChange` con un único objeto nuevo y limpian errores de las claves
  tocadas (`onClearError`).

**`PrefillSourceDialog`** (Prime `Dialog` con el pt global centrado, `header="Elegir causa de origen"`,
`draggable={false}`, `dismissableMask`; fuera del `<form>` en el árbol):
- `InputText` de búsqueda con `aria-label="Buscar por expediente, carátula o destinatario"`,
  `autoFocus` al abrir (`onShow`), `onKeyDown` Enter → `preventDefault()` (no hace nada).
- Fuente: `prefillSources(historyCases)` (excluye `caseId` actual si lo hubiera). Filtro: cada token
  de `normalizeCatalogKey(query)` debe estar en `normalizeCatalogKey(nro + " " + caratula + " " + nombre_tribunal)`,
  **o** `normalizeExpediente(nro)` incluye `normalizeExpediente(query)`. Query vacía → todas.
- Lista `<ul>` de `<button type="button">` (máx. 50 renderizados; si hay más, `<p>` "Mostrando 50 de N.
  Refiná la búsqueda."). Cada opción: expediente mono + `formatDate(created_at)` a la derecha; carátula
  en una línea `truncate`; destinatario `text-xs text-fx-text-2`. La actual lleva `aria-current="true"`
  y `Tag` "Actual". Contador `role="status"` "N causas" (cambia solo con la búsqueda).
- Vacío: "No hay causas que coincidan." Sin historial pericial: el botón que abre el diálogo no se muestra.
- Al cerrar, devolver el foco al botón que lo abrió (ref; Prime no lo garantiza en todos los casos).

### 6.11 Fecha de intervención

- `labelAside` del campo fecha (está libre: no tiene catálogo): dos `Button text size="small" type="button"`
  "Hoy" y "Ayer" → `set("fecha_intervencion", todayIso() | yesterdayIso())`. `aria-label` "Fecha de intervención: hoy" / "…: ayer".
- `hint` dinámico = `fechaEnPalabras(value)` (si es `null`, sin hint). Al ser `${id}-hint`, ya entra en
  `aria-describedby`; no es región viva.
- Fecha futura (`isFutureIsoDate(value)`): `<p id="case-fecha_intervencion-warn">` debajo del campo,
  `text-xs text-fx-warning` con `AlertTriangle` + "La fecha es posterior a hoy. ¿Es correcta?";
  se agrega su id a `aria-describedby`; **no** `aria-invalid`, **no** entra en `errors`,
  **no** bloquea `handleSaveCase`. Si además hay `errors.fecha_intervencion`, se muestra solo el error.
- El guard de Enter del `Calendar` (#52 D8) no cambia.

### 6.12 Expediente repetido

- Estado local `dupCheckedFor: string | null` en `CaseFormStep`; `onBlur` del input de
  `nro_referencia` → `setDupCheckedFor(form.nro_referencia)`.
- `dups = useMemo(() => dupCheckedFor !== null && normalizeExpediente(dupCheckedFor) === normalizeExpediente(form.nro_referencia)
  ? findSameExpediente(historyCases, form.nro_referencia, caseId) : [], [...])`. Al volver a tipear,
  la clave deja de coincidir y el aviso se oculta hasta el próximo blur (no avisa mientras se tipea).
- `DuplicateExpedienteNotice` debajo del campo (dentro de su celda de la grilla, `sm:col-span-2` si
  hace falta): `FxBanner tone="info" role="status"`:
  "Ya tenés un caso con este expediente ({formatDate(created_at)}, {manufacturer} {model})."; con
  más de uno: "Ya tenés {n} casos con este expediente (el último: …)". Acción "Usar los datos de esa
  causa" **solo** si `mode === "create"` y el caso tiene `schema_version >= 1` → mismo flujo que
  elegir origen (6.10). Cierre (X) → oculta hasta que cambie el valor.

### 6.13 Integrantes: "Separar en N"

En `IntegrantesField`:
- `onPaste` de cada fila: si la fila **estaba vacía** y `splitIntegrantes(text).length >= 2`, dejar que
  el pegado ocurra normal (queda todo en una fila, como hoy) y guardar
  `offer = { rowKey, parts, pasted: text }`.
- Mientras `offer` exista y el valor de esa fila siga siendo el pegado: debajo de la fila, una línea con
  `Button size="small" outlined type="button"` "Separar en {n} integrantes" + botón ícono `X`
  `aria-label="Descartar"`. La oferta es contenido estático (no región viva); el botón queda en el
  orden de tabulación justo después de la fila.
- Aceptar: reemplazar la fila por `n` filas (la primera conserva su `key`, las demás `newIntegranteRow`),
  respetando `MAX_INTEGRANTES` (si se pasa, se agregan las que entran y la línea dice "Se agregaron {k}
  (máximo 20)."); foco a la última fila agregada. Descartar o editar la fila → `offer = null`.
- Enter, subir/bajar/quitar y el tope no cambian.

### 6.14 Microinteracciones (D4 A)

- Ficha al montar: `motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]`
  (keyframe existente). `PrefillNotice`/`DuplicateExpedienteNotice`/oferta de integrantes:
  `motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]`.
- **Destello de un dato de la ficha al salir del campo** (nunca por tecla): `onBlur` del `<form>`
  (burbujea `focusout`): si `e.target.id` es `case-<clave>` y el valor cambió desde el `focus` de ese
  campo (ref con el valor al enfocar), `setFlashKey({ key, n: n + 1 })`. En la ficha, la fila de esa
  clave renderiza `<span key={n} aria-hidden className="pointer-events-none absolute inset-0 rounded-fx-sm bg-fx-accent-soft motion-safe:animate-[fx-summary-flash_var(--fx-dur-slow)_var(--fx-ease-out)_both] motion-reduce:hidden" />`.
  Keyframe nuevo en `globals.css` (junto a los demás): `@keyframes fx-summary-flash { from { opacity: 1 } to { opacity: 0 } }`.
- Chevron de secciones: el actual. Fundido 2D→3D: `transition-opacity duration-fx-base`.
- Ningún campo cambia de lugar mientras se tipea: los avisos nuevos aparecen **al salir del campo**
  (expediente) o en respuesta a una acción (precarga, pegado); la advertencia de fecha aparece al
  elegir fecha (el `Calendar` emite `onChange` con fecha completa, no por tecla).
- Solo `transform`/`opacity`. Nada de `transition-all`.

### 6.15 `< lg`: barra y panel

- `CaseSummaryBar` como **último hijo del `<form>` antes de `StepActions`**, `sticky bottom-0 z-10`,
  `-mx-5 px-5 py-2 border-t border-fx-border bg-fx-surface-1/95 backdrop-blur-sm` (o `bg-fx-surface-1`
  si `backdrop-blur` no rinde): expediente mono `truncate` (o "Sin expediente" en cursiva), `·`,
  "Faltan N" / "Completo" (texto + ícono), y `Button type="button" size="small" outlined` "Ver ficha"
  (`aria-haspopup="dialog"`, `aria-expanded`). Al ser `sticky` dentro del form, al llegar al final se
  apoya justo encima de `StepActions` y **nunca tapa el botón principal**.
- Panel: Prime `Dialog` con `position="right"` (variante "drawer" que ya existe en
  `client/src/lib/prime/pt/dialog.ts`, entra deslizando y solo fade con movimiento reducido),
  `header="Resumen de la causa"`, `dismissableMask`. Contiene `<CaseSummaryCard variant="drawer">`
  (sin indicador de conexión ni avisos de equipo, que están en la franja superior; con 2D siempre).
  `onGoToSection` en el panel: cerrar el panel y después `focusField` (el `setTimeout` de 60 ms
  existente alcanza; si no, enfocar en `onHide`).
- Elegido sobre una hoja inferior porque la variante drawer ya está probada con su pt y su
  movimiento reducido; no se crea un pt nuevo.

### 6.16 Rendimiento

- La ficha se deriva de `form` en render (`useMemo` para `summarySections`, `prefilled`, `dups`,
  `source`); **cero** `useEffect` que dependa de `form`.
- `onRefreshShot`, `onZoomShot`, `onGoToSection`: `useCallback`. `DeviceStage` con `memo`.
- `canUsePhone3D()` corre una sola vez por sesión.

## 7. Checklist atómico (`client/`)

### 7.0 Antes de escribir código
- [ ] Invocar **`ui-ux-pro-max` primero** y dejar en `progress/impl_frontend_formulario-causa-rediseno.md` el sistema de diseño de esta pantalla mapeado a `--fx-*` (jerarquía tipográfica de la ficha, densidad del formulario, tratamiento de "precargado", barra `< lg`, escena 3D: colores y pose).
- [ ] Invocar `senior-frontend`, `3d-web-experience` (aplicado de verdad: D2=B), `ui-styling`, `mblode-agent-skills-ui-animation`; anotar en el progress qué tomó de cada una.
- [ ] Leer `node_modules/next/dist/docs/01-app/02-guides/lazy-loading.md` (`next/dynamic` + `ssr: false` en Client Components) y `…/package-bundling.md`.

### 7.1 Dependencias
- [ ] `npm install --save-exact three@0.186.1 @react-three/fiber@9.8.1` y `npm install --save-dev --save-exact @types/three@0.186.0` en `client/`; sin `--legacy-peer-deps`.
- [ ] `client/package.json` y `client/package-lock.json` en el diff; `@react-three/drei` **no** aparece.

### 7.2 Helpers
- [ ] `lib/pericial.ts`: `CasePrefill`, `PREFILL_KEYS`, `PrefillKey`, `lastPericialCase`, `prefillSources`, `prefillFromCase`, `prefilledKeys`; `prefillFromLastCase` reescrita encima con la misma firma.
- [ ] `lib/pericial.ts`: `CASE_FORM_FOCUS_ORDER` con el orden nuevo (6.3).
- [ ] `lib/case-summary.ts` con las funciones de 6.3 y el comentario "tiene que coincidir con ReportValues.cs".
- [ ] Verificar a mano (consola del navegador o un script `node` descartable, no commiteado) los ejemplos de `normalizeExpediente`, `splitIntegrantes` y `fechaEnPalabras` de 6.3; pegar los resultados en el progress.

### 7.3 Piezas
- [ ] `hooks/useMediaQuery.ts`.
- [ ] `PhoneFrame.tsx` partido en `PhoneScreen` + `CaptureControls`, `PhoneFrame` con la misma firma; `DesignSystemShowcase` sin cambios y compilando.
- [ ] `FormField.tsx`: prop `labelBadge`.
- [ ] `case-form/ConnectionIndicator.tsx` e `ImeiMissingNotice.tsx` (extracción sin cambios de texto/roles).
- [ ] `case-form/CaseSummaryCard.tsx` (6.7) y `CaseSummaryBar.tsx` (6.15).
- [ ] `case-form/PrefillNotice.tsx` y `PrefillSourceDialog.tsx` (6.10).
- [ ] `case-form/DuplicateExpedienteNotice.tsx` (6.12).
- [ ] `IntegrantesField.tsx`: `prefilled` + oferta "Separar en N" (6.13).

### 7.4 3D
- [ ] `case-form/phone3d-support.ts`: `canUsePhone3D`, `disablePhone3D`, `readFxColor`, `readFxDurationMs` (sin `three`).
- [ ] `case-form/Phone3DBoundary.tsx`.
- [ ] `case-form/PhoneModel3D.tsx` según 6.8 D7 (demand, DPR `[1, 1.5]`, `flat`, `failIfMajorPerformanceCaveat`, geometría procedural, UV normalizadas, textura "cover" con `?tex=1` + `crossOrigin`, entrada única, colores de tokens, `webglcontextlost`, dispose).
- [ ] `case-form/DeviceStage.tsx` (`memo`, `next/dynamic` con `ssr: false`, elegibilidad de 6.8 D4, capas de 6.8 D6).
- [ ] `grep -rln "from \"three\"\|@react-three/fiber" client/src` → solo `PhoneModel3D.tsx`; `grep -rn "PhoneModel3D" client/src` → solo `DeviceStage.tsx` (y el propio archivo).

### 7.5 `CaseFormStep.tsx`
- [ ] Props nuevas: `historyCases: Case[]`, `caseId: string | null`, `prefill: CasePrefill | null`, `onPrefillChange: (p: CasePrefill | null) => void`.
- [ ] Secciones `causa`/`destinatario`/`partes`/`equipo` con los `FieldDef` de 6.5; `SECTION_OF`, `sectionOfErrorKey`, `sectionOfFieldId`, estado `open` y el foco post-reintento de catálogos (`"destinatario"`) actualizados.
- [ ] `FormSection` con "Faltan N" / check.
- [ ] Expediente `mono` + `translate="no"`; carátula multilínea (6.9).
- [ ] Fecha: "Hoy"/"Ayer", hint en palabras, advertencia futura (6.11).
- [ ] Marcas de precarga + `PrefillNotice` + diálogo + undo (6.10).
- [ ] Aviso de expediente repetido (6.12).
- [ ] Layout 6.4 con `isLg`; ficha `aside` sticky / barra + drawer; franja superior `< lg`; una sola instancia montada de `ConnectionIndicator`.
- [ ] `Lightbox` para la captura (`zoomSrc`), fuera del `<form>`.
- [ ] Destello al salir del campo (6.14) + keyframe `fx-summary-flash` en `globals.css`.
- [ ] Captura (`takeShot`, efecto de flancos, `stale`, `showCaptured`) **sin cambios de lógica**.
- [ ] `grep -n "<Button\|<button" client/src/components/CaseFormStep.tsx client/src/components/case-form/*.tsx client/src/components/form/IntegrantesField.tsx`: todo botón dentro del `<form>` tiene `type="button"` salvo el principal.

### 7.6 `page.tsx` (mínimo)
- [ ] Estado `casePrefill`; `startWizard` lo inicializa con `lastPericialCase`; `proceedResume` y `resetWizard` lo ponen en `null`.
- [ ] Props nuevas a `<CaseFormStep>` (6.2). Nada más.

### 7.7 Cierre
- [ ] `web-design-guidelines` sobre todos los archivos tocados; hallazgos y correcciones en el progress.
- [ ] Verificación de §8.1 y §8.2 con las cifras en el progress.
- [ ] No mover tarjetas del Project ni tocar `progress/sesiones/`. No commitear (lo hace el orquestador).

## 8. Verificación

### 8.1 Antes de declararse `done` (implementer-frontend)

```bash
cd /Users/joelmiguelserrudo/Projects/Factum/client
npx tsc --noEmit
rm -rf .next && npx next build          # tiene que terminar sin errores (Turbopack, default en Next 16)
```

### 8.2 Medición del bundle (cifras al progress)

```bash
cd /Users/joelmiguelserrudo/Projects/Factum/client
# (a) Chunks que contienen three: tamaño crudo y gzip
for f in $(grep -rl "WebGLRenderer" .next/static/chunks); do
  printf "%s raw=%s gz=%s\n" "$f" "$(wc -c < "$f")" "$(gzip -9c "$f" | wc -c)"; done
# Presupuesto: suma de gz <= 260 KB (D9).

# (b) Lo que carga /dashboard de entrada no contiene three
cp -r .next/static .next/standalone/.next/static && cp -r public .next/standalone/public 2>/dev/null
PORT=3100 node .next/standalone/server.js &  SRV=$!; sleep 3
curl -s http://localhost:3100/dashboard | grep -o '/_next/static/chunks/[^"]*\.js' | sort -u > /tmp/dash-chunks.txt
while read p; do grep -l "WebGLRenderer" ".next${p#/_next}" 2>/dev/null; done < /tmp/dash-chunks.txt   # tiene que no imprimir nada
kill $SRV
# Opcional: npx next experimental-analyze --output  → .next/diagnostics/analyze (filtrar por /dashboard, client).
```

(Si `/dashboard` redirige sin sesión y el HTML no trae los chunks, usar en su lugar la prueba
manual 8.3-15 en DevTools → Network. Usar el directorio scratch del agente en vez de `/tmp` si el
entorno lo pide.)

### 8.3 Prueba manual (para el usuario; el implementador la recorre lo que pueda)

Con Tatana abierto, un Android conectado y un caso nuevo:

1. **≥ 1024 px:** formulario a la izquierda, ficha a la derecha `sticky`; al tipear expediente, carátula y partes, la ficha se actualiza al instante; los vacíos dicen "Sin …" en cursiva.
2. "Trámite ante Sala II de X, con la integración de A y B" y "Propone: N – P, M.P. M" coinciden letra por letra con el informe generado de esa causa.
3. Índice: con faltantes en dos secciones muestra "Faltan N" en cada una; clic → foco en el primer obligatorio vacío (despliega si estaba plegada). El lector anuncia "Faltan N obligatorios" solo cuando cambia N.
4. **3D:** el teléfono aparece sin demorar el formulario (primero se ve el 2D y funde al 3D), gira una sola vez y queda quieto; la pantalla muestra la captura; "Actualizar captura" la cambia; desconectar → "Captura anterior" atenuada; clic en el teléfono abre la captura ampliada.
5. DevTools → Rendering → **Frame Rendering Stats**: después de la entrada no hay frames nuevos mientras se tipea.
6. **Fallback 2D:** con "reducir movimiento" del SO activado, con `chrome://flags` WebGL deshabilitado o en `chrome --disable-gpu`, el equipo se ve con el marco 2D de siempre y el chunk de three **no** aparece en Network.
7. Pestaña oculta y vuelta: sin errores. Cambiar tema claro/oscuro: el teléfono toma los colores del tema.
8. Pasar al paso 3: en DevTools → Memory / consola, no quedan contextos WebGL vivos (no hay advertencia "Too many active WebGL contexts" tras ir y volver 10 veces entre pasos 2 y 3); el paso 3 no tiene 3D.
9. **Precarga:** con historial, aparece "Datos tomados de la causa …"; los 8 campos reutilizables tienen el ícono de historial (y el lector dice "tomado de la causa anterior"); editar uno → su marca desaparece. "Empezar en blanco" vacía solo esos, deja "Teléfono celular" y la fecha de hoy y lo escrito a mano; "Deshacer" lo vuelve. "Cambiar causa de origen" → buscar por expediente, carátula o destinatario → elegir → se reemplazan solo los reutilizables. "Mantener" oculta aviso y marcas.
10. Sin historial pericial: no hay aviso. Editando un caso existente (y en un caso legacy): no hay aviso ni marcas.
11. **Expediente repetido:** escribir uno existente y salir del campo → aviso con fecha y equipo; "Usar los datos de esa causa" precarga; mientras se tipea no aparece. `" 01234 / 2026"` coincide con `1234/2026`.
12. **Fecha:** "Hoy"/"Ayer" funcionan; debajo se lee "lunes 12 de octubre de 2026"; una fecha futura muestra la advertencia y "Crear caso y continuar" igual crea.
13. **Integrantes:** pegar "Dr. Juan Pérez, Dra. Ana Gómez y Dr. Luis Díaz" en una fila vacía → "Separar en 3 integrantes" → tres filas y vista previa correcta; sin aceptar, queda en una fila.
14. **Carátula:** Enter envía el formulario; pegar un texto con saltos de línea lo deja en una línea; contador n/500.
15. **< 1024 px** (320, 375, 768): formulario a ancho completo, conexión e IMEI arriba sin desplegar nada, barra inferior "… · Faltan N · Ver ficha" que no tapa el botón principal; el panel abre la ficha (2D). Sin scroll horizontal en 320/375/768/1024. Network: el chunk de three no se pide.
16. **No regresión:** cuerpo de `POST /api/cases` (DevTools → Network → Payload) idéntico al de `develop` para los mismos valores; mensajes de validación, foco al primer error en el **orden nuevo**, catálogos con "Administrar", perfil del perito, Enter que envía, guard de Enter del calendario y autocompletado, indicador de conexión/aviso/captura de la #52.
17. Tema claro y oscuro; con un color de marca distinto configurado.
18. Teclado solo: recorrido completo formulario → ficha; todos los controles con foco visible.

## 9. Skills obligatorias del `implementer-frontend` (recordatorio)

1. **`ui-ux-pro-max` ANTES de escribir el JSX final** (sistema de diseño de esta pantalla mapeado a `--fx-*`, constancia en el progress).
2. **`senior-frontend`**: estado derivado sin efectos por tecla, `memo` de `DeviceStage`, componentes chicos de `case-form/`.
3. **`3d-web-experience`**: aplicado a la escena R3F (presupuesto, demand, DPR, fallback, dispose).
4. **`ui-styling`**: Tailwind + tokens, pt de Prime para `Dialog`/`InputTextarea`/botones nuevos.
5. **`mblode-agent-skills-ui-animation`**: solo transform/opacidad, `--fx-dur-*`/`--fx-ease-*`, variante reducida para todo.
6. **`web-design-guidelines`**: autochequeo final de los archivos tocados.

## 10. Fuera de alcance (recordatorio)

Backend/Tatana/`agent-ui`; cambios de campos, obligatorios o mensajes; máscara del expediente;
generador de carátula; diálogo de revisión; autoguardado; `localStorage`; `CaptureStep` y pasos
1, 3–6; diálogo "Mi perfil de perito"; plantilla del informe; drei, OrbitControls, sombras,
mapas de entorno, modelos `.glb`.

## 11. Preguntas para el usuario

**P1 — ¿El teléfono 3D se puede girar arrastrando con el mouse?**
- **A (recomendada, default de esta SDD): no.** Pose fija con una única entrada animada; el
  teléfono es decorativo (`aria-hidden`) y el clic abre la captura ampliada. *Por qué:* girar no
  aporta información (la pantalla es lo único que importa), exige `OrbitControls` o manejo de
  puntero propio, compite con el clic para ampliar, no es accesible por teclado y obliga a
  redibujar continuamente mientras se arrastra en PCs modestas.
- B: arrastre horizontal acotado (±25°) con vuelta suave a la pose, sin drei (handlers propios),
  y el clic para ampliar pasa a un botón aparte.

El implementador avanza con **A**; si el usuario elige B antes de implementar, se agrega como
paso extra del checklist 7.4.

**Respuesta del usuario (2026-10-13): B.** Se implementa el arrastre horizontal acotado a ±25°
con vuelta suave a la pose, con handlers de puntero propios (sin drei ni `OrbitControls`):
- Paso extra del checklist 7.4: rotación Y limitada a ±25° mientras se arrastra (pointer
  capture, solo en `lg+`), vuelta a la pose con easing al soltar; `invalidate()` solo durante
  el arrastre y la vuelta (frameloop "demand" se mantiene); sin rotación con
  `prefers-reduced-motion` (en ese caso ya se muestra el fallback 2D).
- El clic para ampliar la captura pasa a un **botón aparte** visible y accesible por teclado
  ("Ver captura"); el canvas sigue `aria-hidden` y un arrastre nunca dispara la ampliación.
- Esto reemplaza la línea "Sin `OrbitControls` ni handlers de puntero" de §504 en lo que
  respecta a los handlers (sigue sin `OrbitControls`).
