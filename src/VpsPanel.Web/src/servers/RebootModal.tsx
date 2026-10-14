import { useState } from 'react';
import { Group, Stack, Text } from '@mantine/core';
import { api } from '../api/endpoints';
import type { ServerView } from '../api/types';
import { ConfirmModal } from '../common/ConfirmModal';
import { useRuns } from '../runs/RunsContext';
import { Dot } from './ServerSummary';

export function RebootModal({ server, onClose }: { server: ServerView; onClose: () => void }) {
  const runs = useRuns();
  const [loading, setLoading] = useState(false);
  const active = runs.activeFor(server.id);

  const reboot = async () => {
    setLoading(true);
    const started = await runs.track(api.reboot(server.id));
    setLoading(false);
    if (started) onClose();
  };

  return (
    <ConfirmModal title={`Reboot "${server.name}"?`} confirmLabel="Reboot" danger loading={loading} onConfirm={() => void reboot()} onClose={onClose}>
      {active.length > 0 && (
        <Stack gap={8}>
          <Text c="#4A505C">Currently running on the server:</Text>
          {active.map((run) => (
            <Group key={run.id} gap={10} p="10px 12px" bg="#EEF1FF" style={{ borderRadius: 10 }}>
              <Dot color="#2F4FE0" />
              <Text fw={600}>{run.title}</Text>
            </Group>
          ))}
          <Text c="#4A505C">The run will be interrupted by the reboot.</Text>
        </Stack>
      )}
    </ConfirmModal>
  );
}
