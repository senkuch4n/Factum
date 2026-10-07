import { contextBridge, ipcRenderer } from 'electron'

contextBridge.exposeInMainWorld('tatana', {
  // Config
  getConfig: () => ipcRenderer.invoke('tatana:get-config'),
  saveConfig: (patch: Record<string, unknown>) => ipcRenderer.invoke('tatana:save-config', patch),

  // Agent process
  getAgentStatus: () => ipcRenderer.invoke('tatana:agent-status'),
  restartAgent: () => ipcRenderer.invoke('tatana:restart-agent'),

  // Server connection check
  checkConnection: (url: string) => ipcRenderer.invoke('tatana:check-connection', url),

  // Devices
  getDevices: (port?: number) => ipcRenderer.invoke('tatana:get-devices', port),

  // Libraries
  getLibraries: () => ipcRenderer.invoke('tatana:get-libraries'),
  installLibrary: (id: string) => ipcRenderer.invoke('tatana:install-library', id),
  updateLibrary: (id: string) => ipcRenderer.invoke('tatana:update-library', id),

  // App y actualizaciones (SDD §6.7)
  getAppInfo: () => ipcRenderer.invoke('tatana:get-app-info'),
  getUpdateState: () => ipcRenderer.invoke('tatana:get-update-state'),
  checkUpdates: () => ipcRenderer.invoke('tatana:check-updates'),
  installUpdate: () => ipcRenderer.invoke('tatana:install-update'),
  dismissNotice: (id: 'migration' | 'local_config') => ipcRenderer.invoke('tatana:dismiss-notice', id),

  // Events (one-way: main → renderer)
  onAgentLog: (cb: (lines: string[]) => void) =>
    ipcRenderer.on('tatana:agent-log-batch', (_e, lines) => cb(lines)),
  onAgentStatusChange: (cb: (status: string) => void) =>
    ipcRenderer.on('tatana:agent-status-change', (_e, s) => cb(s)),
  onLibProgress: (
    id: string,
    cb: (p: { line: string; done: boolean; ok: boolean }) => void,
  ) => ipcRenderer.on(`tatana:lib-progress:${id}`, (_e, p) => cb(p)),

  onUpdateStateChange: (cb: (s: unknown) => void) =>
    ipcRenderer.on('tatana:update-state-change', (_e, s) => cb(s)),
  onAppInfoChange: (cb: (info: unknown) => void) =>
    ipcRenderer.on('tatana:app-info-change', (_e, info) => cb(info)),

  removeAllListeners: (channel: string) => ipcRenderer.removeAllListeners(channel),
})
