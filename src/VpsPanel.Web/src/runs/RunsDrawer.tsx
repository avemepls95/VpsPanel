import { useEffect, useState } from 'react';
import { Badge, Box, Button, Drawer, Group, ScrollArea, Stack, Text, Title, Tooltip, UnstyledButton } from '@mantine/core';
import { IconPlayerStopFilled } from '@tabler/icons-react';
import { api } from '../api/endpoints';
import type { RunStatus, RunView } from '../api/types';
import { notifyError } from '../common/notify';
import { formatDuration, formatWhen } from '../format';
import { ui } from '../theme';
import { RunOutput } from './RunOutput';
import { useRuns } from './RunsContext';

const statusColor: Record<RunStatus, string> = {
  running: 'brand',
  succeeded: 'green',
  failed: 'red',
  stopped: 'gray',
};

const dotColor: Record<RunStatus, string> = {
  running: '#2F4FE0',
  succeeded: ui.ok,
  failed: ui.bad,
  stopped: ui.neutral,
};

function useNow(active: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [active]);
  return now;
}

function statusText(run: RunView, now: number): string {
  const started = new Date(run.startedAt).getTime();
  const duration = formatDuration((run.finishedAt ? new Date(run.finishedAt).getTime() : now) - started);
  switch (run.status) {
    case 'running':
      return `running · ${duration}`;
    case 'succeeded':
      return `succeeded · ${duration}`;
    case 'failed':
      return run.exitCode === null ? `failed · ${duration}` : `failed · exit code ${run.exitCode} · ${duration}`;
    case 'stopped':
      return `stopped · ${duration}`;
  }
}

export function RunsDrawer() {
  const { runs, opened, selectedId, open, close, refresh } = useRuns();
  const [stopping, setStopping] = useState(false);
  const now = useNow(runs.some((run) => run.status === 'running'));
  const selected = runs.find((run) => run.id === selectedId) ?? null;

  const stop = async () => {
    if (!selected) return;
    setStopping(true);
    try {
      await api.stopRun(selected.id);
      await refresh();
    } catch (error) {
      notifyError(error);
    } finally {
      setStopping(false);
    }
  };

  return (
    <Drawer
      opened={opened}
      onClose={close}
      position="right"
      size={660}
      title="Runs"
      styles={{
        title: { fontFamily: 'var(--mantine-font-family-headings)', fontWeight: 700, fontSize: 'var(--mantine-h3-font-size)', lineHeight: 'var(--mantine-h3-line-height)' },
        content: { display: 'flex', flexDirection: 'column' },
        body: { flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 },
      }}
    >
      <ScrollArea.Autosize mah="35vh" mb="md" style={{ flex: '0 0 auto' }}>
        <Stack gap={4}>
          {runs.length === 0 && <Text c="dimmed">No runs yet</Text>}
          {runs.map((run) => (
            <UnstyledButton
              key={run.id}
              onClick={() => open(run.id)}
              p="sm"
              style={{ borderRadius: 12, background: run.id === selectedId ? '#EEF1FF' : undefined }}
            >
              <Group wrap="nowrap" gap="sm">
                <Box w={10} h={10} style={{ borderRadius: '50%', background: dotColor[run.status], flex: '0 0 10px' }} />
                <Box style={{ flex: 1, minWidth: 0 }}>
                  <Text fw={600} truncate>{run.title}</Text>
                  <Text size="sm" c="dimmed" truncate>
                    {[run.subtitle, formatWhen(run.startedAt)].filter(Boolean).join(' · ')}
                  </Text>
                </Box>
                <Badge color={statusColor[run.status]} variant="light" style={{ flex: '0 0 auto' }}>{statusText(run, now)}</Badge>
              </Group>
            </UnstyledButton>
          ))}
        </Stack>
      </ScrollArea.Autosize>

      {selected && (
        <Stack gap="sm" style={{ flex: 1, minHeight: 0 }}>
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Box style={{ minWidth: 0 }}>
              <Title order={4}>{selected.title}</Title>
              {selected.subtitle && <Text size="sm" c="dimmed">{selected.subtitle}</Text>}
              <Badge mt={6} color={statusColor[selected.status]} variant="light">{statusText(selected, now)}</Badge>
            </Box>
            {selected.status === 'running' && (
              <Tooltip
                multiline
                w={300}
                label="Stopping closes the connection. A command that prints output will end, while a silent one may keep running on the server."
              >
                <Button
                  variant="outline"
                  color="red"
                  loading={stopping}
                  leftSection={<IconPlayerStopFilled size={12} />}
                  onClick={() => void stop()}
                >
                  Stop
                </Button>
              </Tooltip>
            )}
          </Group>
          <RunOutput runId={selected.id} onEnd={refresh} />
        </Stack>
      )}
    </Drawer>
  );
}
