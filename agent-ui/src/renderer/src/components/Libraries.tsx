import { useState, useEffect, useRef } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { Package, CheckCircle2, Download, RefreshCw, ChevronDown, ChevronUp, Loader2 } from 'lucide-react'

type LibState = 'idle' | 'installing' | 'updating' | 'done_ok' | 'done_fail'

interface LibRow extends TatanaLibrary {
  state: LibState
  logs: string[]
  expanded: boolean
}

export function Libraries() {
  const [libs, setLibs] = useState<LibRow[]>([])
  const [loading, setLoading] = useState(true)

  const refresh = async () => {
    setLoading(true)
    try {
      const data = await window.tatana.getLibraries()
      setLibs((prev) =>
        data.map((lib) => {
          const existing = prev.find((l) => l.id === lib.id)
          return { ...lib, state: existing?.state ?? 'idle', logs: existing?.logs ?? [], expanded: existing?.expanded ?? false }
        }),
      )
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { refresh() }, [])

  const runAction = async (id: string, action: 'install' | 'update') => {
    const newState: LibState = action === 'install' ? 'installing' : 'updating'

    setLibs((prev) =>
      prev.map((l) => l.id === id ? { ...l, state: newState, logs: [], expanded: true } : l),
    )

    window.tatana.onLibProgress(id, ({ line, done, ok }) => {
      if (done) {
        window.tatana.removeAllListeners(`tatana:lib-progress:${id}`)
        setLibs((prev) =>
          prev.map((l) => l.id === id ? { ...l, state: ok ? 'done_ok' : 'done_fail' } : l),
        )
        if (ok) {
          setTimeout(refresh, 1000)
          // Reiniciar el agente para que tome la nueva librería instalada
          window.tatana.restartAgent()
        }
      } else if (line) {
        setLibs((prev) =>
          prev.map((l) => l.id === id ? { ...l, logs: [...l.logs, line] } : l),
        )
      }
    })

    if (action === 'install') await window.tatana.installLibrary(id)
    else await window.tatana.updateLibrary(id)
  }

  const toggleExpand = (id: string) =>
    setLibs((prev) => prev.map((l) => l.id === id ? { ...l, expanded: !l.expanded } : l))

  return (
    <div className="p-6 space-y-5 max-w-2xl">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-lg font-bold" style={{ color: 'var(--text-primary)' }}>
            Librerías
          </h1>
          <p className="text-sm mt-0.5" style={{ color: 'var(--text-secondary)' }}>
            Instalá o actualizá las dependencias del agente
          </p>
        </div>
        <button
          onClick={refresh}
          disabled={loading}
          className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-colors"
          style={{ background: 'var(--btn-secondary-bg)', color: 'var(--text-secondary)' }}
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          Actualizar estado
        </button>
      </div>

      {loading && libs.length === 0 ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="w-6 h-6 animate-spin" style={{ color: 'var(--text-muted)' }} />
        </div>
      ) : (
        <div className="space-y-3">
          {libs.map((lib) => (
            <LibraryCard
              key={lib.id}
              lib={lib}
              onInstall={() => runAction(lib.id, 'install')}
              onUpdate={() => runAction(lib.id, 'update')}
              onToggle={() => toggleExpand(lib.id)}
            />
          ))}
        </div>
      )}
    </div>
  )
}

function LibraryCard({
  lib,
  onInstall,
  onUpdate,
  onToggle,
}: {
  lib: LibRow
  onInstall: () => void
  onUpdate: () => void
  onToggle: () => void
}) {
  const logRef = useRef<HTMLDivElement>(null)
  const busy = lib.state === 'installing' || lib.state === 'updating'

  useEffect(() => {
    if (logRef.current) logRef.current.scrollTop = logRef.current.scrollHeight
  }, [lib.logs])

  const stateColor = {
    idle: lib.installed ? 'var(--green)' : 'var(--text-muted)',
    installing: 'var(--blue)',
    updating: 'var(--blue)',
    done_ok: 'var(--green)',
    done_fail: 'var(--red)',
  }[lib.state]

  const stateLabel = {
    idle:       lib.installed ? `v${lib.version ?? 'instalado'}` : 'No instalado',
    installing: 'Instalando...',
    updating:   'Actualizando...',
    done_ok:    'Completado',
    done_fail:  'Error',
  }[lib.state]

  return (
    <motion.div
      layout
      className="rounded-2xl border overflow-hidden"
      style={{ background: 'var(--bg-surface)', borderColor: 'var(--border)' }}
    >
      {/* Main row */}
      <div className="flex items-center gap-3 px-4 py-3.5">
        {/* Icon */}
        <div
          className="w-9 h-9 rounded-xl flex items-center justify-center flex-shrink-0"
          style={{
            background: lib.installed ? 'rgba(5,150,105,0.1)' : 'var(--bg-elevated)',
          }}
        >
          {lib.installed ? (
            <CheckCircle2 className="w-4.5 h-4.5" style={{ color: 'var(--green)' }} />
          ) : (
            <Package className="w-4.5 h-4.5" style={{ color: 'var(--text-muted)' }} />
          )}
        </div>

        {/* Info */}
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <p className="text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>
              {lib.name}
            </p>
            {busy && <Loader2 className="w-3 h-3 animate-spin" style={{ color: 'var(--blue)' }} />}
          </div>
          <p className="text-xs mt-0.5" style={{ color: 'var(--text-secondary)' }}>
            {lib.description}
          </p>
        </div>

        {/* State badge */}
        <span className="text-[0.65rem] font-medium px-2 py-0.5 rounded-full flex-shrink-0" style={{ color: stateColor, background: `${stateColor}15` }}>
          {stateLabel}
        </span>

        {/* Actions */}
        <div className="flex items-center gap-1.5 flex-shrink-0">
          {!lib.installed && !busy && (
            <button
              onClick={onInstall}
              className="flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-semibold text-white transition-opacity hover:opacity-90"
              style={{ background: 'var(--blue)' }}
            >
              <Download className="w-3 h-3" /> Instalar
            </button>
          )}
          {lib.installed && !busy && (
            <button
              onClick={onUpdate}
              className="flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-medium transition-colors"
              style={{ background: 'var(--btn-secondary-bg)', color: 'var(--text-secondary)' }}
            >
              <RefreshCw className="w-3 h-3" /> Actualizar
            </button>
          )}
          {lib.logs.length > 0 && (
            <button
              onClick={onToggle}
              className="p-1.5 rounded-lg transition-colors"
              style={{ background: 'var(--btn-secondary-bg)', color: 'var(--text-muted)' }}
            >
              {lib.expanded ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
            </button>
          )}
        </div>
      </div>

      {/* Progress bar */}
      {busy && (
        <div className="h-0.5 w-full" style={{ background: 'var(--bg-elevated)' }}>
          <motion.div
            className="h-full rounded-full"
            style={{ background: 'var(--blue)' }}
            animate={{ scaleX: [0.1, 0.9, 0.1], originX: 0 }}
            transition={{ duration: 1.6, repeat: Infinity, ease: 'easeInOut' }}
          />
        </div>
      )}

      {/* Log output */}
      <AnimatePresence>
        {lib.expanded && lib.logs.length > 0 && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.2 }}
          >
            <div
              ref={logRef}
              className="px-4 pb-4 font-mono text-[0.65rem] overflow-y-auto space-y-0.5"
              style={{
                background: 'var(--bg-elevated)',
                color: 'var(--text-secondary)',
                maxHeight: '160px',
              }}
            >
              <div className="pt-3" />
              {lib.logs.map((line, i) => (
                <p key={i} className="leading-relaxed whitespace-pre-wrap break-all">{line}</p>
              ))}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </motion.div>
  )
}
