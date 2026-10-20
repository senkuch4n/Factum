import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { motion, AnimatePresence, useReducedMotion } from 'framer-motion'
import {
  AlertTriangle, CheckCircle2, Circle, Copy, Download, ExternalLink, FileText, Info, Loader2,
  LogOut, MinusCircle, RefreshCw, ShieldCheck, XCircle, Check,
} from 'lucide-react'

/* SDD tatana-linux §8.3: vista "Esta PC" (solo Linux). Tarjeta de instalación,
   asistente "Preparar esta PC" (una sola contraseña, progreso por paso,
   resultado y estados de error de la HU) y lista de chequeos sin contraseña. */

const ENTER = [0.22, 1, 0.36, 1] as const

const PASOS: { id: TatanaPasoId; label: string }[] = [
  { id: 'paquetes', label: 'Paquetes del sistema' },
  { id: 'reglas_usb', label: 'Reglas USB' },
  { id: 'airplay', label: 'AirPlay' },
  { id: 'camara_microfono', label: 'Cámara y micrófono' },
  { id: 'sandbox', label: 'Protección de la ventana' },
]

const CHECK_LABEL: Record<AgentLinuxCheckId, string> = {
  usbmuxd: 'iPhone por USB',
  android_usb: 'Android por USB',
  avahi: 'AirPlay (descubrimiento en la red)',
  uxplay: 'AirPlay (receptor)',
  camera: 'Cámara',
  microphone: 'Micrófono',
  sandbox: 'Protección de la ventana',
}

const CHECK_STATUS: Record<AgentLinuxCheckStatus, { text: string; color: string; icon: React.ElementType }> = {
  ok: { text: 'Listo', color: 'var(--green)', icon: CheckCircle2 },
  missing: { text: 'Falta', color: 'var(--red)', icon: XCircle },
  outdated: { text: 'Versión anterior', color: 'var(--amber)', icon: AlertTriangle },
  warning: { text: 'Revisar', color: 'var(--amber)', icon: AlertTriangle },
  not_applicable: { text: 'No aplica', color: 'var(--text-secondary)', icon: MinusCircle },
}

const PASO_ESTADO: Record<TatanaPasoEstado | 'pendiente', { text: string; color: string; icon: React.ElementType; spin?: boolean }> = {
  pendiente: { text: 'Pendiente', color: 'var(--text-muted)', icon: Circle },
  en_curso: { text: 'En curso', color: 'var(--blue)', icon: Loader2, spin: true },
  ok: { text: 'Listo', color: 'var(--green)', icon: CheckCircle2 },
  error: { text: 'Falló', color: 'var(--red)', icon: XCircle },
  omitido: { text: 'No hacía falta', color: 'var(--text-secondary)', icon: MinusCircle },
}

const MSG_CANCELADO = 'Sin la contraseña, Tatana no puede reconocer iPhone ni Android por USB.'
const MSG_SIN_INTERNET = 'Hace falta internet para instalar los paquetes del sistema.'

function formatFecha(iso: string): string {
  const d = new Date(iso)
  return Number.isNaN(d.getTime())
    ? iso
    : d.toLocaleString('es-AR', { dateStyle: 'medium', timeStyle: 'short' })
}

export function LinuxPc() {
  const [status, setStatus] = useState<TatanaLinuxStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [running, setRunning] = useState(false)
  const [pasos, setPasos] = useState<TatanaPasoPreparacion[]>([])
  const [outcome, setOutcome] = useState<TatanaPrepareOutcome | null>(null)
  const [prepareError, setPrepareError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      setStatus(await window.tatana.getLinuxStatus())
    } catch {
      setStatus(null)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  useEffect(() => {
    window.tatana.onPrepareStep((paso) => {
      setPasos((prev) => {
        const next = prev.filter((p) => p.id !== paso.id)
        next.push(paso)
        return PASOS.map((s) => next.find((p) => p.id === s.id)).filter((p): p is TatanaPasoPreparacion => !!p)
      })
    })
    return () => window.tatana.removeAllListeners('tatana:prepare-pc-step')
  }, [])

  const prepare = async () => {
    setRunning(true)
    setPasos([])
    setOutcome(null)
    setPrepareError(null)
    try {
      const r = await window.tatana.preparePc()
      if (r) {
        setOutcome(r)
        if (r.pasos.length) setPasos(r.pasos)
      } else {
        setPrepareError('No se pudo iniciar la preparación.')
      }
    } catch {
      setPrepareError('No se pudo iniciar la preparación.')
    } finally {
      setRunning(false)
      void refresh()
    }
  }

  const health = status?.health ?? null
  const preparation = status?.preparation ?? null
  const needsSetup = !preparation || health?.setup_state === 'incomplete'
  const requiereReiniciar = outcome ? outcome.requiereReiniciarSesion : preparation?.requiere_reiniciar_sesion === true
  const showSteps = running || pasos.length > 0

  return (
    <div className="p-6 space-y-5 max-w-3xl">
      <div>
        <h1 className="text-lg font-bold" style={{ color: 'var(--text-primary)' }}>Esta PC</h1>
        <p className="text-sm mt-0.5" style={{ color: 'var(--text-secondary)' }}>
          {health?.distro_name ?? 'Linux'}
          {health?.glibc_version ? ` · glibc ${health.glibc_version}` : ''}
        </p>
      </div>

      {status && status.offer.kind !== 'none' && <InstallCard offer={status.offer} canonicalPath={status.canonicalPath} />}

      {/* Asistente "Preparar esta PC" */}
      <section
        aria-labelledby="prepare-title"
        className="rounded-2xl border p-4 space-y-4"
        style={{ background: 'var(--bg-surface)', borderColor: 'var(--border)' }}
      >
        <div className="flex items-start gap-3 flex-wrap">
          <ShieldCheck aria-hidden="true" className="w-5 h-5 mt-0.5 flex-shrink-0" style={{ color: 'var(--blue)' }} />
          <div className="flex-1 min-w-[16rem]">
            <h2 id="prepare-title" className="text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>
              Preparar esta PC
            </h2>
            <p id="prepare-help" className="text-xs mt-0.5 leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
              Pide la contraseña de administrador una sola vez, para que la PC reconozca iPhone y Android por USB
              sin pedirte la clave cada vez.
            </p>
            {preparation && !running && !outcome && (
              <p className="text-xs mt-1.5" style={{ color: 'var(--text-secondary)' }}>
                Última preparación: {formatFecha(preparation.fecha)} ({resultadoCorto(preparation.resultado)}).
              </p>
            )}
          </div>
          <button
            type="button"
            onClick={() => { void prepare() }}
            disabled={running}
            aria-describedby="prepare-help"
            className={`focus-ring flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-semibold transition-opacity disabled:opacity-60 disabled:cursor-not-allowed ${
              needsSetup ? 'text-white hover:opacity-90' : 'bg-[var(--btn-secondary-bg)] hover:bg-[var(--btn-secondary-hov)]'
            }`}
            style={needsSetup ? { background: 'var(--blue)' } : { color: 'var(--text-secondary)' }}
          >
            {running
              ? <Loader2 aria-hidden="true" className="w-4 h-4 animate-spin motion-reduce:animate-none" />
              : <ShieldCheck aria-hidden="true" className="w-4 h-4" />}
            {running ? 'Preparando…' : needsSetup ? 'Preparar esta PC' : 'Preparar de nuevo'}
          </button>
        </div>

        {showSteps && <StepList pasos={pasos} running={running} />}

        <AnimatePresence initial={false} mode="wait">
          {outcome && !running && (
            <OutcomeBanner key={outcome.resultado} outcome={outcome} onRetry={() => { void prepare() }} />
          )}
        </AnimatePresence>

        {prepareError && (
          <p role="alert" className="text-xs" style={{ color: 'var(--red)' }}>{prepareError}</p>
        )}
      </section>

      {requiereReiniciar && (
        <div
          role="note"
          className="flex items-start gap-3 px-4 py-3 rounded-xl border"
          style={{ background: 'rgba(217,119,6,0.08)', borderColor: 'rgba(217,119,6,0.35)' }}
        >
          <LogOut aria-hidden="true" className="w-5 h-5 mt-0.5 flex-shrink-0" style={{ color: 'var(--amber)' }} />
          <div>
            <p className="text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>
              Cerrá sesión y volvé a entrar
            </p>
            <p className="text-xs mt-0.5 leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
              Los permisos nuevos de la cámara recién se aplican en la próxima sesión. Guardá tu trabajo, cerrá
              sesión en el escritorio y volvé a entrar.
            </p>
          </div>
        </div>
      )}

      <ChecksList health={health} loading={loading} onRefresh={() => { void refresh() }} />
    </div>
  )
}

function resultadoCorto(r: TatanaResultadoPreparacion): string {
  switch (r) {
    case 'ok': return 'todo listo'
    case 'parcial': return 'con pasos que fallaron'
    case 'sin_soporte': return 'distro sin soporte'
    case 'sin_internet': return 'sin internet'
    case 'cancelado': return 'sin contraseña'
    case 'sin_polkit': return 'falta correr el comando a mano'
    default: return 'con error'
  }
}

/* ── Tarjeta "Instalá Tatana en esta PC" (D-T6) ─────────────────────────── */

function InstallCard({ offer, canonicalPath }: { offer: TatanaInstallOffer; canonicalPath: string }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  if (offer.kind === 'none') return null

  const title = offer.kind === 'install' ? 'Instalá Tatana en esta PC' : 'Tatana ya está instalado en esta PC'
  const action = offer.kind === 'install'
    ? 'Instalar (recomendado)'
    : offer.kind === 'replace' ? 'Reemplazar la instalada' : 'Abrir la instalada'

  const run = async () => {
    setBusy(true)
    setError(null)
    try {
      const r = await window.tatana.installOnPc()
      // Si sale bien, Tatana se relanza: esta ventana se cierra sola.
      if (!r.ok) setError(r.error ?? 'No se pudo instalar Tatana.')
    } catch {
      setError('No se pudo instalar Tatana.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section
      aria-labelledby="install-title"
      className="rounded-2xl border p-4 space-y-3"
      style={{ background: 'rgba(37,99,235,0.05)', borderColor: 'rgba(37,99,235,0.22)' }}
    >
      <div className="flex items-start gap-3 flex-wrap">
        <Download aria-hidden="true" className="w-5 h-5 mt-0.5 flex-shrink-0" style={{ color: 'var(--blue)' }} />
        <div className="flex-1 min-w-[16rem]">
          <h2 id="install-title" className="text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>{title}</h2>
          <p className="text-xs mt-0.5 leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
            {offer.kind === 'install' && (
              <>
                Tatana se copia a <PathCode>{canonicalPath}</PathCode>, aparece en el menú de aplicaciones y
                arranca solo al iniciar sesión. Sin instalar, no recibe actualizaciones. Después ya podés borrar el
                archivo que descargaste.
              </>
            )}
            {offer.kind === 'replace' && (
              <>Hay una versión anterior instalada (v{offer.installed}). Se reemplaza por esta; tu evidencia y tu configuración no se tocan.</>
            )}
            {offer.kind === 'open' && (
              <>Ya hay un Tatana instalado (v{offer.installed}). Usá ese: es el que recibe actualizaciones.</>
            )}
          </p>
        </div>
        <button
          type="button"
          onClick={() => { void run() }}
          disabled={busy}
          className="focus-ring flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-semibold text-white transition-opacity hover:opacity-90 disabled:opacity-60 disabled:cursor-not-allowed"
          style={{ background: 'var(--blue)' }}
        >
          {busy
            ? <Loader2 aria-hidden="true" className="w-4 h-4 animate-spin motion-reduce:animate-none" />
            : offer.kind === 'open' ? <ExternalLink aria-hidden="true" className="w-4 h-4" /> : <Download aria-hidden="true" className="w-4 h-4" />}
          {busy ? (offer.kind === 'open' ? 'Abriendo…' : 'Instalando…') : action}
        </button>
      </div>
      {error && <p role="alert" className="text-xs" style={{ color: 'var(--red)' }}>{error}</p>}
    </section>
  )
}

/* ── Progreso por paso ───────────────────────────────────────────────────── */

function StepList({ pasos, running }: { pasos: TatanaPasoPreparacion[]; running: boolean }) {
  const done = pasos.filter((p) => p.estado !== 'en_curso').length
  const current = pasos.find((p) => p.estado === 'en_curso')
  const currentLabel = current ? PASOS.find((s) => s.id === current.id)?.label : undefined
  return (
    <div>
      <p role="status" aria-live="polite" className="text-xs font-medium mb-2" style={{ color: 'var(--text-secondary)' }}>
        {running
          ? currentLabel
            ? `Paso ${Math.min(done + 1, PASOS.length)} de ${PASOS.length}: ${currentLabel}`
            : 'Esperando la contraseña de administrador…'
          : `${done} de ${PASOS.length} pasos terminados`}
      </p>
      <ol className="space-y-1">
        {PASOS.map((s) => {
          const paso = pasos.find((p) => p.id === s.id)
          const estado = paso?.estado ?? 'pendiente'
          const e = PASO_ESTADO[estado]
          const Icon = e.icon
          const failed = estado === 'error'
          return (
            <li
              key={s.id}
              className="flex items-start gap-2.5 px-3 py-2 rounded-lg"
              style={{ background: failed ? 'rgba(220,38,38,0.07)' : 'var(--bg-elevated)' }}
            >
              <Icon
                aria-hidden="true"
                className={`w-4 h-4 mt-px flex-shrink-0 ${e.spin ? 'animate-spin motion-reduce:animate-none' : ''}`}
                style={{ color: e.color }}
              />
              <div className="flex-1 min-w-0">
                <p className="text-sm" style={{ color: 'var(--text-primary)' }}>
                  {s.label}
                  <span className="sr-only">: {e.text}</span>
                </p>
                {paso?.detalle && (estado === 'error' || estado === 'omitido') && (
                  <p className="text-xs mt-0.5 break-words select-text" style={{ color: failed ? 'var(--red)' : 'var(--text-secondary)' }}>
                    {paso.detalle}
                  </p>
                )}
              </div>
              <span aria-hidden="true" className="text-xs flex-shrink-0" style={{ color: e.color }}>{e.text}</span>
            </li>
          )
        })}
      </ol>
    </div>
  )
}

/* ── Resultado del asistente ─────────────────────────────────────────────── */

function OutcomeBanner({ outcome, onRetry }: { outcome: TatanaPrepareOutcome; onRetry: () => void }) {
  const reduce = useReducedMotion()
  const r = outcome.resultado
  const tone = r === 'ok' ? 'ok' : r === 'sin_soporte' || r === 'sin_polkit' ? 'info' : r === 'parcial' ? 'warn' : 'error'
  const color = { ok: 'var(--green)', info: 'var(--blue)', warn: 'var(--amber)', error: 'var(--red)' }[tone]
  const bg = { ok: 'rgba(5,150,105,0.06)', info: 'rgba(37,99,235,0.05)', warn: 'rgba(217,119,6,0.07)', error: 'rgba(220,38,38,0.06)' }[tone]
  const Icon = tone === 'ok' ? CheckCircle2 : tone === 'info' ? Info : tone === 'warn' ? AlertTriangle : XCircle

  const title =
    r === 'ok' ? 'Esta PC quedó preparada.'
    : r === 'parcial' ? 'Algunos pasos no se pudieron completar.'
    : r === 'cancelado' ? MSG_CANCELADO
    : r === 'sin_internet' ? MSG_SIN_INTERNET
    : r === 'sin_soporte' ? 'Tatana no sabe preparar esta distribución de Linux.'
    : r === 'sin_polkit' ? 'Esta PC no puede pedir la contraseña desde Tatana.'
    : 'No se pudo completar la preparación.'

  const showLog = r === 'parcial' || r === 'error'
  const showRetry = r === 'cancelado' || r === 'sin_internet' || r === 'parcial' || r === 'error'

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1, transition: { duration: reduce ? 0 : 0.2, ease: ENTER } }}
      exit={{ opacity: 0, transition: { duration: reduce ? 0 : 0.12 } }}
      role={tone === 'error' || tone === 'warn' ? 'alert' : 'status'}
      className="rounded-xl border px-4 py-3 space-y-2.5"
      style={{ background: bg, borderColor: 'var(--border-md)' }}
    >
      <div className="flex items-start gap-2.5">
        <Icon aria-hidden="true" className="w-4 h-4 mt-0.5 flex-shrink-0" style={{ color }} />
        <div className="flex-1 min-w-0">
          <p className="text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>{title}</p>
          {r === 'ok' && (
            <p className="text-xs mt-0.5" style={{ color: 'var(--text-secondary)' }}>
              No vas a tener que volver a escribir la contraseña. Abajo están los chequeos de esta PC.
            </p>
          )}
          {r === 'parcial' && (
            <p className="text-xs mt-0.5" style={{ color: 'var(--text-secondary)' }}>
              Los pasos en rojo dicen el motivo. Podés reintentar o abrir el detalle para soporte técnico.
            </p>
          )}
          {r === 'sin_soporte' && (
            <p className="text-xs mt-0.5" style={{ color: 'var(--text-secondary)' }}>
              No se ejecutó nada. Pasale a tu soporte técnico la lista de paquetes y reglas que hacen falta.
            </p>
          )}
          {r === 'sin_polkit' && (
            <p className="text-xs mt-0.5" style={{ color: 'var(--text-secondary)' }}>
              Abrí una terminal, pegá este comando y escribí tu contraseña. Después tocá «Volver a revisar».
            </p>
          )}
        </div>
      </div>

      {r === 'sin_soporte' && outcome.plan && outcome.plan.comandos.length > 0 && (
        <CommandBlock text={outcome.plan.comandos.join('\n')} copyLabel="Copiar para soporte técnico" label="Paquetes y reglas que hacen falta" />
      )}

      {r === 'sin_polkit' && outcome.fallbackCommand && (
        <>
          <CommandBlock text={outcome.fallbackCommand} copyLabel="Copiar" label="Comando para la terminal" />
          <p className="text-[0.7rem] break-all select-text" style={{ color: 'var(--text-secondary)' }}>
            SHA-256 del script: <span translate="no" className="font-mono">{outcome.scriptSha256}</span>
          </p>
        </>
      )}

      {(showRetry || showLog) && (
        <div className="flex items-center gap-2 flex-wrap">
          {showRetry && (
            <button
              type="button"
              onClick={onRetry}
              className="focus-ring flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold text-white transition-opacity hover:opacity-90"
              style={{ background: 'var(--blue)' }}
            >
              <RefreshCw aria-hidden="true" className="w-3.5 h-3.5" />
              Reintentar
            </button>
          )}
          {showLog && (
            <button
              type="button"
              onClick={() => { void window.tatana.openPrepareLog() }}
              className="focus-ring flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-colors bg-[var(--btn-secondary-bg)] hover:bg-[var(--btn-secondary-hov)]"
              style={{ color: 'var(--text-secondary)' }}
            >
              <FileText aria-hidden="true" className="w-3.5 h-3.5" />
              Ver detalle
            </button>
          )}
        </div>
      )}
    </motion.div>
  )
}

/* ── Bloque de comando copiable ──────────────────────────────────────────── */

function CommandBlock({ text, copyLabel, label }: { text: string; copyLabel: string; label: string }) {
  const [copied, setCopied] = useState<'ok' | 'error' | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const id = useId()
  useEffect(() => () => { if (timer.current) clearTimeout(timer.current) }, [])

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied('ok')
    } catch {
      setCopied('error')
    }
    if (timer.current) clearTimeout(timer.current)
    timer.current = setTimeout(() => setCopied(null), 2500)
  }

  return (
    <div className="space-y-1.5">
      <p id={id} className="text-xs font-medium" style={{ color: 'var(--text-secondary)' }}>{label}</p>
      <pre
        aria-labelledby={id}
        translate="no"
        tabIndex={0}
        className="focus-ring select-text whitespace-pre-wrap break-all rounded-lg px-3 py-2 font-mono text-[0.72rem] leading-relaxed max-h-48 overflow-y-auto"
        style={{ background: 'var(--bg-elevated)', color: 'var(--text-primary)' }}
      >
        {text}
      </pre>
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={() => { void copy() }}
          className="focus-ring flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-colors bg-[var(--btn-secondary-bg)] hover:bg-[var(--btn-secondary-hov)]"
          style={{ color: 'var(--text-secondary)' }}
        >
          {copied === 'ok' ? <Check aria-hidden="true" className="w-3.5 h-3.5" /> : <Copy aria-hidden="true" className="w-3.5 h-3.5" />}
          {copyLabel}
        </button>
        <span role="status" className="text-xs" style={{ color: copied === 'error' ? 'var(--red)' : 'var(--green)' }}>
          {copied === 'ok' ? 'Copiado' : copied === 'error' ? 'No se pudo copiar: seleccioná el texto y copialo a mano.' : ''}
        </span>
      </div>
    </div>
  )
}

/* ── Chequeos de esta PC (sin contraseña) ────────────────────────────────── */

function ChecksList({ health, loading, onRefresh }: { health: AgentLinuxStatus | null; loading: boolean; onRefresh: () => void }) {
  return (
    <section aria-labelledby="checks-title" aria-busy={loading}>
      <div className="flex items-center justify-between mb-3">
        <h2 id="checks-title" className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--text-secondary)' }}>
          Chequeos de esta PC
        </h2>
        <button
          type="button"
          onClick={onRefresh}
          disabled={loading}
          className="focus-ring flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-medium transition-colors bg-[var(--btn-secondary-bg)] hover:bg-[var(--btn-secondary-hov)] disabled:opacity-60"
          style={{ color: 'var(--text-secondary)' }}
        >
          <RefreshCw aria-hidden="true" className={`w-3 h-3 ${loading ? 'animate-spin motion-reduce:animate-none' : ''}`} />
          Volver a revisar
        </button>
      </div>

      {!health ? (
        <div
          className="flex flex-col items-center justify-center py-8 rounded-2xl border border-dashed text-center px-4"
          style={{ borderColor: 'var(--border-md)', background: 'var(--bg-surface)' }}
        >
          <p className="text-sm font-medium" style={{ color: 'var(--text-secondary)' }}>
            {loading ? 'Revisando…' : 'El agente no responde: todavía no se pueden leer los chequeos.'}
          </p>
          {!loading && (
            <p className="text-xs mt-1" style={{ color: 'var(--text-secondary)' }}>
              Esperá unos segundos a que arranque y tocá «Volver a revisar».
            </p>
          )}
        </div>
      ) : (
        <ul
          className="rounded-2xl border divide-y"
          style={{ background: 'var(--bg-surface)', borderColor: 'var(--border)' }}
        >
          {health.checks.map((c) => {
            const st = CHECK_STATUS[c.status]
            const Icon = st.icon
            return (
              <li key={c.id} className="flex items-start gap-3 px-4 py-2.5" style={{ borderColor: 'var(--border)' }}>
                <Icon aria-hidden="true" className="w-4 h-4 mt-0.5 flex-shrink-0" style={{ color: st.color }} />
                <div className="flex-1 min-w-0">
                  <p className="text-sm" style={{ color: 'var(--text-primary)' }}>
                    {CHECK_LABEL[c.id]}
                    {c.required && c.status !== 'ok' && (
                      <span className="ml-2 text-[0.65rem] font-semibold uppercase tracking-wide" style={{ color: 'var(--red)' }}>
                        Necesario
                      </span>
                    )}
                  </p>
                  {c.status !== 'ok' && c.detail && (
                    <p className="text-xs mt-0.5 break-words select-text" style={{ color: 'var(--text-secondary)' }}>{c.detail}</p>
                  )}
                </div>
                <span className="text-xs font-medium flex-shrink-0" style={{ color: st.color }}>{st.text}</span>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

function PathCode({ children }: { children: string }) {
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
