export const IPC_INVOKE = 'switcher:invoke'
export const IPC_SELECT_CODEX_HOME = 'switcher:select-codex-home'
export const IPC_UPDATE_GET = 'switcher:update:get'
export const IPC_UPDATE_CHECK = 'switcher:update:check'
export const IPC_UPDATE_DOWNLOAD = 'switcher:update:download'
export const IPC_UPDATE_INSTALL = 'switcher:update:install'
export const IPC_UPDATE_OPEN_RELEASE = 'switcher:update:open-release'
export const allowedIpcChannels = [
  IPC_INVOKE,
  IPC_SELECT_CODEX_HOME,
  IPC_UPDATE_GET,
  IPC_UPDATE_CHECK,
  IPC_UPDATE_DOWNLOAD,
  IPC_UPDATE_INSTALL,
  IPC_UPDATE_OPEN_RELEASE
] as const
