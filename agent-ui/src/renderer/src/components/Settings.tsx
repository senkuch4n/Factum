import { useState, useEffect } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { Save, CheckCircle2, XCircle, Loader2, Power, Link } from 'lucide-react'

interface Props {
  config: TatanaConfig | null
  onSave: (config: TatanaConfig) => void
  appInfo?: TatanaAppInfo | null
  onView?: (v: 'linux') => void
}

export function Settings({ config, onSave, appInfo, onView }: Props) {
  const isLinux = appInfo?.platform === 'linux'
  // tatana-linux D-T18: en Linux el autoarranque necesita la instalación canónica.
  const autostartBlocked = isLinux && appInfo?.packaged === true && appInfo.linux?.installed === false
  const [serverUrl, setServerUrl] = useState('')
  const [agentPort, setAgentPort] = useState(8765)
  const [autostart, setAutostart] = useState(false)
  const [checking, setChecking] = useState(false)
  const [checkResult, setCheckResult] = useState<{ ok: boolean; ms: number; error?: string } | null>(null)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (config) {
      setServerUrl(config.serverUrl)
      setAgentPort(config.agentPort)
      setAutostart(config.autostart)
    }
  }, [config])

  const testConnection = async () => {
    if (!serverUrl.trim()) return
    setChecking(true)
    setCheckResult(null)
    try {
      const result = await window.tatana.checkConnection(serverUrl)
      setCheckResult(result)
    } finally {
      setChecking(false)
    }
  }

  const save = async () => {
    setSaving(true)
    try {
      const updated = await window.tatana.saveConfig({ serverUrl, agentPort, autostart })
      onSave(updated)
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="p-6 space-y-6 max-w-lg">
      <div>
        <h1 className="text-lg font-bold" style={{ color: 'var(--text-primary)' }}>
          Configuración
        </h1>
        <p className="text-sm mt-0.5" style={{ color: 'var(--text-secondary)' }}>
          Ajustes del agente y conexión con el servidor
        </p>
      </div>

      {/* Server URL */}
      <Section icon={Link} title="Servidor Factum">
        <label className="block text-xs font-medium mb-1.5" style={{ color: 'var(--text-secondary)' }}>
          URL del servidor
        </label>
        <div className="flex gap-2">
          <input
            type="url"
            value={serverUrl}
            onChange={(e) => { setServerUrl(e.target.value); setCheckResult(null) }}
            placeholder="https://factum.example.com"
            className="flex-1 px-3 py-2 rounded-xl text-sm outline-none border transition-colors"
            style={{
              background: 'var(--bg-elevated)',
              borderColor: checkResult
                ? checkResult.ok ? 'rgba(5,150,105,0.4)' : 'rgba(220,38,38,0.4)'
                : 'var(--border-md)',
              color: 'var(--text-primary)',
            }}
            onKeyDown={(e) => e.key === 'Enter' && testConnection()}
          />
          <button
            onClick={testConnection}
            disabled={checking || !serverUrl.trim()}
            className="px-3 py-2 rounded-xl text-xs font-medium transition-colors flex items-center gap-1.5 flex-shrink-0"
            style={{ background: 'var(--btn-secondary-bg)', color: 'var(--text-secondary)' }}
          >
            {checking ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : 'Probar'}
          </button>
        </div>

        <AnimatePresence>
          {checkResult && (
            <motion.p
              initial={{ opacity: 0, y: -4 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0 }}
              className="flex items-center gap-1.5 text-xs mt-2"
              style={{ color: checkResult.ok ? 'var(--green)' : 'var(--red)' }}
            >
              {checkResult.ok
                ? <><CheckCircle2 className="w-3.5 h-3.5" /> Conectado · {checkResult.ms} ms</>
                : <><XCircle className="w-3.5 h-3.5" /> {checkResult.error ?? 'Sin respuesta'}</>
              }
            </motion.p>
          )}
        </AnimatePresence>
      </Section>

      {/* Agent port */}
      <Section icon={Link} title="Puerto del agente local">
        <label className="block text-xs font-medium mb-1.5" style={{ color: 'var(--text-secondary)' }}>
          Puerto HTTP (default: 8765)
        </label>
        <input
          type="number"
          value={agentPort}
          onChange={(e) => setAgentPort(Number(e.target.value))}
          min={1024}
          max={65535}
          className="w-32 px-3 py-2 rounded-xl text-sm outline-none border"
          style={{
            background: 'var(--bg-elevated)',
            borderColor: 'var(--border-md)',
            color: 'var(--text-primary)',
          }}
        />
        <p className="text-xs mt-1.5" style={{ color: 'var(--text-muted)' }}>
          Puerto en el que el agente .NET escucha localmente
        </p>
      </Section>

      {/* Autostart */}
      <Section icon={Power} title="Inicio automático">
        <div className="flex items-center justify-between">
          <div>
            <p id="autostart-label" className="text-sm font-medium" style={{ color: 'var(--text-primary)' }}>
              {isLinux ? 'Iniciar al entrar a la sesión' : 'Iniciar con Windows / macOS'}
            </p>
            <p id="autostart-help" className="text-xs mt-0.5" style={{ color: 'var(--text-secondary)' }}>
              {autostartBlocked
                ? 'Primero instalá Tatana en esta PC: el arranque automático usa la copia instalada.'
                : 'Tatana y el agente arrancan al prender la PC'}
            </p>
            {autostartBlocked && onView && (
              <button
                type="button"
                onClick={() => onView('linux')}
                className="focus-ring mt-1.5 text-xs font-medium underline underline-offset-2 rounded"
                style={{ color: 'var(--blue)' }}
              >
                Ir a «Esta PC»
              </button>
            )}
          </div>
          <Toggle
            value={autostart && !autostartBlocked}
            onChange={setAutostart}
            disabled={autostartBlocked}
            labelledBy="autostart-label"
            describedBy="autostart-help"
          />
        </div>
      </Section>

      {/* Save */}
      <div className="flex items-center gap-3 pt-1">
        <button
          onClick={save}
          disabled={saving}
          className="flex items-center gap-2 px-5 py-2.5 rounded-xl text-sm font-semibold text-white transition-opacity hover:opacity-90 disabled:opacity-60"
          style={{ background: 'var(--blue)' }}
        >
          {saving
            ? <Loader2 className="w-4 h-4 animate-spin" />
            : <Save className="w-4 h-4" />
          }
          Guardar configuración
        </button>

        <AnimatePresence>
          {saved && (
            <motion.span
              initial={{ opacity: 0, x: -6 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0 }}
              className="flex items-center gap-1.5 text-sm"
              style={{ color: 'var(--green)' }}
            >
              <CheckCircle2 className="w-4 h-4" /> Guardado
            </motion.span>
          )}
        </AnimatePresence>
      </div>
    </div>
  )
}

function Section({ icon: Icon, title, children }: { icon: React.ElementType; title: string; children: React.ReactNode }) {
  return (
    <div
      className="rounded-2xl border p-4 space-y-3"
      style={{ background: 'var(--bg-surface)', borderColor: 'var(--border)' }}
    >
      <div className="flex items-center gap-2 mb-1">
        <Icon className="w-4 h-4" style={{ color: 'var(--blue)' }} />
        <p className="text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>{title}</p>
      </div>
      {children}
    </div>
  )
}

function Toggle({
  value, onChange, disabled = false, labelledBy, describedBy,
}: {
  value: boolean
  onChange: (v: boolean) => void
  disabled?: boolean
  labelledBy?: string
  describedBy?: string
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={value}
      aria-labelledby={labelledBy}
      aria-describedby={describedBy}
      disabled={disabled}
      onClick={() => onChange(!value)}
      className="focus-ring relative w-11 h-6 rounded-full transition-colors flex-shrink-0 disabled:opacity-50 disabled:cursor-not-allowed"
      style={{ background: value ? 'var(--blue)' : 'var(--bg-elevated)', border: '1px solid var(--border-md)' }}
    >
      <motion.span
        className="absolute top-0.5 w-5 h-5 rounded-full bg-white shadow-sm"
        animate={{ left: value ? '22px' : '2px' }}
        transition={{ type: 'spring', stiffness: 500, damping: 30 }}
      />
    </button>
  )
}
