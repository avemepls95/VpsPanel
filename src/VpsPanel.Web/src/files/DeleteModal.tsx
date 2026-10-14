import { useState } from 'react';
import { Code, Stack, Text, Textarea, TextInput } from '@mantine/core';
import { api } from '../api/endpoints';
import type { FileEntryView } from '../api/types';
import { ConfirmModal } from '../common/ConfirmModal';
import { notifyError } from '../common/notify';

interface Props {
  serverId: string;
  path: string;
  entry: FileEntryView;
  onClose: () => void;
  onDeleted: () => void;
}

export function DeleteModal({ serverId, path, entry, onClose, onDeleted }: Props) {
  const isFolder = entry.type === 'dir';
  const [typed, setTyped] = useState('');
  const [loading, setLoading] = useState(false);

  const remove = async () => {
    setLoading(true);
    try {
      await api.deleteFile(serverId, path, isFolder ? typed : null);
      onDeleted();
    } catch (error) {
      notifyError(error);
    } finally {
      setLoading(false);
    }
  };

  return (
    <ConfirmModal
      title={isFolder ? `Delete folder ${entry.name}?` : `Delete ${entry.name}?`}
      confirmLabel="Delete"
      danger
      loading={loading}
      confirmDisabled={isFolder && typed !== entry.name}
      onConfirm={() => void remove()}
      onClose={onClose}
    >
      <Stack gap="sm">
        <Text c="#4A505C">
          {isFolder ? 'The folder ' : ''}<Code>{path}</Code>{isFolder ? ' will be deleted with all its contents and cannot be restored.' : ' will be deleted and cannot be restored.'}
        </Text>
        {isFolder && (entry.name.includes('\n')
          ? (
            <Textarea
              label="Enter the folder name to confirm"
              value={typed}
              onChange={(e) => setTyped(e.currentTarget.value)}
              autosize
              minRows={2}
              styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)' } }}
              data-autofocus
            />
          )
          : (
            <TextInput
              label="Enter the folder name to confirm"
              value={typed}
              onChange={(e) => setTyped(e.currentTarget.value)}
              styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)' } }}
              data-autofocus
            />
          ))}
      </Stack>
    </ConfirmModal>
  );
}
