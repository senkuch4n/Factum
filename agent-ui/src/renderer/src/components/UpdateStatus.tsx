import { useState } from 'react'
import { motion, AnimatePresence, useReducedMotion } from 'framer-motion'
import {
  Loader2, Download, ArrowUpCircle, CheckCircle2, WifiOff, ShieldAlert,
  AlertTriangle, RefreshCw, Info, X, PauseCircle,
} from 'lucide-react'
import { busyOperationsText } from '../hooks/useTatanaUpdates'

/* SDD tatana-instalador-autoupdate §6.8: una línea de estado de la
   actualización (debajo del estado del agente) y avisos cerrables. Nada de
   diálogos modales. */

const ENTER = [0.22, 1, 0.36, 1] as const

type Tone = 'neutral' | 'ok' | 'info' | 'warn' | 'error'

const TONE_COLOR: Record<Tone, string> = {
  neutral: 'var(--text-secondary)',
  ok: 'var(--green)',
  info: 'var(--blue)',
  warn: 'var(--amber)',
  error: 'var(--red)',
}

interface Line {
  icon: React.ElementType
  text: string
  tone: Tone
  spin?: boolean
}

function lineFor(s: TatanaUpdateState): Line | null {
  const v = s.availableVersion ? `v${s.availableVersion}` : 'la versión nueva'
  switch (s.phase) {
    case 'checking':
      return { icon: Loader2, text: 'Buscando actualizaciones…', tone: 'info', spin: true }
    case 'downloading':
      return { icon: Download, text: `Descargando ${v} (${Math.round(s.percent ?? 0)}\u00a0%)`, tone: 'info' }
    case 'ready':
      return { icon: ArrowUpCircle, text: `Actualización lista: ${v}`, tone: 'info' }
    case 'installing':
      return { icon: Loader2, text: `Instalando ${v}…`, tone: 'info', spin: true }
    case 'up_to_date':
      return { icon: CheckCircle2, text: 'Tatana está al día', tone: 'ok' }
    case 'offline':
      return { icon: WifiOff, text: 'No se pudo buscar actualizaciones (sin conexión)', tone: 'neutral' }
    case 'rejected':
      return { icon: ShieldAlert, text: 'Se descartó una actualización: no pasó la verificación', tone: 'warn' }
    case 'error':
      return { icon: AlertTriangle, text: 'No se pudo instalar la actualización', tone: 'error' }
    case 'disabled':
      // Sin línea en producción; en dev se aclara por qué no hay actualizaciones.
      return import.meta.env.DEV
        ? { icon: Info, text: 'Actualizaciones desactivadas', tone: 'neutral' }
        : null
    default:
      return null
  }
}

const CAN_CHECK: ReadonlySet<TatanaUpdatePhase> = new Set(['idle', 'up_to_date', 'offline', 'rejected', 'error'])

export function UpdateStatusLine({ state }: { state: TatanaUpdateState | null }) {
  const [installing, setInstalling] = useState(false)
  const [installError, setInstallError] = useState<string | null>(null)
  if (!state) return null
  const line = lineFor(state)
  if (!line) return null

  const busy = state.busyOperations ?? []
  const isBusy = busy.length > 0
  const Icon = line.icon
  const percent = Math.max(0, Math.min(100, state.percent ?? 0))

  const install = async () => {
    setInstalling(true)
    setInstallError(null)
    try {
      const r = await window.tatana.installUpdate()
      if (!r.ok && r.reason === 'error') setInstallError('No se pudo iniciar la instalación. Reintentá en unos segundos.')
    } catch {
      setInstallError('No se pudo iniciar la instalación. Reintentá en unos segundos.')
    } finally {
      setInstalling(false)
    }
  }

  return (
    <section
      aria-label="Actualizaciones de Tatana"
      className="rounded-xl border px-4 py-3"
      style={{ background: 'var(--bg-surface)', borderColor: 'var(--border)' }}
    >
      <div className="flex items-center gap-3 flex-wrap">
        <Icon
          aria-hidden="true"
          className={`w-4 h-4 flex-shrink-0 ${line.spin ? 'animate-spin motion-reduce:animate-none' : ''}`}
          style={{ color: TONE_COLOR[line.tone] }}
        />
        <p
          role="status"
          aria-atomic="true"
          className="flex-1 min-w-0 text-sm font-medium tabular-nums"
          style={{ color: 'var(--text-primary)' }}
        >
          {line.text}
        </p>

        {state.phase === 'ready' && (
          <button
            type="button"
            onClick={install}
            disabled={isBusy || installing}
            aria-describedby={isBusy ? 'tatana-update-busy' : undefined}
            className="focus-ring flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold transition-opacity hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
            style={{ background: 'var(--blue)', color: '#ffffff' }}
          >
            {installing
              ? <Loader2 aria-hidden="true" className="w-3.5 h-3.5 animate-spin motion-reduce:animate-none" />
              : <RefreshCw aria-hidden="true" className="w-3.5 h-3.5" />}
            Reiniciar y actualizar
          </button>
        )}

        {CAN_CHECK.has(state.phase) && (
          <button
            type="button"
            onClick={() => { void window.tatana.checkUpdates() }}
            className="focus-ring flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-medium transition-colors bg-[var(--btn-secondary-bg)] hover:bg-[var(--btn-secondary-hov)]"
            style={{ color: 'var(--text-secondary)' }}
          >
            <RefreshCw aria-hidden="true" className="w-3 h-3" />
            Buscar actualizaciones
          </button>
        )}
      </div>

      {state.phase === 'downloading' && (
        <div
          role="progressbar"
          aria-label="Descarga de la actualización"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(percent)}
          className="mt-2.5 h-1 rounded-full overflow-hidden"
          style={{ background: 'var(--bg-elevated)' }}
        >
          {/* scaleX (no width): se anima solo transform */}
          <div
            className="h-full w-full origin-left transition-transform duration-300 motion-reduce:transition-none"
            style={{ background: 'var(--blue)', transform: `scaleX(${percent / 100})` }}
          />
        </div>
      )}

      {state.phase === 'ready' && isBusy && (
        <p
          id="tatana-update-busy"
          className="mt-2 flex items-center gap-1.5 text-xs"
          style={{ color: 'var(--text-secondary)' }}
        >
          <PauseCircle aria-hidden="true" className="w-3.5 h-3.5 flex-shrink-0" style={{ color: 'var(--amber)' }} />
          Esperando: {busyOperationsText(busy)}. Se puede actualizar cuando termine.
        </p>
      )}

      {state.phase === 'ready' && !isBusy && (
        <p className="mt-2 text-xs" style={{ color: 'var(--text-secondary)' }}>
          También se instala sola la próxima vez que se abra Tatana o al iniciar sesión.
        </p>
      )}

      {installError && (
        <p role="alert" className="mt-2 text-xs" style={{ color: 'var(--red)' }}>
          {installError}
        </p>
      )}
    </section>
  )
}

/* ── Avisos cerrables ─────────────────────────────────────────────────────── */

function Path({ children }: { children: string }) {
  return (
    <code
      translate="no"
      className="select-text break-all px-1 py-0.5 rounded font-mono text-[0.7rem]"
      style={{ background: 'var(--bg-elevated)', color: 'var(--text-primary)' }}
    >
      {children}
    </code>
  )
}

function Notice({
  tone, title, children, onClose,
}: {
  tone: 'info' | 'warn'
  title: string
  children: React.ReactNode
  onClose: () => void
}) {
  const reduce = useReducedMotion()
  const color = tone === 'warn' ? 'var(--amber)' : 'var(--blue)'
  const Icon = tone === 'warn' ? AlertTriangle : Info
  return (
    <motion.div
      layout={!reduce}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1, transition: { duration: reduce ? 0 : 0.2, ease: ENTER } }}
      exit={{ opacity: 0, transition: { duration: reduce ? 0 : 0.12 } }}
      role="note"
      aria-label={title}
      className="flex items-start gap-3 px-4 py-3 rounded-xl border"
      style={{
        background: tone === 'warn' ? 'rgba(217,119,6,0.06)' : 'rgba(37,99,235,0.05)',
        borderColor: tone === 'warn' ? 'rgba(217,119,6,0.22)' : 'rgba(37,99,235,0.18)',
      }}
    >
      <Icon aria-hidden="true" className="w-4 h-4 mt-0.5 flex-shrink-0" style={{ color }} />
      <div className="flex-1 min-w-0">
        <p className="text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>{title}</p>
        <p className="text-xs mt-0.5 leading-relaxed" style={{ color: 'var(--text-secondary)' }}>{children}</p>
      </div>
      <button
        type="button"
        onClick={onClose}
        aria-label={`Cerrar aviso: ${title}`}
        title="Cerrar aviso"
        className="focus-ring -m-1 p-1.5 rounded-lg flex-shrink-0 transition-colors hover:bg-[var(--btn-secondary-hov)]"
        style={{ color: 'var(--text-secondary)' }}
      >
        <X aria-hidden="true" className="w-4 h-4" />
      </button>
    </motion.div>
  )
}

export function TatanaNotices({ info }: { info: TatanaAppInfo | null }) {
  const migration = info?.notices.migration
  const lc = info?.notices.localConfig
  const dismiss = (id: TatanaNoticeId) => { void window.tatana.dismissNotice(id) }

  return (
    <AnimatePresence initial={false}>
      {migration && (
        <Notice key="migration" tone="info" title="Tatana portátil reemplazado" onClose={() => dismiss('migration')}>
          Se reemplazó Tatana portátil{migration.portable_version ? ` (v${migration.portable_version})` : ''}.
          Tu evidencia sigue en <Path>{'%LOCALAPPDATA%\\Tatana\\data'}</Path> y <Path>{'C:\\Factum\\Evidencia'}</Path>.
          La carpeta del portátil anterior quedó en <Path>{migration.carpeta_anterior}</Path>; la podés borrar
          cuando confirmes que todo anda.
          {migration.conflicto && ' Tu configuración local anterior quedó en appsettings.Local.portable.json.'}
        </Notice>
      )}
      {lc && lc.error && (
        <Notice key="local-config-error" tone="warn" title="Configuración local con error" onClose={() => dismiss('local_config')}>
          La configuración local (<Path>{lc.path}</Path>) tiene un error y se ignoró: {lc.error.replace(/\.+$/, '')}.
        </Notice>
      )}
      {lc && !lc.error && lc.overridesAllowedOrigins && (
        <Notice key="local-config-origins" tone="warn" title="Orígenes fijados por la configuración local" onClose={() => dismiss('local_config')}>
          La configuración local (<Path>{lc.path}</Path>) fija los orígenes permitidos: las actualizaciones de
          Tatana no los van a cambiar.
        </Notice>
      )}
    </AnimatePresence>
  )
}
