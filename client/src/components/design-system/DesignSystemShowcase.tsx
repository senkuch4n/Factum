"use client";

import { useRef, useState } from "react";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { Dialog } from "primereact/dialog";
import { Menu } from "primereact/menu";
import type { MenuItem } from "primereact/menuitem";
import { Toast } from "primereact/toast";
import { Message } from "primereact/message";
import { Tag } from "primereact/tag";
import { SelectButton } from "primereact/selectbutton";
import { Calendar } from "primereact/calendar";
import { DataTable } from "primereact/datatable";
import { Column } from "primereact/column";
import { Paginator } from "primereact/paginator";
import { InputTextarea } from "primereact/inputtextarea";
import { Dropdown } from "primereact/dropdown";
import { ProgressBar } from "primereact/progressbar";
import { Checkbox } from "primereact/checkbox";
import { toast as sonner } from "sonner";
import {
  AlertCircle, CalendarRange, Camera, Check, CheckCircle2, ChevronDown, ChevronRight, ChevronUp, ChevronsUpDown, CircleDashed, Clock,
  Download, FileText, Hash, HelpCircle, LayoutGrid, List, Loader2, MoreHorizontal, Plus, Settings, Smartphone, Table, Trash2,
} from "lucide-react";
import { AppNavbar } from "@/components/shell/AppNavbar";
import { SiteFooter } from "@/components/shell/SiteFooter";
import { usePublicConfig } from "@/hooks/usePublicConfig";
import { Tip } from "@/components/ui/tooltip";
import { FxPassword } from "@/components/form/FxPassword";
import { FxTip } from "@/components/overlay/FxTip";
import { FxBanner } from "@/components/feedback/FxBanner";
import { ConfirmDialog } from "@/components/ui/alert-dialog";
import { useFxToast } from "@/components/shell/FxToastProvider";
import { StepIndicator, StepIndicatorCompact } from "@/components/StepIndicator";
import { PhoneFrame, PhoneShell } from "@/components/PhoneFrame";
import { CopyButton } from "@/components/ui/CopyButton";
import { FxMediaDialog } from "@/components/overlay/FxMediaDialog";
import { MEDIA_ACTIONS } from "@/components/capture/MediaErrorGuide";

/* ── Datos de ejemplo (ficticios) ──────────────────────────────────── */

const DEMO_USER = { name: "Oficial de Prueba", sigla: "DEMO", dni: "00.000.000" };

const SECTIONS = [
  { id: "paleta", label: "Paleta" },
  { id: "tipografia", label: "Tipografía" },
  { id: "forma", label: "Radios, sombras y tarjetas" },
  { id: "prime", label: "PrimeReact" },
  { id: "sonner", label: "Notificaciones y tooltips" },
  { id: "legacy", label: "Convivencia legacy" },
];

interface Swatch {
  token: string;
  note?: string;
}

const COLOR_GROUPS: { title: string; swatches: Swatch[] }[] = [
  {
    title: "Superficies",
    swatches: [
      { token: "--fx-bg", note: "fondo de página" },
      { token: "--fx-surface-1", note: "tarjetas, paneles" },
      { token: "--fx-surface-2", note: "inputs, menús, diálogos" },
      { token: "--fx-surface-3", note: "hover, tooltip" },
      { token: "--fx-nav-bg", note: "navbar" },
      { token: "--fx-overlay", note: "máscara de Dialog" },
    ],
  },
  {
    title: "Bordes y texto",
    swatches: [
      { token: "--fx-border", note: "solo decorativo" },
      { token: "--fx-border-strong", note: "límite de controles" },
      { token: "--fx-text", note: "primario" },
      { token: "--fx-text-2", note: "secundario" },
      { token: "--fx-text-3", note: "terciario, placeholder" },
      { token: "--fx-text-disabled", note: "deshabilitado" },
    ],
  },
  {
    title: "Acento de marca",
    swatches: [
      { token: "--fx-accent", note: "CTA, activo" },
      { token: "--fx-accent-hover" },
      { token: "--fx-accent-active" },
      { token: "--fx-on-accent", note: "texto sobre acento" },
      { token: "--fx-accent-text", note: "acento como texto" },
      { token: "--fx-accent-soft", note: "ítem activo" },
      { token: "--fx-focus", note: "anillo de foco" },
    ],
  },
  {
    title: "Estados",
    swatches: [
      { token: "--fx-success" },
      { token: "--fx-success-soft" },
      { token: "--fx-warning" },
      { token: "--fx-warning-soft" },
      { token: "--fx-danger" },
      { token: "--fx-danger-fill" },
      { token: "--fx-danger-soft" },
      { token: "--fx-info" },
      { token: "--fx-info-soft" },
    ],
  },
];

/* Ratios de la tabla T4 de la SDD (texto estático, no se calculan). */
const CONTRAST_PAIRS: { fg: string; bg: string; label: string; dark: string; light: string }[] = [
  { fg: "--fx-text", bg: "--fx-bg", label: "text / bg", dark: "17.91", light: "17.76" },
  { fg: "--fx-text-2", bg: "--fx-bg", label: "text-2 / bg", dark: "11.37", light: "9.19" },
  { fg: "--fx-text-3", bg: "--fx-surface-1", label: "text-3 / surface-1", dark: "6.53", light: "5.93" },
  { fg: "--fx-on-accent", bg: "--fx-accent", label: "on-accent / accent", dark: "10.56", light: "6.17" },
  { fg: "--fx-accent-text", bg: "--fx-accent-soft", label: "accent-text / accent-soft", dark: "9.50", light: "5.76" },
  { fg: "--fx-on-danger", bg: "--fx-danger-fill", label: "on-danger / danger-fill", dark: "5.19", light: "6.51" },
  { fg: "--fx-success", bg: "--fx-success-soft", label: "success / success-soft", dark: "8.14", light: "5.71" },
  { fg: "--fx-warning", bg: "--fx-warning-soft", label: "warning / warning-soft", dark: "8.48", light: "5.24" },
  { fg: "--fx-danger", bg: "--fx-danger-soft", label: "danger / danger-soft", dark: "6.21", light: "5.44" },
  { fg: "--fx-info", bg: "--fx-info-soft", label: "info / info-soft", dark: "7.48", light: "5.12" },
];

const TYPE_SCALE = [
  { cls: "text-fx-display", label: "Display", sample: "Evidencia íntegra" },
  { cls: "text-fx-h1", label: "H1", sample: "Inspecciones del día" },
  { cls: "text-fx-h2", label: "H2", sample: "Datos de la causa" },
  { cls: "text-fx-h3", label: "H3", sample: "Dispositivo conectado" },
  { cls: "text-fx-body", label: "Body", sample: "El informe se firma con el hash de cada archivo capturado." },
  { cls: "text-fx-body-sm", label: "Body sm", sample: "Última sincronización hace 3 minutos." },
  { cls: "text-fx-label uppercase", label: "Label", sample: "Estado del agente" },
];

const RADII = ["sm", "md", "lg", "xl"] as const;
const RADIUS_CLASS: Record<(typeof RADII)[number], string> = {
  sm: "rounded-fx-sm",
  md: "rounded-fx-md",
  lg: "rounded-fx-lg",
  xl: "rounded-fx-xl",
};
const SHADOWS = [
  { cls: "shadow-fx-1", label: "shadow-1" },
  { cls: "shadow-fx-2", label: "shadow-2" },
  { cls: "shadow-fx-3", label: "shadow-3" },
];

const DEMO_CARDS = [
  { title: "Samsung Galaxy A54", meta: "Android 14 · 32 capturas", icon: Smartphone },
  { title: "Causa 1234/26", meta: "Informe Word + ZIP de evidencia", icon: FileText },
  { title: "Configuración", meta: "Agente local en :8765", icon: Settings },
];

/* ── Datos ficticios de las demos del historial (sin API) ───────────── */

type DemoView = "list" | "table" | "grid";

const DEMO_VIEWS = [
  { value: "list" as DemoView, label: "Lista", Icon: List },
  { value: "table" as DemoView, label: "Tabla", Icon: Table },
  { value: "grid" as DemoView, label: "Cuadrícula", Icon: LayoutGrid },
];

interface DemoRow { id: string; causa: string; caratula: string; fecha: string; estado: string }

const DEMO_ROWS: DemoRow[] = [
  { id: "1", causa: "12/26", caratula: "Pérez c/ Gómez s/ daños", fecha: "2026-09-12", estado: "Completado" },
  { id: "2", causa: "07/26", caratula: "Ruiz s/ amenazas", fecha: "2026-08-03", estado: "Borrador" },
  { id: "3", causa: "31/25", caratula: "Ledesma c/ Sosa s/ estafa", fecha: "2026-09-27", estado: "Error" },
];

/* ── Piezas de layout ─────────────────────────────────────────────── */

const DEMO_TRATAMIENTO = [
  { label: "El suscripto", value: "suscripto" },
  { label: "La suscripta", value: "suscripta" },
];
const DEMO_CAMERAS = ["Cámara integrada", "Cámara USB externa"];
const DEMO_STEPS = [
  { id: 1, label: "Dispositivo", sublabel: "Seleccionando dispositivo" },
  { id: 2, label: "Causa", sublabel: "Datos de la causa" },
  { id: 3, label: "Captura", sublabel: "Capturando" },
  { id: 4, label: "Informe", sublabel: "Redactando" },
  { id: 5, label: "Generar", sublabel: "Creando informe" },
  { id: 6, label: "Listo", sublabel: "Resumiendo" },
];

function Section({ id, title, intro, children }: { id: string; title: string; intro?: string; children: React.ReactNode }) {
  return (
    <section id={id} aria-labelledby={`${id}-title`} className="scroll-mt-20 space-y-6 border-t border-fx-border pt-10">
      <div className="space-y-2">
        <h2 id={`${id}-title`} className="text-fx-h2 text-fx-text">{title}</h2>
        {intro && <p className="max-w-[70ch] text-fx-body-sm text-fx-text-2">{intro}</p>}
      </div>
      {children}
    </section>
  );
}

function Subsection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="space-y-3">
      <h3 className="text-fx-label uppercase text-fx-text-3">{title}</h3>
      {children}
    </div>
  );
}

/* ── Showcase ─────────────────────────────────────────────────────── */

export function DesignSystemShowcase() {
  const toastRef = useRef<Toast>(null);
  const menuRef = useRef<Menu>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [refValue, setRefValue] = useState("12/26");
  const [passValue, setPassValue] = useState("");
  const [passInvalidValue, setPassInvalidValue] = useState("");
  const { organizationName, organizationLogoSrc } = usePublicConfig();

  const refInvalid = refValue.trim() === "";

  // Demos del historial (T15 de rediseno-dashboard-historial).
  const fxToast = useFxToast();
  const [demoView, setDemoView] = useState<DemoView>("list");
  const [demoRange, setDemoRange] = useState<(Date | null)[] | null>(null);
  const [demoSort, setDemoSort] = useState<{ field: keyof DemoRow; order: 1 | -1 }>({ field: "fecha", order: -1 });
  const [demoFirst, setDemoFirst] = useState(0);
  const [confirmTone, setConfirmTone] = useState<"destructive" | "neutral" | null>(null);

  // Demos del wizard (T12 de rediseno-dashboard-wizard).
  const [demoTratamiento, setDemoTratamiento] = useState<string>("suscripto");
  const [demoInvalidOpt, setDemoInvalidOpt] = useState<string | null>(null);
  const [demoStep, setDemoStep] = useState(3);
  const [demoPhoneLoading, setDemoPhoneLoading] = useState(false);

  // Demos de la captura (T13 de rediseno-dashboard-captura).
  const [demoChecked, setDemoChecked] = useState(true);
  const [demoMic, setDemoMic] = useState(false);
  const [demoMediaOpen, setDemoMediaOpen] = useState(false);
  const demoSorted = [...DEMO_ROWS].sort((a, b) =>
    a[demoSort.field] < b[demoSort.field] ? -demoSort.order : a[demoSort.field] > b[demoSort.field] ? demoSort.order : 0,
  );

  const demoMenu: MenuItem[] = [
    { label: "Descargar informe", icon: <Download className="h-4 w-4" aria-hidden="true" /> },
    { label: "Nueva inspección", icon: <Plus className="h-4 w-4" aria-hidden="true" /> },
    { label: "Ítem deshabilitado", disabled: true },
    { separator: true },
    {
      label: "Eliminar borrador",
      icon: <Trash2 className="h-4 w-4" aria-hidden="true" />,
      className: "text-fx-danger",
    },
  ];

  const startLoading = () => {
    setLoading(true);
    window.setTimeout(() => setLoading(false), 2000);
  };

  return (
    <div className="min-h-screen bg-fx-bg text-fx-text">
      {/* 1. Navbar real */}
      <AppNavbar
        showThemeToggle
        brandHref="/design-system"
        user={DEMO_USER}
        onLogout={() => sonner.info("Cierre de sesión de ejemplo", { description: "En el showcase no se cierra la sesión real." })}
        center={
          <nav aria-label="Ruta" className="flex min-w-0 items-center gap-1.5 text-sm">
            <span className="truncate font-medium text-fx-text-3">Inspecciones</span>
            <ChevronRight aria-hidden="true" className="h-3.5 w-3.5 shrink-0 text-fx-text-3" />
            <span aria-current="page" className="truncate font-medium text-fx-text">Sistema de diseño</span>
          </nav>
        }
        actions={
          <Tip label="Acción de ejemplo">
            <button
              type="button"
              aria-label="Nueva inspección (ejemplo)"
              className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-fx-md bg-fx-accent text-fx-on-accent transition-colors duration-fx-fast ease-fx hover:bg-fx-accent-hover fx-focus-ring"
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
            </button>
          </Tip>
        }
      />

      <main className="mx-auto max-w-[1200px] space-y-12 px-4 pb-24 pt-10 sm:px-6">
        <header className="space-y-4">
          <p className="text-fx-label uppercase text-fx-accent-text">Solo desarrollo</p>
          <h1 className="text-fx-h1 text-fx-text">Sistema de diseño de Factum</h1>
          <p className="max-w-[70ch] text-fx-body text-fx-text-2">
            Tokens <code className="font-mono text-fx-body-sm">--fx-*</code>, tipografía y componentes PrimeReact
            tematizados por pass-through. Cambiá el tema con el botón de la navbar para validar los dos modos.
          </p>
          <nav aria-label="Secciones del sistema de diseño">
            <ul className="flex flex-wrap gap-2">
              {SECTIONS.map((s) => (
                <li key={s.id}>
                  <a
                    href={`#${s.id}`}
                    className="inline-flex rounded-fx-pill border border-fx-border bg-fx-surface-1 px-3 py-1 text-fx-body-sm text-fx-text-2 no-underline transition-colors duration-fx-fast ease-fx hover:border-fx-border-strong hover:text-fx-text fx-focus-ring"
                  >
                    {s.label}
                  </a>
                </li>
              ))}
            </ul>
          </nav>
        </header>

        {/* 2. Paleta */}
        <Section
          id="paleta"
          title="Paleta"
          intro="Cada swatch lee el token en vivo: al cambiar de modo cambian todos a la vez. El acento (lima) y el éxito (menta) son tonos distintos y el éxito siempre va con ícono y texto."
        >
          {COLOR_GROUPS.map((group) => (
            <Subsection key={group.title} title={group.title}>
              <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
                {group.swatches.map((s) => (
                  <li key={s.token} className="fx-card overflow-hidden">
                    <div className="h-14 border-b border-fx-border" style={{ background: `var(${s.token})` }} />
                    <div className="space-y-0.5 p-3">
                      <p className="truncate font-mono text-xs text-fx-text">{s.token}</p>
                      {s.note && <p className="text-xs text-fx-text-3">{s.note}</p>}
                    </div>
                  </li>
                ))}
              </ul>
            </Subsection>
          ))}

          <Subsection title="Pares de contraste (WCAG 2.1, ratio oscuro · claro)">
            <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {CONTRAST_PAIRS.map((p) => (
                <li
                  key={p.label}
                  className="flex items-center justify-between gap-3 rounded-fx-md border border-fx-border px-4 py-3"
                  style={{ background: `var(${p.bg})`, color: `var(${p.fg})` }}
                >
                  <span className="font-mono text-xs">{p.label}</span>
                  <span className="text-xs font-semibold tabular-nums">
                    {p.dark} · {p.light}
                  </span>
                </li>
              ))}
            </ul>
          </Subsection>
        </Section>

        {/* 3. Tipografía */}
        <Section id="tipografia" title="Tipografía" intro="Inter para la interfaz e IBM Plex Mono para datos técnicos (hashes, IMEI, rutas).">
          <ul className="space-y-5">
            {TYPE_SCALE.map((t) => (
              <li key={t.cls} className="grid grid-cols-1 items-baseline gap-2 sm:grid-cols-[9rem_minmax(0,1fr)]">
                <span className="font-mono text-xs text-fx-text-3">{t.cls.split(" ")[0]}</span>
                <span className={`${t.cls} min-w-0 text-fx-text`}>{t.sample}</span>
              </li>
            ))}
            <li className="grid grid-cols-1 items-baseline gap-2 sm:grid-cols-[9rem_minmax(0,1fr)]">
              <span className="font-mono text-xs text-fx-text-3">font-mono</span>
              <span className="min-w-0 break-all font-mono text-fx-body-sm text-fx-text-2">
                SHA-256 9f2c1e7a4b3d0c8e6f5a2b1c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4a3b2c1d0e
              </span>
            </li>
          </ul>
        </Section>

        {/* 4. Radios, sombras y tarjetas */}
        <Section
          id="forma"
          title="Radios, sombras y tarjetas"
          intro="Las tarjetas interactivas se elevan al pasar el puntero o al enfocarlas con el teclado. Con movimiento reducido solo cambian el borde y la sombra."
        >
          <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
            <Subsection title="Radios">
              <ul className="flex flex-wrap gap-4">
                {RADII.map((r) => (
                  <li key={r} className="flex flex-col items-center gap-2">
                    <div className={`h-16 w-16 border border-fx-border-strong bg-fx-surface-2 ${RADIUS_CLASS[r]}`} />
                    <span className="font-mono text-xs text-fx-text-3">radius-{r}</span>
                  </li>
                ))}
              </ul>
            </Subsection>
            <Subsection title="Elevación">
              <ul className="flex flex-wrap gap-4">
                {SHADOWS.map((s) => (
                  <li key={s.cls} className="flex flex-col items-center gap-2">
                    <div className={`h-16 w-16 rounded-fx-lg bg-fx-surface-1 ${s.cls}`} />
                    <span className="font-mono text-xs text-fx-text-3">{s.label}</span>
                  </li>
                ))}
              </ul>
            </Subsection>
          </div>

          <Subsection title="Tarjetas interactivas (.fx-card .fx-card-interactive)">
            <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {DEMO_CARDS.map(({ title, meta, icon: Icon }) => (
                <li key={title}>
                  <div
                    role="button"
                    tabIndex={0}
                    aria-label={`${title}: ${meta}`}
                    onClick={() => sonner(`Abriste "${title}"`)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        sonner(`Abriste "${title}"`);
                      }
                    }}
                    className="fx-card fx-card-interactive flex items-center gap-4 p-5"
                  >
                    <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-fx-md bg-fx-accent-soft text-fx-accent-text">
                      <Icon className="h-5 w-5" aria-hidden="true" />
                    </span>
                    <span className="min-w-0">
                      <span className="block truncate text-fx-body-sm font-semibold text-fx-text">{title}</span>
                      <span className="block truncate text-xs text-fx-text-3">{meta}</span>
                    </span>
                  </div>
                </li>
              ))}
            </ul>
          </Subsection>
        </Section>

        {/* 5. PrimeReact */}
        <Section
          id="prime"
          title="PrimeReact"
          intro="Componentes de PrimeReact 10 en modo unstyled, vestidos por el preset pass-through de src/lib/prime/pt."
        >
          <Subsection title="Button: variantes">
            <div className="flex flex-wrap items-center gap-3">
              <Button label="Primario" />
              <Button label="Secundario" severity="secondary" />
              <Button label="Contorno" outlined />
              <Button label="Texto" text />
              <Button label="Link" link />
              <Button label="Eliminar" severity="danger" icon={<Trash2 className="h-4 w-4" aria-hidden="true" />} />
              <Button label="Eliminar" severity="danger" outlined />
            </div>
          </Subsection>
          <Subsection title="Button: estados y tamaños">
            <div className="flex flex-wrap items-center gap-3">
              <Button label="Chico" size="small" />
              <Button label="Normal" />
              <Button label="Grande" size="large" />
              <Button label="Deshabilitado" disabled />
              <Button label="Secundario deshabilitado" severity="secondary" disabled />
              <Button label={loading ? "Generando…" : "Generar informe"} loading={loading} onClick={startLoading} />
              <Button aria-label="Agregar" icon={<Plus className="h-4 w-4" aria-hidden="true" />} />
              <Button aria-label="Más opciones" severity="secondary" icon={<MoreHorizontal className="h-4 w-4" aria-hidden="true" />} />
            </div>
          </Subsection>

          <Subsection title="InputText">
            <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
              <div className="space-y-1.5">
                <label htmlFor="ds-nombre" className="block text-fx-body-sm font-medium text-fx-text">Titular del dispositivo</label>
                <InputText id="ds-nombre" placeholder="Ej.: Juana Pérez" autoComplete="off" />
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ds-ref" className="block text-fx-body-sm font-medium text-fx-text">Nro. de referencia</label>
                <InputText
                  id="ds-ref"
                  value={refValue}
                  onChange={(e) => setRefValue(e.target.value)}
                  invalid={refInvalid}
                  aria-invalid={refInvalid}
                  aria-describedby="ds-ref-msg"
                  autoComplete="off"
                />
                <p id="ds-ref-msg" className={refInvalid ? "text-xs text-fx-danger" : "text-xs text-fx-text-3"}>
                  {refInvalid ? "Ingresá el número de referencia." : "Borrá el valor para ver el estado de error."}
                </p>
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ds-invalid" className="block text-fx-body-sm font-medium text-fx-text">IMEI</label>
                <InputText id="ds-invalid" defaultValue="35-209900" invalid aria-invalid aria-describedby="ds-invalid-msg" autoComplete="off" />
                <p id="ds-invalid-msg" className="text-xs text-fx-danger">El IMEI tiene que tener 15 dígitos.</p>
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ds-disabled" className="block text-fx-body-sm font-medium text-fx-text-disabled">Tribunal (deshabilitado)</label>
                <InputText id="ds-disabled" defaultValue="Tribunal de ejemplo" disabled />
              </div>
            </div>
          </Subsection>

          <Subsection title="Password (FxPassword)">
            <p className="max-w-2xl text-fx-body-sm text-fx-text-2">
              Siempre a través de <code>FxPassword</code>: el control de mostrar/ocultar es un botón con
              nombre en español (&laquo;Mostrar contraseña&raquo; / &laquo;Ocultar contraseña&raquo;) que conserva el foco.
            </p>
            <div className="grid grid-cols-1 gap-5 sm:grid-cols-3">
              <div className="space-y-1.5">
                <label htmlFor="ds-pass" className="block text-fx-body-sm font-medium text-fx-text">Contraseña</label>
                <FxPassword inputId="ds-pass" autoComplete="off" value={passValue} onChange={(e) => setPassValue(e.target.value)} />
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ds-pass-invalid" className="block text-fx-body-sm font-medium text-fx-text">Contraseña (inválida)</label>
                <FxPassword
                  inputId="ds-pass-invalid"
                  autoComplete="off"
                  value={passInvalidValue}
                  onChange={(e) => setPassInvalidValue(e.target.value)}
                  invalid
                  aria-invalid
                  aria-describedby="ds-pass-invalid-msg"
                 
                />
                <p id="ds-pass-invalid-msg" className="text-xs text-fx-danger">Ingresá tu contraseña.</p>
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ds-pass-disabled" className="block text-fx-body-sm font-medium text-fx-text-disabled">Contraseña (deshabilitada)</label>
                <FxPassword inputId="ds-pass-disabled" autoComplete="off" value="secreto" readOnly disabled />
              </div>
            </div>
          </Subsection>

          <Subsection title="Message">
            <div className="grid max-w-2xl grid-cols-1 gap-3">
              <Message role="note" severity="error" text="DNI, usuario o contraseña incorrectos." />
              <Message role="note" severity="warn" text="Tatana sin conexión: abrí el agente para adquirir." />
              <Message role="note" severity="success" text="Informe generado y firmado." />
              <Message role="note" severity="info" text="La sesión vence a las 8 horas de inactividad." />
            </div>
          </Subsection>

          <Subsection title="Campo con ícono (patrón del login)">
            <div className="max-w-sm space-y-1.5">
              <label htmlFor="ds-dni" className="block text-fx-body-sm font-semibold text-fx-text-2">DNI</label>
              <div className="group relative">
                <Hash
                  className="pointer-events-none absolute left-3.5 top-1/2 z-[1] h-4 w-4 -translate-y-1/2 text-fx-text-3 transition-colors duration-fx-fast ease-fx group-focus-within:text-fx-accent-text"
                  aria-hidden="true"
                />
                <InputText id="ds-dni" inputMode="numeric" autoComplete="off" placeholder="12345678" className="h-12 !pl-10" />
              </div>
            </div>
          </Subsection>

          <Subsection title="Dialog, Menu y Toast">
            <div className="flex flex-wrap items-center gap-3">
              <Button label="Abrir diálogo" severity="secondary" onClick={() => setDialogOpen(true)} />
              <Button
                label="Abrir menú"
                severity="secondary"
                aria-haspopup="menu"
                aria-controls="ds-menu_list"
                onClick={(e) => menuRef.current?.toggle(e)}
              />
              <Menu ref={menuRef} id="ds-menu" model={demoMenu} popup pt={{ menu: { "aria-label": "Acciones de ejemplo" } }} />
              <Button
                label="Toast éxito"
                text
                onClick={() => toastRef.current?.show({ severity: "success", summary: "Informe generado", detail: "El informe y el ZIP de evidencia quedaron guardados." })}
              />
              <Button
                label="Toast info"
                text
                onClick={() => toastRef.current?.show({ severity: "info", summary: "Agente conectado", detail: "Tatana responde en localhost:8765." })}
              />
              <Button
                label="Toast advertencia"
                text
                onClick={() => toastRef.current?.show({ severity: "warn", summary: "Batería baja", detail: "El celular tiene menos del 15 % de carga." })}
              />
              <Button
                label="Toast error"
                text
                severity="danger"
                onClick={() => toastRef.current?.show({ severity: "error", summary: "No se pudo capturar", detail: "Revisá el cable USB y volvé a intentar." })}
              />
            </div>
            <Toast ref={toastRef} position="top-right" />
            <Dialog
              header="Confirmar salida"
              visible={dialogOpen}
              onHide={() => setDialogOpen(false)}
              footer={
                <>
                  <Button label="Seguir acá" severity="secondary" onClick={() => setDialogOpen(false)} />
                  <Button label="Salir sin guardar" severity="danger" onClick={() => setDialogOpen(false)} />
                </>
              }
            >
              <p className="m-0">
                Los archivos capturados no se guardan en el servidor. Si salís ahora, se pierden.
              </p>
              <div className="mt-4 space-y-1.5">
                <label htmlFor="ds-dialog-motivo" className="block text-fx-body-sm font-medium text-fx-text">Motivo (opcional)</label>
                <InputText id="ds-dialog-motivo" placeholder="Tab y Shift+Tab quedan dentro del diálogo" />
              </div>
            </Dialog>
          </Subsection>

          <Subsection title="Tag">
            <div className="flex flex-wrap items-center gap-2">
              <Tag severity="success" value="Completado" icon={<CheckCircle2 className="h-3 w-3" aria-hidden="true" />} />
              <Tag severity="info" value="Generando…" icon={<Loader2 className="h-3 w-3" aria-hidden="true" />} />
              <Tag severity="warning" value="En espera" icon={<Clock className="h-3 w-3" aria-hidden="true" />} />
              <Tag severity="danger" value="Error" icon={<AlertCircle className="h-3 w-3" aria-hidden="true" />} />
              <Tag severity="secondary" value="Borrador" icon={<CircleDashed className="h-3 w-3" aria-hidden="true" />} />
            </div>
          </Subsection>

          <Subsection title="SelectButton">
            <SelectButton
              value={demoView}
              onChange={(e) => e.value && setDemoView(e.value as DemoView)}
              allowEmpty={false}
              options={DEMO_VIEWS}
              optionLabel="label"
              optionValue="value"
              itemTemplate={(o: (typeof DEMO_VIEWS)[number]) => (
                <>
                  <o.Icon className="h-4 w-4" aria-hidden="true" />
                  <span className="sr-only sm:not-sr-only">{o.label}</span>
                </>
              )}
              pt={{ root: { "aria-label": "Vista de ejemplo" } }}
            />
          </Subsection>

          <Subsection title="Calendar (rango)">
            <div className="max-w-xs">
              <label htmlFor="ds-range" className="sr-only">Rango de fechas de ejemplo</label>
              <Calendar
                inputId="ds-range"
                selectionMode="range"
                value={demoRange}
                onChange={(e) => setDemoRange(e.value ?? null)}
                readOnlyInput
                showIcon
                icon={<CalendarRange className="h-4 w-4" aria-hidden="true" />}
                showButtonBar
                hideOnRangeSelection
                dateFormat="dd/mm/yy"
                placeholder="Desde – hasta"
              />
            </div>
          </Subsection>

          <Subsection title="DataTable + Paginator">
            <div className="fx-card overflow-hidden">
              <DataTable
                value={demoSorted}
                dataKey="id"
                lazy
                rowHover
                sortField={demoSort.field}
                sortOrder={demoSort.order}
                onSort={(e) => setDemoSort({ field: e.sortField as keyof DemoRow, order: e.sortOrder === 1 ? 1 : -1 })}
                removableSort={false}
                sortIcon={(opts: { sorted?: boolean; sortOrder?: number | null }) =>
                  opts.sorted
                    ? opts.sortOrder === 1
                      ? <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" />
                      : <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
                    : <ChevronsUpDown className="h-3.5 w-3.5" aria-hidden="true" />
                }
                tableStyle={{ minWidth: "32rem" }}
                pt={{ table: { "aria-label": "Inspecciones de ejemplo" } }}
              >
                <Column field="causa" sortField="causa" sortable header="N° de causa" />
                <Column field="caratula" sortField="caratula" sortable header="Carátula" />
                <Column field="fecha" sortField="fecha" sortable header="Fecha" />
                <Column field="estado" header="Estado" />
              </DataTable>
            </div>
            <Paginator
              first={demoFirst}
              rows={8}
              totalRecords={40}
              onPageChange={(e) => setDemoFirst(e.first)}
              template="PrevPageLink PageLinks NextPageLink"
              pageLinkSize={5}
            />
          </Subsection>

          <Subsection title="InputTextarea">
            <div className="grid max-w-3xl grid-cols-1 gap-4 sm:grid-cols-3">
              <div className="space-y-1.5">
                <label htmlFor="ds-ta" className="block text-fx-body-sm font-medium text-fx-text">Descripción</label>
                <InputTextarea id="ds-ta" rows={3} placeholder="Contanos qué pasó…" />
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ds-ta-invalid" className="block text-fx-body-sm font-medium text-fx-text">Descripción (inválida)</label>
                <InputTextarea id="ds-ta-invalid" rows={3} invalid aria-invalid aria-describedby="ds-ta-invalid-msg" defaultValue="Corto" />
                <p id="ds-ta-invalid-msg" className="text-xs text-fx-danger">Escribí al menos 10 caracteres.</p>
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ds-ta-disabled" className="block text-fx-body-sm font-medium text-fx-text-disabled">Descripción (deshabilitada)</label>
                <InputTextarea id="ds-ta-disabled" rows={3} disabled defaultValue="No editable" />
              </div>
            </div>
          </Subsection>

          <Subsection title="Tooltip (FxTip)">
            <FxTip label="Guía de uso" side="bottom">
              <button
                type="button"
                aria-label="Guía de uso (ejemplo)"
                className="inline-flex h-8 w-8 items-center justify-center rounded-fx-md text-fx-text-2 transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring"
              >
                <HelpCircle className="h-4 w-4" aria-hidden="true" />
              </button>
            </FxTip>
          </Subsection>

          <Subsection title="ConfirmDialog">
            <div className="flex flex-wrap gap-3">
              <Button label="Confirmación destructiva" severity="secondary" onClick={() => setConfirmTone("destructive")} />
              <Button label="Confirmación neutra" severity="secondary" onClick={() => setConfirmTone("neutral")} />
            </div>
            <ConfirmDialog
              open={confirmTone !== null}
              onOpenChange={(o) => { if (!o) setConfirmTone(null); }}
              onConfirm={() => fxToast.info("Confirmado", "Acción de ejemplo, no hace nada.")}
              tone={confirmTone ?? "destructive"}
              title={confirmTone === "neutral" ? "¿Restaurar el texto por defecto?" : "¿Salir de la inspección?"}
              description={confirmTone === "neutral"
                ? "Se reemplaza lo que escribiste por el texto sugerido."
                : "Los archivos capturados no se guardan en el servidor. Si salís ahora, se pierden."}
              confirmLabel={confirmTone === "neutral" ? "Restaurar" : "Salir sin guardar"}
              cancelLabel="Seguir acá"
            />
          </Subsection>

          <Subsection title="FxBanner">
            <div className="grid max-w-2xl grid-cols-1 gap-3">
              <FxBanner role="note" tone="error" onClose={() => {}}>No se pudo cargar el historial.</FxBanner>
              <FxBanner role="note" tone="warn">Tatana sin conexión: abrí el agente para adquirir.</FxBanner>
              <FxBanner role="note" tone="success">Informe generado y firmado.</FxBanner>
              <FxBanner role="note" tone="info">Preparando la inspección…</FxBanner>
            </div>
          </Subsection>

          <Subsection title="Dropdown">
            <div className="grid grid-cols-1 gap-5 sm:grid-cols-3">
              <div className="space-y-1.5">
                <label htmlFor="ds-dd-tratamiento" className="block text-fx-body-sm font-medium text-fx-text">Tratamiento en el informe</label>
                <Dropdown
                  inputId="ds-dd-tratamiento"
                  value={demoTratamiento}
                  options={DEMO_TRATAMIENTO}
                  optionLabel="label"
                  optionValue="value"
                  onChange={(e) => setDemoTratamiento(e.value)}
                />
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ds-dd-invalid" className="block text-fx-body-sm font-medium text-fx-text">Cámara</label>
                <Dropdown
                  inputId="ds-dd-invalid"
                  value={demoInvalidOpt}
                  options={DEMO_CAMERAS}
                  onChange={(e) => setDemoInvalidOpt(e.value)}
                  placeholder="Elegí una cámara…"
                  invalid={!demoInvalidOpt}
                  aria-describedby="ds-dd-invalid-msg"
                />
                <p id="ds-dd-invalid-msg" className={demoInvalidOpt ? "m-0 text-xs text-fx-text-3" : "m-0 text-xs text-fx-danger"}>
                  {demoInvalidOpt ? "Cámara elegida." : "Elegí una cámara para continuar."}
                </p>
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ds-dd-disabled" className="block text-fx-body-sm font-medium text-fx-text-disabled">Micrófono (deshabilitado)</label>
                <Dropdown inputId="ds-dd-disabled" value="Micrófono integrado" options={["Micrófono integrado"]} disabled />
              </div>
            </div>
          </Subsection>

          <Subsection title="ProgressBar">
            <div className="grid max-w-md grid-cols-1 gap-4">
              {[0, 50, 100].map((v) => (
                <div key={v} className="space-y-1.5">
                  <p className="m-0 text-xs text-fx-text-2">{v} %</p>
                  <ProgressBar value={v} showValue={false} aria-label={`Progreso de ejemplo al ${v} %`} />
                </div>
              ))}
            </div>
          </Subsection>

          <Subsection title="Riel de pasos (StepIndicator + StepIndicatorCompact)">
            <div className="grid grid-cols-1 gap-6 sm:grid-cols-[13rem_1fr]">
              <StepIndicator steps={DEMO_STEPS} current={demoStep} minJumpable={2} onSelect={setDemoStep} />
              <div className="space-y-3">
                <StepIndicatorCompact steps={DEMO_STEPS} current={demoStep} />
                <div className="flex flex-wrap gap-3">
                  <Button
                    severity="secondary"
                    size="small"
                    label="Retroceder"
                    disabled={demoStep <= 1}
                    onClick={() => setDemoStep((n) => Math.max(1, n - 1))}
                  />
                  <Button
                    size="small"
                    label="Avanzar"
                    disabled={demoStep >= DEMO_STEPS.length}
                    onClick={() => setDemoStep((n) => Math.min(DEMO_STEPS.length, n + 1))}
                  />
                </div>
              </div>
            </div>
          </Subsection>

          <Subsection title="Marco de teléfono (PhoneShell + PhoneFrame)">
            <div className="flex flex-wrap items-start gap-8">
              <div className="w-32 space-y-2 text-center">
                <PhoneShell platform="android">
                  <div className="absolute inset-0 flex items-center justify-center bg-fx-surface-2 text-xs text-fx-text-3">Android</div>
                </PhoneShell>
              </div>
              <div className="w-32 space-y-2 text-center">
                <PhoneShell platform="ios">
                  <div className="absolute inset-0 flex items-center justify-center bg-fx-surface-2 text-xs text-fx-text-3">iOS</div>
                </PhoneShell>
              </div>
              <div className="space-y-3">
                <PhoneFrame src={null} platform="android" loading={demoPhoneLoading} onRefresh={() => {}} />
                <Button
                  severity="secondary"
                  size="small"
                  label={demoPhoneLoading ? "Ver vacío" : "Ver cargando"}
                  onClick={() => setDemoPhoneLoading((v) => !v)}
                />
              </div>
            </div>
          </Subsection>

          <Subsection title="Checkbox">
            <div className="flex flex-wrap items-start gap-8">
              <span className="flex items-center gap-2">
                <Checkbox inputId="ds-cb-off" checked={!demoChecked} onChange={(e) => setDemoChecked(!e.checked)}
                  icon={<Check className="h-3.5 w-3.5" strokeWidth={3} aria-hidden="true" />} />
                <label htmlFor="ds-cb-off" className="text-fx-body-sm text-fx-text">Normal</label>
              </span>
              <span className="flex items-center gap-2">
                <Checkbox inputId="ds-cb-on" checked={demoChecked} onChange={(e) => setDemoChecked(!!e.checked)}
                  icon={<Check className="h-3.5 w-3.5" strokeWidth={3} aria-hidden="true" />} />
                <label htmlFor="ds-cb-on" className="text-fx-body-sm text-fx-text">Marcado</label>
              </span>
              <span className="flex items-center gap-2">
                <Checkbox inputId="ds-cb-disabled" checked={false} disabled
                  icon={<Check className="h-3.5 w-3.5" strokeWidth={3} aria-hidden="true" />} />
                <label htmlFor="ds-cb-disabled" className="text-fx-body-sm text-fx-text-3">Deshabilitado</label>
              </span>
              <div className="flex max-w-xs items-start gap-2.5">
                <Checkbox inputId="ds-cb-mic" checked={demoMic} onChange={(e) => setDemoMic(!!e.checked)}
                  aria-describedby="ds-cb-mic-hint" className="mt-0.5"
                  icon={<Check className="h-3.5 w-3.5" strokeWidth={3} aria-hidden="true" />} />
                <div>
                  <label htmlFor="ds-cb-mic" className="cursor-pointer text-fx-body-sm text-fx-text">Mezclar micrófono de la PC</label>
                  <p id="ds-cb-mic-hint" className="m-0 mt-0.5 text-xs text-fx-text-3">Si el audio no se graba solo (ej. notas de voz de WhatsApp)</p>
                </div>
              </div>
            </div>
          </Subsection>

          <Subsection title="Diálogo de medios (FxMediaDialog)">
            <Button severity="secondary" label="Abrir diálogo de cámara" aria-haspopup="dialog" onClick={() => setDemoMediaOpen(true)} />
            <FxMediaDialog
              visible={demoMediaOpen}
              onHide={() => setDemoMediaOpen(false)}
              title="Foto del perito"
              icon={<Camera className="h-4 w-4" aria-hidden="true" />}
            >
              <div className="flex aspect-video items-center justify-center bg-fx-bg text-xs text-fx-text-3">
                Vista de cámara (relleno)
              </div>
              <div className={MEDIA_ACTIONS}>
                <div className="flex-1">
                  <Button text severity="secondary" label="Cancelar" onClick={() => setDemoMediaOpen(false)} className="min-h-11" />
                </div>
                <button
                  type="button"
                  disabled
                  aria-label="Tomar foto"
                  className="relative flex h-16 w-16 items-center justify-center rounded-full border-[3px] border-fx-accent disabled:cursor-not-allowed disabled:opacity-40"
                >
                  <span className="h-12 w-12 rounded-full bg-fx-text" aria-hidden="true" />
                </button>
                <span className="flex-1" aria-hidden="true" />
              </div>
            </FxMediaDialog>
          </Subsection>

          <Subsection title="CopyButton">
            <div className="flex flex-wrap items-center gap-3">
              <span translate="no" className="select-all font-mono text-fx-body font-bold text-fx-text">k7Q2-mX9p-4Lr8</span>
              <CopyButton text="k7Q2-mX9p-4Lr8" label="Copiar contraseña de ejemplo" />
            </div>
          </Subsection>

          <Subsection title="Toast global (useFxToast)">
            <div className="flex flex-wrap gap-3">
              <Button label="Éxito" text onClick={() => fxToast.success("Perfil de perito guardado", "Se usa en los casos que crees o edites desde ahora.")} />
              <Button label="Info" text onClick={() => fxToast.info("Agente conectado", "Tatana responde en localhost:8765.")} />
              <Button label="Advertencia" text onClick={() => fxToast.warn("Batería baja", "El celular tiene menos del 15 % de carga.")} />
              <Button label="Error" text severity="danger" onClick={() => fxToast.error("No se pudo capturar", "Revisá el cable USB y volvé a intentar.")} />
            </div>
          </Subsection>
        </Section>

        {/* 6. Sonner + tooltips */}
        <Section
          id="sonner"
          title="Notificaciones y tooltips"
          intro="El Toaster global (sonner) usa el mismo esquema que el Toast de Prime. Los tooltips de base-ui pasan a superficie 3."
        >
          <div className="flex flex-wrap items-center gap-3">
            <Button label="Éxito" severity="secondary" onClick={() => sonner.success("Captura guardada", { description: "Se agregó al caso." })} />
            <Button label="Info" severity="secondary" onClick={() => sonner.info("Hay una versión nueva", { description: "Recargá la página cuando termines." })} />
            <Button label="Advertencia" severity="secondary" onClick={() => sonner.warning("Sin dispositivo", { description: "Conectá el celular por USB." })} />
            <Button label="Error" severity="secondary" onClick={() => sonner.error("Falló la subida", { description: "El servidor no respondió." })} />
            <Tip label="Tooltip de ejemplo">
              <button
                type="button"
                className="rounded-fx-md border border-fx-border-strong bg-fx-surface-2 px-4 py-2 text-fx-body-sm text-fx-text transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 fx-focus-ring"
              >
                Pasá el puntero o enfocá
              </button>
            </Tip>
          </div>
        </Section>

        {/* 7. Convivencia legacy */}
        <Section
          id="legacy"
          title="Convivencia legacy"
          intro="Clases legacy (.btn-*, .card, .input, .badge-*) al lado de sus equivalentes Prime: ninguna pisa a la otra."
        >
          <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
            <Subsection title="Legacy">
              <div className="card space-y-4 p-5">
                <div className="flex flex-wrap gap-2">
                  <button type="button" className="btn-primary">btn-primary</button>
                  <button type="button" className="btn-secondary">btn-secondary</button>
                </div>
                <label htmlFor="ds-legacy-input" className="sr-only">Input legacy</label>
                <input id="ds-legacy-input" className="input" placeholder=".input legacy" />
                <div className="flex flex-wrap gap-2">
                  <span className="badge badge-blue">badge-blue</span>
                  <span className="badge badge-green">badge-green</span>
                  <span className="badge badge-amber">badge-amber</span>
                  <span className="badge badge-red">badge-red</span>
                  <span className="badge badge-gray">badge-gray</span>
                </div>
              </div>
            </Subsection>
            <Subsection title="Sistema nuevo">
              <div className="fx-card space-y-4 p-5">
                <div className="flex flex-wrap gap-2">
                  <Button label="Button" />
                  <Button label="secondary" severity="secondary" />
                </div>
                <label htmlFor="ds-new-input" className="sr-only">InputText Prime</label>
                <InputText id="ds-new-input" placeholder="InputText de Prime" />
                <div className="flex flex-wrap gap-2">
                  <span className="inline-flex items-center rounded-fx-pill border border-fx-border px-2 py-0.5 text-xs font-medium text-fx-success bg-fx-success-soft">success-soft</span>
                  <span className="inline-flex items-center rounded-fx-pill border border-fx-border px-2 py-0.5 text-xs font-medium text-fx-warning bg-fx-warning-soft">warning-soft</span>
                  <span className="inline-flex items-center rounded-fx-pill border border-fx-border px-2 py-0.5 text-xs font-medium text-fx-danger bg-fx-danger-soft">danger-soft</span>
                  <span className="inline-flex items-center rounded-fx-pill border border-fx-border px-2 py-0.5 text-xs font-medium text-fx-info bg-fx-info-soft">info-soft</span>
                </div>
              </div>
            </Subsection>
          </div>
        </Section>
      </main>

      {/* 8. Footer */}
      <SiteFooter
        organization={{ name: organizationName, logoSrc: organizationLogoSrc }}
        extraColumns={[
          {
            title: "Recursos (ejemplo)",
            links: [
              { label: "Paleta", href: "#paleta" },
              { label: "Componentes PrimeReact", href: "#prime" },
            ],
          },
        ]}
      />
    </div>
  );
}
