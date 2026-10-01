import { Component, type ReactNode } from 'react'
import { AlertTriangle, RefreshCw } from 'lucide-react'

interface Props { children: ReactNode }
interface State { error: Error | null }

export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  render() {
    if (!this.state.error) return this.props.children

    return (
      <div className="flex-1 flex items-center justify-center p-8">
        <div
          className="max-w-md w-full rounded-2xl border p-6 space-y-4"
          style={{ background: 'var(--bg-surface)', borderColor: 'rgba(220,38,38,0.2)' }}
        >
          <div className="flex items-center gap-3">
            <AlertTriangle className="w-5 h-5 flex-shrink-0" style={{ color: 'var(--red)' }} />
            <p className="font-semibold" style={{ color: 'var(--text-primary)' }}>Error inesperado</p>
          </div>
          <pre
            className="text-xs font-mono p-3 rounded-xl overflow-auto"
            style={{ background: 'var(--bg-elevated)', color: 'var(--red)', maxHeight: '200px' }}
          >
            {this.state.error.message}
          </pre>
          <button
            onClick={() => this.setState({ error: null })}
            className="flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-medium"
            style={{ background: 'var(--btn-secondary-bg)', color: 'var(--text-secondary)' }}
          >
            <RefreshCw className="w-3.5 h-3.5" /> Reintentar
          </button>
        </div>
      </div>
    )
  }
}
