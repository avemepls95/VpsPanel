import { useState } from 'react';
import { Button, Group, Modal, Stack, Text, TextInput, Textarea } from '@mantine/core';
import { api } from '../api/endpoints';
import type { CommandView } from '../api/types';
import { notifyError } from '../common/notify';
import { ui } from '../theme';

export type CommandTarget =
  | { type: 'compose'; project: string; folder: string }
  | { type: 'manual'; projectId: string; projectName: string; folder: string };

interface Props {
  serverId: string;
  target: CommandTarget;
  command: CommandView | null;
  onClose: () => void;
  onSaved: () => void;
  onDelete: (command: CommandView) => void;
}

export function CommandModal({ serverId, target, command, onClose, onSaved, onDelete }: Props) {
  const [name, setName] = useState(command?.name ?? '');
  const [script, setScript] = useState(command?.script ?? '');
  const [saving, setSaving] = useState(false);
  const projectName = target.type === 'compose' ? target.project : target.projectName;

  const save = async () => {
    setSaving(true);
    try {
      const body = { name, script };
      if (target.type === 'compose') {
        if (command) await api.updateComposeCommand(serverId, target.project, command.id, body);
        else await api.addComposeCommand(serverId, target.project, body);
      } else if (command) {
        await api.updateManualCommand(serverId, target.projectId, command.id, body);
      } else {
        await api.addManualCommand(serverId, target.projectId, body);
      }
      onSaved();
    } catch (error) {
      notifyError(error);
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal opened onClose={onClose} centered radius="lg" size="lg" title={`Command of project ${projectName}`}>
      <Stack>
        <TextInput label="Name" value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
        <Textarea
          label="Script"
          description={`Runs in folder ${target.folder}`}
          value={script}
          onChange={(e) => setScript(e.currentTarget.value)}
          autosize
          minRows={3}
          styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)', background: ui.console, color: '#E6E8EE' } }}
        />
        <Group justify="space-between">
          {command ? (
            <Button variant="outline" color="red" onClick={() => onDelete(command)}>Delete command</Button>
          ) : <Text />}
          <Group>
            <Button variant="default" onClick={onClose}>Cancel</Button>
            <Button loading={saving} onClick={() => void save()}>Save</Button>
          </Group>
        </Group>
      </Stack>
    </Modal>
  );
}
