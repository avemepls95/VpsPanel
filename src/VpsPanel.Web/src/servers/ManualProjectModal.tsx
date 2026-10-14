import { useState } from 'react';
import { Button, Group, Modal, Stack, TextInput } from '@mantine/core';
import { api } from '../api/endpoints';
import type { ManualProjectView } from '../api/types';
import { notifyError } from '../common/notify';

interface Props {
  serverId: string;
  project: ManualProjectView | null;
  onClose: () => void;
  onSaved: () => void;
}

export function ManualProjectModal({ serverId, project, onClose, onSaved }: Props) {
  const [name, setName] = useState(project?.name ?? '');
  const [folder, setFolder] = useState(project?.folder ?? '/root/app/');
  const [saving, setSaving] = useState(false);

  const save = async () => {
    setSaving(true);
    try {
      if (project) await api.updateManualProject(serverId, project.id, { name, folder });
      else await api.addManualProject(serverId, { name, folder });
      onSaved();
    } catch (error) {
      notifyError(error);
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal opened onClose={onClose} centered radius="lg" title={project ? 'Manual project' : 'New manual project'}>
      <Stack>
        <TextInput label="Name" value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
        <TextInput
          label="Folder on the server"
          value={folder}
          onChange={(e) => setFolder(e.currentTarget.value)}
          styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)' } }}
        />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>Cancel</Button>
          <Button loading={saving} onClick={() => void save()}>Save</Button>
        </Group>
      </Stack>
    </Modal>
  );
}
