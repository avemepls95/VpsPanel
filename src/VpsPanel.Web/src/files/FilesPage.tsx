import { useCallback, useEffect, useRef, useState } from 'react';
import {
  Alert, Anchor, Box, Breadcrumbs, Button, Container, FileButton, Group, Loader, Paper, Select, Table, Text, TextInput, UnstyledButton,
} from '@mantine/core';
import { IconFile, IconFolder, IconLink, IconUpload } from '@tabler/icons-react';
import { useNavigate, useSearchParams } from 'react-router';
import { api } from '../api/endpoints';
import type { DirectoryListing, FileEntryView, ServerView } from '../api/types';
import { AppHeader } from '../common/AppHeader';
import { ConfirmModal } from '../common/ConfirmModal';
import { notifyError, notifySuccess } from '../common/notify';
import { childPath, formatModified, formatSize } from '../format';
import { CopyModal } from './CopyModal';
import { DeleteModal } from './DeleteModal';

type Dialog =
  | { kind: 'delete'; entry: FileEntryView }
  | { kind: 'copy'; entry: FileEntryView }
  | { kind: 'replace'; file: File };

const effectiveType = (entry: FileEntryView) => (entry.type === 'link' ? entry.targetType : entry.type);

function EntryIcon({ entry }: { entry: FileEntryView }) {
  const [Icon, bg, fg]: [typeof IconFile, string, string] =
    entry.type === 'link' ? [IconLink, '#E8ECFF', '#2F4FE0']
      : entry.type === 'dir' ? [IconFolder, '#FDF0D9', '#9A5B00']
        : [IconFile, '#F1F2F5', '#5B6170'];
  return (
    <Box w={28} h={28} style={{ flex: '0 0 28px', borderRadius: 8, display: 'flex', alignItems: 'center', justifyContent: 'center', background: bg, color: fg }}>
      <Icon size={15} />
    </Box>
  );
}

export function FilesPage() {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const [servers, setServers] = useState<ServerView[]>([]);
  const [listing, setListing] = useState<DirectoryListing | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [uploading, setUploading] = useState(false);
  const loadId = useRef(0);
  const resetUpload = useRef<() => void>(null);

  const serverId = params.get('server') ?? servers[0]?.id ?? '';
  const path = params.get('path') ?? '/';
  const [pathInput, setPathInput] = useState(path);
  const server = servers.find((s) => s.id === serverId) ?? null;

  useEffect(() => {
    api.servers().then(setServers).catch(notifyError);
  }, []);

  useEffect(() => setPathInput(path), [path]);

  const load = useCallback(async () => {
    if (!serverId) return;
    const id = ++loadId.current;
    setLoading(true);
    setError(null);
    setSelected(null);
    try {
      const result = await api.listFiles(serverId, path);
      if (id === loadId.current) setListing(result);
    } catch (e) {
      if (id !== loadId.current) return;
      setListing(null);
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      if (id === loadId.current) setLoading(false);
    }
  }, [serverId, path]);

  useEffect(() => {
    void load();
  }, [load]);

  const go = (nextPath: string, nextServer = serverId) => setParams({ server: nextServer, path: nextPath });

  const open = (entry: FileEntryView) => {
    const full = childPath(path, entry.name);
    const type = effectiveType(entry);
    if (type === 'dir') go(full);
    else if (type === 'file') navigate(`/editor?server=${encodeURIComponent(serverId)}&path=${encodeURIComponent(full)}`);
  };

  const upload = async (file: File, overwrite: boolean) => {
    setUploading(true);
    try {
      const result = await api.uploadFile(serverId, path, file, overwrite);
      notifySuccess('File uploaded', result.backupPath ? `Previous version: ${result.backupPath}` : result.path);
      setDialog(null);
      await load();
    } catch (e) {
      notifyError(e);
    } finally {
      setUploading(false);
    }
  };

  const startUpload = (file: File | null) => {
    resetUpload.current?.();
    if (!file) return;
    if (listing?.entries.some((entry) => entry.name === file.name)) setDialog({ kind: 'replace', file });
    else void upload(file, false);
  };

  const segments = path.split('/').filter(Boolean);
  const crumbs = [
    <Anchor key="/" ff="monospace" onClick={() => go('/')}>/</Anchor>,
    ...segments.map((segment, index) => {
      const target = '/' + segments.slice(0, index + 1).join('/');
      return index === segments.length - 1
        ? <Text key={target} ff="monospace" fw={600}>{segment}</Text>
        : <Anchor key={target} ff="monospace" onClick={() => go(target)}>{segment}</Anchor>;
    }),
  ];

  return (
    <>
      <AppHeader section="Files" />
      <Container size={1240} py={24}>
        <Paper withBorder radius={16} p="sm" mb="sm">
          <Group align="flex-end" gap={12}>
            <Select
              label="Server"
              w={200}
              data={servers.map((s) => ({ value: s.id, label: s.name }))}
              value={serverId || null}
              onChange={(value) => value && go('/', value)}
              allowDeselect={false}
            />
            <Group align="flex-end" gap={8} style={{ flex: '1 1 380px' }}>
              <TextInput
                label="Path"
                style={{ flex: 1 }}
                value={pathInput}
                onChange={(e) => setPathInput(e.currentTarget.value)}
                onKeyDown={(e) => e.key === 'Enter' && go(pathInput)}
                styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)' } }}
              />
              <Button variant="default" onClick={() => go(pathInput)}>Go</Button>
            </Group>
            <FileButton resetRef={resetUpload} onChange={startUpload}>
              {(props) => (
                <Button {...props} loading={uploading} disabled={!listing} leftSection={<IconUpload size={16} />}>
                  Upload file
                </Button>
              )}
            </FileButton>
          </Group>
        </Paper>

        <Breadcrumbs separator="›" mb="sm">{crumbs}</Breadcrumbs>

        {error && <Alert color="red" mb="sm">{error}</Alert>}
        {loading && <Loader size="sm" />}

        {listing && !loading && (
          <Paper withBorder radius={16} style={{ overflowX: 'auto' }}>
            <Table highlightOnHover verticalSpacing={6} fz={14}>
              <Table.Thead bg="#F6F7F9">
                <Table.Tr>
                  <Table.Th>Name</Table.Th>
                  <Table.Th ta="right">Size</Table.Th>
                  <Table.Th>Modified</Table.Th>
                  <Table.Th>Permissions</Table.Th>
                  <Table.Th><Box component="span" style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>Actions</Box></Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {listing.entries.length === 0 && (
                  <Table.Tr><Table.Td colSpan={5}><Text c="dimmed">The folder is empty</Text></Table.Td></Table.Tr>
                )}
                {listing.entries.map((entry) => {
                  const isSelected = selected === entry.name;
                  const type = effectiveType(entry);
                  const full = childPath(path, entry.name);
                  return (
                    <Table.Tr key={entry.name} bg={isSelected ? '#EEF1FF' : undefined}>
                      <Table.Td style={{ whiteSpace: 'nowrap' }}>
                        <UnstyledButton onClick={() => setSelected(entry.name)} onDoubleClick={() => open(entry)}>
                          <Group gap={10} wrap="nowrap">
                            <EntryIcon entry={entry} />
                            <Text ff="monospace" fz={13} fw={500} style={{ whiteSpace: 'pre' }}>{entry.name}{type === 'dir' ? '/' : ''}</Text>
                            {entry.type === 'link' && <Text ff="monospace" fz={12} c="dimmed">→ {entry.linkTarget}</Text>}
                          </Group>
                        </UnstyledButton>
                      </Table.Td>
                      <Table.Td ta="right" c="#4A505C" style={{ whiteSpace: 'nowrap' }}>{type === 'file' ? formatSize(entry.size) : ''}</Table.Td>
                      <Table.Td c="#4A505C" style={{ whiteSpace: 'nowrap' }}>{formatModified(entry.modified)}</Table.Td>
                      <Table.Td style={{ whiteSpace: 'nowrap' }}>
                        <Text ff="monospace" fz={12} c={entry.permissions === 'rw-------' ? '#5A2FC0' : '#4A505C'}>{entry.permissions}</Text>
                      </Table.Td>
                      <Table.Td style={{ whiteSpace: 'nowrap' }}>
                        {isSelected && (
                          <Group gap={6} wrap="nowrap">
                            {(type === 'file' || type === 'dir') && <Button size="xs" onClick={() => open(entry)}>Open</Button>}
                            {(type === 'file' || type === 'dir') && (
                              <Button size="xs" variant="default" component="a" href={api.downloadUrl(serverId, full)} download>
                                {type === 'dir' ? 'Download as archive' : 'Download'}
                              </Button>
                            )}
                            {(type === 'file' || type === 'dir') && (
                              <Button size="xs" variant="default" onClick={() => setDialog({ kind: 'copy', entry })}>Copy to server</Button>
                            )}
                            <Button size="xs" variant="outline" color="red" onClick={() => setDialog({ kind: 'delete', entry })}>Delete</Button>
                          </Group>
                        )}
                      </Table.Td>
                    </Table.Tr>
                  );
                })}
              </Table.Tbody>
            </Table>
          </Paper>
        )}
      </Container>

      {dialog?.kind === 'delete' && (
        <DeleteModal
          serverId={serverId}
          path={childPath(path, dialog.entry.name)}
          entry={dialog.entry}
          onClose={() => setDialog(null)}
          onDeleted={() => {
            setDialog(null);
            void load();
          }}
        />
      )}
      {dialog?.kind === 'copy' && server && (
        <CopyModal
          servers={servers}
          sourceServer={server}
          sourcePath={childPath(path, dialog.entry.name)}
          entry={dialog.entry}
          defaultTargetDir={path}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === 'replace' && (
        <ConfirmModal
          title={`Replace ${dialog.file.name}?`}
          confirmLabel="Replace"
          loading={uploading}
          onConfirm={() => void upload(dialog.file, true)}
          onClose={() => setDialog(null)}
        >
          <Text c="#4A505C">A file with this name already exists. The previous version will be saved to a backup copy on this computer.</Text>
        </ConfirmModal>
      )}
    </>
  );
}
