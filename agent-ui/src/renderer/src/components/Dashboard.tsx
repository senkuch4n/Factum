import { useState, useEffect, useCallback } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import {
  Server, Wifi, WifiOff, Smartphone, RefreshCw,
  AlertTriangle, CheckCircle2, XCircle, Loader2,
} from 'lucide-react'

interface Props {
  agentStatus: string
  config: TatanaConfig | null
}

export function Dashboard({ agentStatus, config }: Props) {
  const [devices, setDevices] = useState<TatanaDevice[]>([])
  const [loadingDevices, setLoadingDevices] = useState(false)
  const [serverResult, setServerResult] = useState<{ ok: boolean; ms: number; error?: string } | null>(null)
  const [checkingServer, setCheckingServer] = useState(false)
  const [logs, setLogs] = useState<string[]>([])

  const fetchDevices = useCallback(async () => {
    if (!config) return
    setLoadingDevices(true)
    try {
      const devs = await window.tatana.getDevices(config.agentPort)
      setDevices(Array.isArray(devs) ? devs : [])
    } finally {
      setLoadingDevices(false)
    }
  }, [config])

  const checkServer = useCallback(async () => {
    if (!config?.serverUrl) return
    setCheckingServer(true)
    try {
      const result = await window.tatana.checkConnection(config.serverUrl)
      setServerResult(result)
    } finally {
      setCheckingServer(false)
    }
  }, [config?.serverUrl])

  useEffect(() => {
    fetchDevices()
    checkServer()
    const interval = setInterval(() => { fetchDevices(); checkServer() }, 5000)
    return () => clearInterval(interval)
  }, [fetchDevices, checkServer])

  useEffect(() => {
    window.tatana.onAgentLog((lines) => {
      setLogs((prev) => [...prev, ...lines].slice(-200))
    })
    return () => window.tatana.removeAllListeners('tatana:agent-log-batch')
  }, [])

  const agentRunning = agentStatus === 'running'
  const agentStarting = agentStatus === 'starting'
  const agentMissing = agentStatus === 'missing'

  return (
    <div className="p-6 space-y-5 max-w-3xl">
      <div>
        <h1 className="text-lg font-bold" style={{ color: 'var(--text-primary)' }}>
          Dashboard
        </h1>
        <p className="text-sm mt-0.5" style={{ color: 'var(--text-secondary)' }}>
          Estado del agente y dispositivos conectados
        </p>
      </div>

      {/* Status row */}
      <div className="grid grid-cols-2 gap-4">
        {/* Agent card */}
        <StatusCard
          title="Agente .NET"
          subtitle={
            agentMissing ? 'Binario no encontrado' :
            agentStarting ? 'Iniciando...' :
            agentRunning ? `Puerto :${config?.agentPort ?? 8765}` :
            'Detenido'
          }
          state={agentMissing ? 'error' : agentStarting ? 'loading' : agentRunning ? 'ok' : 'error'}
          icon={Server}
          action={!agentRunning && !agentStarting ? {
            label: 'Reiniciar',
            onClick: () => window.tatana.restartAgent(),
          } : undefined}
        />

        {/* Server card */}
        <StatusCard
          title="Servidor Factum"
          subtitle={
            checkingServer ? 'Verificando...' :
            !config?.serverUrl ? 'No configurado' :
            serverResult?.ok ? `${serverResult.ms} ms · ${config.serverUrl}` :
            serverResult?.error ?? 'Sin respuesta'
          }
          state={checkingServer ? 'loading' : serverResult?.ok ? 'ok' : 'error'}
          icon={serverResult?.ok ? Wifi : WifiOff}
          action={{ label: 'Reintentar', onClick: checkServer }}
        />
      </div>

      {/* Agent missing alert */}
      <AnimatePresence>
        {agentMissing && (
          <motion.div
            initial={{ opacity: 0, y: -8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
            className="flex items-start gap-3 px-4 py-3 rounded-xl border"
            style={{
              background: 'rgba(217,119,6,0.06)',
              borderColor: 'rgba(217,119,6,0.2)',
            }}
          >
            <AlertTriangle className="w-4 h-4 mt-0.5 flex-shrink-0" style={{ color: 'var(--amber)' }} />
            <div>
              <p className="text-sm font-semibold" style={{ color: 'var(--amber)' }}>
                Binario Factum.Agent no encontrado
              </p>
              <p className="text-xs mt-0.5" style={{ color: 'var(--text-secondary)' }}>
                Compilá el proyecto .NET primero:{' '}
                <code
                  className="px-1.5 py-0.5 rounded text-xs font-mono"
                  style={{ background: 'rgba(217,119,6,0.1)', color: 'var(--amber)' }}
                >
                  dotnet build server/src/Factum.Agent
                </code>
              </p>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Devices */}
      <div>
        <div className="flex items-center justify-between mb-3">
          <p className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--text-muted)' }}>
            Dispositivos conectados
          </p>
          <button
            onClick={fetchDevices}
            disabled={loadingDevices}
            className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-medium transition-colors"
            style={{
              background: 'var(--btn-secondary-bg)',
              color: 'var(--text-secondary)',
            }}
          >
            <RefreshCw className={`w-3 h-3 ${loadingDevices ? 'animate-spin' : ''}`} />
            Actualizar
          </button>
        </div>

        {devices.length === 0 ? (
          <div
            className="flex flex-col items-center justify-center py-10 rounded-2xl border border-dashed"
            style={{ borderColor: 'var(--border-md)', background: 'var(--bg-surface)' }}
          >
            <Smartphone className="w-8 h-8 mb-3 opacity-30" style={{ color: 'var(--text-muted)' }} />
            <p className="text-sm font-medium" style={{ color: 'var(--text-muted)' }}>
              {agentRunning ? 'Sin dispositivos conectados' : 'Agente no activo'}
            </p>
            <p className="text-xs mt-1" style={{ color: 'var(--text-muted)' }}>
              Conectá un celular por USB
            </p>
          </div>
        ) : (
          <div className="space-y-2">
            {devices.map((d) => (
              <motion.div
                key={d.serial}
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                className="flex items-center gap-3 px-4 py-3 rounded-xl border"
                style={{ background: 'var(--bg-surface)', borderColor: 'var(--border)' }}
              >
                <div
                  className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0"
                  style={{
                    background: d.platform === 'ios'
                      ? 'rgba(37,99,235,0.1)'
                      : 'rgba(5,150,105,0.1)',
                  }}
                >
                  <Smartphone
                    className="w-4 h-4"
                    style={{ color: d.platform === 'ios' ? 'var(--blue)' : 'var(--green)' }}
                  />
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-semibold truncate" style={{ color: 'var(--text-primary)' }}>
                    {d.model ?? d.serial}
                  </p>
                  <p className="text-xs" style={{ color: 'var(--text-muted)' }}>
                    {(d.platform ?? 'unknown').toUpperCase()} · {d.state ?? 'conectado'}
                  </p>
                </div>
                <span
                  className="text-[0.65rem] font-medium px-2 py-0.5 rounded-full"
                  style={{
                    background: 'rgba(5,150,105,0.1)',
                    color: 'var(--green)',
                  }}
                >
                  Listo
                </span>
              </motion.div>
            ))}
          </div>
        )}
      </div>

      {/* Log console */}
      {logs.length > 0 && (
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider mb-2" style={{ color: 'var(--text-muted)' }}>
            Log del agente
          </p>
          <div
            className="rounded-xl p-3 font-mono text-[0.68rem] overflow-y-auto space-y-0.5"
            style={{
              background: 'var(--bg-elevated)',
              color: 'var(--text-secondary)',
              maxHeight: '140px',
            }}
          >
            {logs.map((l, i) => (
              <p key={i} className="leading-relaxed">{l}</p>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

function StatusCard({
  title,
  subtitle,
  state,
  icon: Icon,
  action,
}: {
  title: string
  subtitle: string
  state: 'ok' | 'error' | 'loading'
  icon: React.ElementType
  action?: { label: string; onClick: () => void }
}) {
  const colors = {
    ok:      { bg: 'rgba(5,150,105,0.07)',  border: 'rgba(5,150,105,0.2)',  icon: 'var(--green)', status: CheckCircle2 },
    error:   { bg: 'rgba(220,38,38,0.07)',  border: 'rgba(220,38,38,0.15)', icon: 'var(--red)',   status: XCircle },
    loading: { bg: 'rgba(37,99,235,0.07)',  border: 'rgba(37,99,235,0.15)', icon: 'var(--blue)',  status: Loader2 },
  }[state]

  const StatusIcon = colors.status

  return (
    <div
      className="rounded-2xl border p-4 space-y-3"
      style={{ background: colors.bg, borderColor: colors.border }}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="flex items-center gap-2.5">
          <div
            className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0"
            style={{ background: 'var(--bg-surface)' }}
          >
            <Icon className="w-4 h-4" style={{ color: colors.icon }} />
          </div>
          <div>
            <p className="text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>{title}</p>
            <p className="text-xs mt-0.5 line-clamp-1" style={{ color: 'var(--text-secondary)' }}>{subtitle}</p>
          </div>
        </div>
        <StatusIcon
          className={`w-4 h-4 flex-shrink-0 mt-0.5 ${state === 'loading' ? 'animate-spin' : ''}`}
          style={{ color: colors.icon }}
        />
      </div>

      {action && (
        <button
          onClick={action.onClick}
          className="w-full text-xs font-medium py-1.5 rounded-lg transition-colors"
          style={{
            background: 'var(--btn-secondary-bg)',
            color: 'var(--text-secondary)',
          }}
        >
          {action.label}
        </button>
      )}
    </div>
  )
}
