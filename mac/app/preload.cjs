const {contextBridge, ipcRenderer} = require('electron');
contextBridge.exposeInMainWorld('dit', Object.freeze({
  api: (method, route, body, token) => ipcRenderer.invoke('api', method, route, body, token),
  pick: (kind) => ipcRenderer.invoke('pick', kind),
  preview: (project, asset, token, thumbnail) => ipcRenderer.invoke('preview', project, asset, token, thumbnail),
  connectAdobe: (project, token) => ipcRenderer.invoke('adobe', project, token),
  exportReport: (project, token, csv) => ipcRenderer.invoke('export', project, token, csv),
  ready: () => ipcRenderer.invoke('ready')
}));
