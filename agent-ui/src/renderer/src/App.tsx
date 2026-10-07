import { useState, useEffect } from 'react'
import { Sidebar } from './components/Sidebar'
import { Dashboard } from './components/Dashboard'
import { Libraries } from './components/Libraries'
import { Settings } from './components/Settings'
import { ErrorBoundary } from './components/ErrorBoundary'
import { useTatanaUpdates } from './hooks/useTatanaUpdates'

type View = 'dashboard' | 'libraries' | 'settings'

export default function App() {
  const [view, setView] = useState<View>('dashboard')
  const [config, setConfig] = useState<TatanaConfig | null>(null)
  const [agentStatus, setAgentStatus] = useState('unknown')
  const [serverConnected, setServerConnected] = useState(false)
  const { appInfo, update } = useTatanaUpdates()

  // Load config on mount
  useEffect(() => {
    if (!window.tatana) return          // preload no disponible
    window.tatana.getConfig().then(setConfig).catch(console.error)
    window.tatana.getAgentStatus().then(({ running }) =>
      setAgentStatus(running ? 'running' : 'stopped'),
    ).catch(console.error)
    window.tatana.onAgentStatusChange(setAgentStatus)
    return () => window.tatana.removeAllListeners('tatana:agent-status-change')
  }, [])

  // Poll server connection
  useEffect(() => {
    if (!config?.serverUrl) return
    const check = () =>
      window.tatana.checkConnection(config.serverUrl).then(({ ok }) => setServerConnected(ok))
    check()
    const id = setInterval(check, 15_000)
    return () => clearInterval(id)
  }, [config?.serverUrl])

  return (
    <div className="flex h-screen overflow-hidden" style={{ background: 'var(--bg-base)' }}>
      <Sidebar
        view={view}
        onView={setView}
        agentRunning={agentStatus === 'running'}
        serverConnected={serverConnected}
        appVersion={appInfo?.appVersion}
        agentVersion={appInfo?.agentVersion}
      />
      <main className="flex-1 overflow-y-auto">
        <ErrorBoundary>
          {view === 'dashboard' && (
            <Dashboard agentStatus={agentStatus} config={config} update={update} appInfo={appInfo} />
          )}
          {view === 'libraries' && <Libraries />}
          {view === 'settings' && (
            <Settings config={config} onSave={setConfig} />
          )}
        </ErrorBoundary>
      </main>
    </div>
  )
}
