import { useState } from 'react';
import { Alert, Badge, Group, Select, Stack, Text, TextInput } from '@mantine/core';
import { ApiError } from '../api/client';
import { api } from '../api/endpoints';
import type { FileEntryView, ServerView } from '../api/types';
import { ConfirmModal } from '../common/ConfirmModal';
import { notifyError } from '../common/notify';
import { useRuns } from '../runs/RunsContext';

const fileExistsPrefix = 'The destination folder already has a file';

interface Props {
  servers: ServerView[];
  sourceServer: ServerView;
  sourcePath: string;
  entry: FileEntryView;
  defaultTargetDir: string;
  onClose: () => void;
}

export function CopyModal({ servers, sourceServer, sourcePath, entry, defaultTargetDir, onClose }: Props) {
  const runs = useRuns();
  const [targetServerId, setTargetServerId] = useState<string | null>(servers.find((s) => s.id !== sourceServer.id)?.id ?? sourceServer.id);
  const [targetDir, setTargetDir] = useState(defaultTargetDir);
  const [conflict, setConflict] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const isFile = entry.type === 'file';

  const copy = async () => {
    if (!targetServerId) return;
    setLoading(true);
    try {
      const started = await api.copy({
        sourceServerId: sourceServer.id,
        sourcePath,
        targetServerId,
        targetDir,
        overwrite: conflict !== null,
      });
      await runs.track(Promise.resolve(started));
      onClose();
    } catch (error) {
      if (error instanceof ApiError && error.status === 409 && isFile && conflict === null && error.message.startsWith(fileExistsPrefix)) setConflict(error.message);
      else notifyError(error);
    } finally {
      setLoading(false);
    }
  };

  return (
    <ConfirmModal
      title="Copy to server"
      confirmLabel={conflict ? 'Replace' : 'Copy'}
      loading={loading}
      confirmDisabled={!targetServerId || !targetDir.startsWith('/')}
      onConfirm={() => void copy()}
      onClose={onClose}
    >
      <Stack gap="sm">
        <Stack gap={4}>
          <Text size="sm" fw={600} c="dimmed">What to copy</Text>
          <Group gap={8}>
            <Badge color="dark" radius="sm" tt="none">{sourceServer.name}</Badge>
            <Text ff="monospace" fz={13} style={{ overflowWrap: 'anywhere' }}>{sourcePath}</Text>
          </Group>
        </Stack>
        <Select
          label="Destination server"
          data={servers.map((s) => ({ value: s.id, label: s.name }))}
          value={targetServerId}
          onChange={(value) => {
            setTargetServerId(value);
            setConflict(null);
          }}
          allowDeselect={false}
        />
        <TextInput
          label="Destination folder"
          value={targetDir}
          onChange={(e) => {
            setTargetDir(e.currentTarget.value);
            setConflict(null);
          }}
          styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)' } }}
        />
        {conflict && (
          <Alert color="yellow">
            {conflict} The previous version will be saved to a backup copy on this computer.
          </Alert>
        )}
      </Stack>
    </ConfirmModal>
  );
}
