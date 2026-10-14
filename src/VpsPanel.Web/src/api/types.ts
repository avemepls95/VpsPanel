export type ServerStatus = 'ok' | 'warning' | 'error';

export interface ProviderInfo {
  name: string;
  isManual: boolean;
}

export interface ServerView {
  id: string;
  name: string;
  host: string;
  port: number;
  user: string;
  keyPath: string;
  provider: ProviderInfo | null;
  hostKeyFingerprint: string | null;
}

export interface ServerRequest {
  name: string;
  host: string;
  port: number;
  user: string;
  keyPath: string;
  providerName: string | null;
}

export interface SystemSnapshot {
  hostname: string;
  os: string;
  uptimeSeconds: number;
  cpus: number;
  memTotal: number;
  memUsed: number;
  diskTotal: number;
  diskUsed: number;
}

export interface ContainerView {
  name: string;
  image: string;
  state: string;
  status: string;
  ports: string;
}

export interface CommandView {
  id: string;
  name: string;
  script: string;
}

export interface ComposeProjectView {
  name: string;
  workingDir: string;
  configFiles: string[];
  environmentFile: string | null;
  running: number;
  total: number;
  stopped: boolean;
  containers: ContainerView[];
  commands: CommandView[];
}

export interface TimerView {
  unit: string;
  service: string;
  next: string | null;
  last: string | null;
  result: string | null;
}

export interface ManualProjectView {
  id: string;
  name: string;
  folder: string;
  commands: CommandView[];
}

export interface ServerOverview {
  serverId: string;
  status: ServerStatus;
  error: string | null;
  hostKeyMismatch: boolean;
  system: SystemSnapshot | null;
  dockerAvailable: boolean;
  composeProjects: ComposeProjectView[];
  standaloneContainers: ContainerView[];
  timers: TimerView[];
  manualProjects: ManualProjectView[];
}

export type RunStatus = 'running' | 'succeeded' | 'failed' | 'stopped';

export interface RunView {
  id: string;
  serverId: string;
  projectKey: string | null;
  title: string;
  subtitle: string | null;
  status: RunStatus;
  exitCode: number | null;
  startedAt: string;
  finishedAt: string | null;
  lastLineId: number;
}

export interface RunStarted {
  runId: string;
}

export type FileEntryType = 'file' | 'dir' | 'link' | 'other' | 'missing';

export interface FileEntryView {
  name: string;
  type: FileEntryType;
  targetType: FileEntryType | null;
  linkTarget: string | null;
  size: number;
  modified: string;
  mode: string;
  permissions: string;
}

export interface DirectoryListing {
  path: string;
  entries: FileEntryView[];
}

export type FileNotEditableReason = 'tooLarge' | 'binary' | 'notUtf8';
export type LineSeparator = 'lf' | 'crlf';

export interface FileContentView {
  path: string;
  size: number;
  editable: boolean;
  reason: FileNotEditableReason | null;
  text: string | null;
  sha256: string | null;
  hasBom: boolean;
  lineSeparator: LineSeparator;
}

export interface SaveFileRequest {
  path: string;
  text: string;
  sha256: string;
  hasBom: boolean;
  force: boolean;
}

export interface SaveFileResult {
  sha256: string;
  backupPath: string;
}

export interface UploadResult {
  path: string;
  backupPath: string | null;
}

export interface CopyRequest {
  sourceServerId: string;
  sourcePath: string;
  targetServerId: string;
  targetDir: string;
  overwrite: boolean;
}

export interface CommandRequest {
  name: string;
  script: string;
}

export interface ManualProjectRequest {
  name: string;
  folder: string;
}
