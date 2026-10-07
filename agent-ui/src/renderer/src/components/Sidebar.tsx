import { motion } from 'framer-motion'
import { LayoutDashboard, Package, Settings, Wifi, WifiOff, Server } from 'lucide-react'
import { SelloMark } from './SelloMark'

type View = 'dashboard' | 'libraries' | 'settings'

interface Props {
  view: View
  onView: (v: View) => void
  agentRunning: boolean
  serverConnected: boolean
}

const NAV: { id: View; label: string; icon: React.ElementType }[] = [
  { id: 'dashboard',  label: 'Dashboard',   icon: LayoutDashboard },
  { id: 'libraries',  label: 'Librerías',   icon: Package },
  { id: 'settings',   label: 'Configuración', icon: Settings },
]

export function Sidebar({ view, onView, agentRunning, serverConnected }: Props) {
  return (
    <aside
      className="w-[210px] flex-shrink-0 flex flex-col h-full border-r"
      style={{ background: 'var(--bg-surface)', borderColor: 'var(--border)' }}
    >
      {/* Header — drag region for macOS frameless window */}
      <div
        className="drag-region h-12 flex items-center px-4 gap-2.5 border-b"
        style={{ borderColor: 'var(--border)' }}
      >
        <SelloMark size={28} className="flex-shrink-0" />
        <div className="no-drag">
          <p className="text-[0.78rem] font-bold leading-tight" style={{ color: 'var(--text-primary)' }}>
            Tatana
          </p>
          <p className="text-[0.58rem] leading-tight" style={{ color: 'var(--text-muted)' }}>
            Agente Factum
          </p>
        </div>
      </div>

      {/* Nav */}
      <nav className="flex-1 p-2.5 space-y-0.5 overflow-y-auto">
        <p
          className="text-[0.6rem] font-semibold uppercase tracking-wider px-2 py-1.5 mb-1"
          style={{ color: 'var(--text-muted)' }}
        >
          Menú
        </p>
        {NAV.map(({ id, label, icon: Icon }) => {
          const active = view === id
          return (
            <motion.button
              key={id}
              onClick={() => onView(id)}
              className="relative w-full flex items-center gap-2.5 px-3 py-2 rounded-xl text-left transition-colors"
              style={{
                background: active ? 'rgba(37,99,235,0.09)' : 'transparent',
                color: active ? 'var(--blue)' : 'var(--text-secondary)',
              }}
              whileHover={{ x: active ? 0 : 2 }}
              transition={{ type: 'spring', stiffness: 400, damping: 30 }}
            >
              {active && (
                <motion.div
                  layoutId="nav-pill"
                  className="absolute left-0 top-1/2 -translate-y-1/2 w-[3px] h-5 rounded-full"
                  style={{ background: 'var(--blue)' }}
                />
              )}
              <Icon className="w-4 h-4 flex-shrink-0" />
              <span className="text-[0.78rem] font-medium">{label}</span>
            </motion.button>
          )
        })}
      </nav>

      {/* Status pills */}
      <div className="p-2.5 space-y-1.5 border-t" style={{ borderColor: 'var(--border)' }}>
        {/* .NET Agent */}
        <StatusPill
          icon={Server}
          label="Agente .NET"
          ok={agentRunning}
          okText="Activo"
          failText="Detenido"
        />
        {/* Factum server */}
        <StatusPill
          icon={agentRunning && serverConnected ? Wifi : WifiOff}
          label="Servidor"
          ok={serverConnected}
          okText="Conectado"
          failText="Sin conexión"
        />

        {/* Versión del agente (útil para soporte) */}
        <p translate="no" className="px-2 pt-2 text-[0.58rem] leading-tight tabular-nums" style={{ color: 'var(--text-muted)' }}>
          Factum · Tatana v{__TATANA_VERSION__}
        </p>
      </div>
    </aside>
  )
}

function StatusPill({
  icon: Icon,
  label,
  ok,
  okText,
  failText,
}: {
  icon: React.ElementType
  label: string
  ok: boolean
  okText: string
  failText: string
}) {
  return (
    <div
      className="flex items-center gap-2 px-3 py-2 rounded-xl text-xs border"
      style={{
        background: ok
          ? 'rgba(5,150,105,0.06)'
          : 'rgba(220,38,38,0.06)',
        borderColor: ok
          ? 'rgba(5,150,105,0.2)'
          : 'rgba(220,38,38,0.15)',
        color: ok ? 'var(--green)' : 'var(--red)',
      }}
    >
      <Icon className="w-3 h-3 flex-shrink-0" />
      <span className="font-medium flex-1">{label}</span>
      <span className="text-[0.65rem] opacity-80">{ok ? okText : failText}</span>
      {ok && (
        <span
          className="w-1.5 h-1.5 rounded-full animate-pulse flex-shrink-0"
          style={{ background: 'var(--green)' }}
        />
      )}
    </div>
  )
}
