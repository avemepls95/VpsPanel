import { Badge, Box, Group, Progress, Stack, Text, Tooltip } from '@mantine/core';
import type { ContainerView, ServerOverview, ServerStatus, SystemSnapshot } from '../api/types';
import { formatUsage } from '../format';
import { ui } from '../theme';

const statusColors: Record<ServerStatus, string> = { ok: ui.ok, warning: ui.warn, error: ui.bad };

export function Dot({ color, size = 8 }: { color: string; size?: number }) {
  return <Box w={size} h={size} style={{ borderRadius: '50%', background: color, flex: `0 0 ${size}px` }} />;
}

export function ContainerDot({ container }: { container: ContainerView }) {
  const running = container.state === 'running';
  return (
    <Tooltip label={running ? 'Container is running' : 'Container is not running'} withArrow>
      <Box style={{ display: 'flex' }}>
        <Dot color={running ? ui.ok : ui.warn} size={7} />
      </Box>
    </Tooltip>
  );
}

function statusHint(overview: ServerOverview): string[] {
  if (overview.status === 'error')
    return [overview.hostKeyMismatch ? 'Server key changed' : 'No SSH connection'];
  if (overview.status === 'ok')
    return ['All containers are running, timers finished without errors'];

  const problems = [
    ...overview.composeProjects
      .filter((p) => p.stopped || p.running < p.total)
      .map((p) => (p.stopped ? `Project ${p.name} is not running` : `Project ${p.name}: running ${p.running} of ${p.total}`)),
    ...overview.standaloneContainers.filter((c) => c.state !== 'running').map((c) => `Container ${c.name} is not running`),
    ...overview.timers.filter((t) => t.result !== null && t.result !== 'success').map((t) => `Timer ${t.unit} failed`),
  ];
  return ['There are problems', ...problems];
}

export function StatusLed({ overview }: { overview: ServerOverview }) {
  const color = statusColors[overview.status];
  return (
    <Tooltip label={statusHint(overview).map((line) => <div key={line}>{line}</div>)} withArrow multiline maw={360}>
      <Box
        w={12}
        h={12}
        style={{ borderRadius: '50%', background: color, flex: '0 0 12px', boxShadow: `0 0 0 4px ${color}2E` }}
      />
    </Tooltip>
  );
}

interface Chip {
  key: string;
  label: string;
  meta: string;
  color: string;
  hint: string;
}

export function SummaryChips({ overview }: { overview: ServerOverview }) {
  if (overview.status === 'error') {
    return (
      <Badge color="red" variant="light" radius="sm" size="lg" tt="none">
        {overview.hostKeyMismatch ? 'server key changed' : 'no SSH connection'}
      </Badge>
    );
  }

  const chips: Chip[] = [
    ...overview.composeProjects.map((project) => ({
      key: `compose-${project.name}`,
      label: project.name,
      meta: project.stopped ? 'not running' : `running ${project.running}/${project.total}`,
      color: project.stopped || project.running < project.total ? ui.warn : ui.ok,
      hint: project.stopped
        ? 'Compose project is not running'
        : project.running < project.total ? 'Not all project containers are running' : 'All project containers are running',
    })),
    ...overview.manualProjects.map((project) => ({
      key: `manual-${project.id}`,
      label: project.name,
      meta: '',
      color: ui.neutral,
      hint: 'A project added manually. The panel does not track its state',
    })),
  ];
  if (overview.standaloneContainers.length > 0) {
    const allRunning = overview.standaloneContainers.every((c) => c.state === 'running');
    chips.push({
      key: 'standalone',
      label: 'no compose',
      meta: String(overview.standaloneContainers.length),
      color: allRunning ? ui.ok : ui.warn,
      hint: allRunning ? 'All containers without compose are running' : 'Not all containers without compose are running',
    });
  }
  if (overview.timers.length > 0) {
    const failed = overview.timers.some((t) => t.result !== null && t.result !== 'success');
    chips.push({
      key: 'timers',
      label: 'timers',
      meta: String(overview.timers.length),
      color: failed ? ui.warn : ui.ok,
      hint: failed ? 'The last run of one of the timers failed' : 'Timers finished without errors',
    });
  }

  return (
    <Group gap={6}>
      {chips.map((chip) => (
        <Tooltip key={chip.key} label={chip.hint} withArrow>
          <Group gap={6} wrap="nowrap" px={10} h={26} bg="#F1F2F5" style={{ borderRadius: 8 }}>
            <Dot color={chip.color} />
            <Text size="sm">{chip.label}</Text>
            {chip.meta && <Text size="sm" c="dimmed">{chip.meta}</Text>}
          </Group>
        </Tooltip>
      ))}
    </Group>
  );
}

function Meter({ label, used, total }: { label: string; used: number; total: number }) {
  return (
    <Stack gap={6} w={140}>
      <Group justify="space-between" gap={8} wrap="nowrap">
        <Text size="xs" fw={600} c="dimmed" style={{ letterSpacing: '0.04em' }}>{label}</Text>
        <Text size="xs" ff="monospace">{formatUsage(used, total)}</Text>
      </Group>
      <Progress value={total > 0 ? (used / total) * 100 : 0} size={4} />
    </Stack>
  );
}

export function UsageMeters({ system }: { system: SystemSnapshot }) {
  return (
    <Group gap={20}>
      <Meter label="RAM" used={system.memUsed} total={system.memTotal} />
      <Meter label="DISK" used={system.diskUsed} total={system.diskTotal} />
    </Group>
  );
}
