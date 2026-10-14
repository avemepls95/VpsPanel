import { request } from './client';
import type {
  CommandRequest, CommandView, CopyRequest, DirectoryListing, FileContentView, ManualProjectRequest,
  ManualProjectView, RunStarted, RunView, SaveFileRequest, SaveFileResult, ServerOverview, ServerRequest,
  ServerView, UploadResult,
} from './types';

const q = encodeURIComponent;
const server = (id: string) => `/api/servers/${q(id)}`;

export const api = {
  settingsStatus: () => request<{ error: string | null }>('GET', '/api/settings/status'),

  servers: () => request<ServerView[]>('GET', '/api/servers'),
  createServer: (body: ServerRequest) => request<ServerView>('POST', '/api/servers', body),
  updateServer: (id: string, body: ServerRequest) => request<ServerView>('PUT', server(id), body),
  deleteServer: (id: string) => request<void>('DELETE', server(id)),
  overview: (id: string) => request<ServerOverview>('GET', `${server(id)}/overview`),
  refreshProvider: (id: string) => request<ServerView>('POST', `${server(id)}/provider/refresh`),
  resetHostKey: (id: string) => request<ServerView>('POST', `${server(id)}/host-key/reset`),
  reboot: (id: string) => request<RunStarted>('POST', `${server(id)}/reboot`),

  updateCompose: (id: string, project: string) =>
    request<RunStarted>('POST', `${server(id)}/compose/${q(project)}/update`),
  addComposeCommand: (id: string, project: string, body: CommandRequest) =>
    request<CommandView>('POST', `${server(id)}/compose/${q(project)}/commands`, body),
  updateComposeCommand: (id: string, project: string, commandId: string, body: CommandRequest) =>
    request<CommandView>('PUT', `${server(id)}/compose/${q(project)}/commands/${q(commandId)}`, body),
  deleteComposeCommand: (id: string, project: string, commandId: string) =>
    request<void>('DELETE', `${server(id)}/compose/${q(project)}/commands/${q(commandId)}`),

  addManualProject: (id: string, body: ManualProjectRequest) =>
    request<ManualProjectView>('POST', `${server(id)}/manual-projects`, body),
  updateManualProject: (id: string, projectId: string, body: ManualProjectRequest) =>
    request<ManualProjectView>('PUT', `${server(id)}/manual-projects/${q(projectId)}`, body),
  deleteManualProject: (id: string, projectId: string) =>
    request<void>('DELETE', `${server(id)}/manual-projects/${q(projectId)}`),
  addManualCommand: (id: string, projectId: string, body: CommandRequest) =>
    request<CommandView>('POST', `${server(id)}/manual-projects/${q(projectId)}/commands`, body),
  updateManualCommand: (id: string, projectId: string, commandId: string, body: CommandRequest) =>
    request<CommandView>('PUT', `${server(id)}/manual-projects/${q(projectId)}/commands/${q(commandId)}`, body),
  deleteManualCommand: (id: string, projectId: string, commandId: string) =>
    request<void>('DELETE', `${server(id)}/manual-projects/${q(projectId)}/commands/${q(commandId)}`),
  runCommand: (id: string, commandId: string) =>
    request<RunStarted>('POST', `${server(id)}/commands/${q(commandId)}/run`),

  runs: () => request<RunView[]>('GET', '/api/runs'),
  stopRun: (runId: string) => request<void>('POST', `/api/runs/${q(runId)}/stop`),

  listFiles: (id: string, path: string) => request<DirectoryListing>('GET', `${server(id)}/files?path=${q(path)}`),
  readFile: (id: string, path: string) => request<FileContentView>('GET', `${server(id)}/files/content?path=${q(path)}`),
  saveFile: (id: string, body: SaveFileRequest) => request<SaveFileResult>('PUT', `${server(id)}/files/content`, body),
  uploadFile: (id: string, dir: string, file: File, overwrite: boolean) => {
    const form = new FormData();
    form.append('file', file, file.name);
    return request<UploadResult>('POST', `${server(id)}/files/upload?dir=${q(dir)}&overwrite=${overwrite}`, form);
  },
  deleteFile: (id: string, path: string, confirmName: string | null) =>
    request<void>('DELETE', `${server(id)}/files?path=${q(path)}${confirmName === null ? '' : `&confirmName=${q(confirmName)}`}`),
  downloadUrl: (id: string, path: string) => `${server(id)}/files/download?path=${q(path)}`,
  copy: (body: CopyRequest) => request<RunStarted>('POST', '/api/files/copy', body),
};
