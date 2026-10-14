import type { ReactNode } from 'react';
import { Badge, Box, Button, Card, Group, Menu, Stack, Table, Text, Title, Tooltip } from '@mantine/core';
import {
  IconBox, IconChevronDown, IconClock, IconDownload, IconPencil, IconPlayerPlayFilled, IconStack2, IconTerminal2, IconTrash,
} from '@tabler/icons-react';
import type { CommandView, ComposeProjectView, ContainerView, ManualProjectView, TimerView } from '../api/types';
import { formatWhen } from '../format';
import { unitColors } from '../theme';
import { ContainerDot } from './ServerSummary';

type UnitKind = keyof typeof unitColors;

const icons: Record<UnitKind, ReactNode> = {
  compose: <IconStack2 size={20} />,
  manual: <IconTerminal2 size={20} />,
  standalone: <IconBox size={20} />,
  timers: <IconClock size={20} />,
};

interface ShellProps {
  kind: UnitKind;
  kindLabel: string;
  name: string;
  path?: string;
  pill?: ReactNode;
  wide?: boolean;
  footer?: ReactNode;
  children?: ReactNode;
}

function UnitShell({ kind, kindLabel, name, path, pill, wide, footer, children }: ShellProps) {
  return (
    <Card
      withBorder
      radius={14}
      p={0}
      shadow="xs"
      style={{ gridColumn: wide ? '1 / -1' : undefined, display: 'flex', flexDirection: 'column', minWidth: 0 }}
    >
      <Group align="flex-start" wrap="nowrap" gap={12} p={16}>
        <Box
          w={40}
          h={40}
          style={{
            flex: '0 0 40px', borderRadius: 11, display: 'flex', alignItems: 'center', justifyContent: 'center',
            background: unitColors[kind].bg, color: unitColors[kind].fg,
          }}
        >
          {icons[kind]}
        </Box>
        <Box style={{ flex: 1, minWidth: 0 }}>
          <Text fz={11} fw={600} tt="uppercase" c="dimmed" style={{ letterSpacing: '0.08em' }}>{kindLabel}</Text>
          <Title order={3} fz={17} style={{ overflowWrap: 'anywhere' }}>{name}</Title>
          {path && <Text ff="monospace" fz={12} c="dimmed" style={{ overflowWrap: 'anywhere' }}>{path}</Text>}
        </Box>
        {pill}
      </Group>
      {children}
      {footer && (
        <Group gap={8} px={16} py={12} mt="auto" bg="#FAFBFC" style={{ borderTop: '1px solid #E6E8ED' }}>
          {footer}
        </Group>
      )}
    </Card>
  );
}

interface CommandActions {
  busy: boolean;
  onRun: (command: CommandView) => void;
  onAdd: () => void;
  onEdit: (command: CommandView) => void;
  onDelete: (command: CommandView) => void;
  onFiles: () => void;
}

function CommandButton({ command, busy, onRun, onEdit, onDelete }: { command: CommandView } & Pick<CommandActions, 'busy' | 'onRun' | 'onEdit' | 'onDelete'>) {
  return (
    <Button.Group>
      <Button size="xs" h={36} variant="light" disabled={busy} leftSection={<IconPlayerPlayFilled size={12} />} onClick={() => onRun(command)}>
        {command.name}
      </Button>
      <Menu position="bottom-end" withinPortal>
        <Menu.Target>
          <Button size="xs" h={36} variant="light" px={8} aria-label={`Actions for command ${command.name}`}>
            <IconChevronDown size={14} />
          </Button>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Item leftSection={<IconPencil size={14} />} onClick={() => onEdit(command)}>Edit</Menu.Item>
          <Menu.Item color="red" leftSection={<IconTrash size={14} />} onClick={() => onDelete(command)}>Delete</Menu.Item>
        </Menu.Dropdown>
      </Menu>
    </Button.Group>
  );
}

function CommandFooter({ commands, actions, leading, trailing }: { commands: CommandView[]; actions: CommandActions; leading?: ReactNode; trailing?: ReactNode }) {
  return (
    <>
      {leading}
      {commands.map((command) => (
        <CommandButton key={command.id} command={command} busy={actions.busy} onRun={actions.onRun} onEdit={actions.onEdit} onDelete={actions.onDelete} />
      ))}
      <Button size="xs" h={36} variant="default" style={{ borderStyle: 'dashed' }} onClick={actions.onAdd}>+ command</Button>
      <Button size="xs" h={36} variant="default" onClick={actions.onFiles}>Files</Button>
      {trailing}
    </>
  );
}

function ContainersTable({ containers }: { containers: ContainerView[] }) {
  return (
    <Box px={16} pb={14} style={{ overflowX: 'auto' }}>
      <Table fz={13} verticalSpacing={6} withRowBorders>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Container</Table.Th>
            <Table.Th>Image</Table.Th>
            <Table.Th>State</Table.Th>
            <Table.Th>Ports</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {containers.map((container) => (
            <Table.Tr key={container.name}>
              <Table.Td ff="monospace" style={{ whiteSpace: 'nowrap' }}>
                <Group gap={8} wrap="nowrap">
                  <ContainerDot container={container} />
                  {container.name}
                </Group>
              </Table.Td>
              <Table.Td ff="monospace" c="#4A505C" style={{ whiteSpace: 'nowrap' }}>{container.image}</Table.Td>
              <Table.Td style={{ whiteSpace: 'nowrap' }}>{container.status}</Table.Td>
              <Table.Td ff="monospace" c="#4A505C" style={{ whiteSpace: 'nowrap' }}>{container.ports}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Box>
  );
}

export function ComposeCard({ project, actions, onUpdate }: { project: ComposeProjectView; actions: CommandActions; onUpdate: () => void }) {
  const fully = project.running === project.total;
  const pill = project.stopped
    ? <Badge color="yellow" variant="light" tt="none">not running</Badge>
    : <Badge color={fully ? 'green' : 'yellow'} variant="light" tt="none">running {project.running} of {project.total}</Badge>;
  return (
    <UnitShell
      kind="compose"
      kindLabel="Compose project"
      name={project.name}
      path={project.workingDir}
      pill={pill}
      wide
      footer={
        <CommandFooter
          commands={project.commands}
          actions={actions}
          leading={
            <Tooltip label="docker compose pull && docker compose up -d" withArrow>
              <Button size="xs" h={36} disabled={actions.busy} leftSection={<IconDownload size={14} />} onClick={onUpdate}>
                Update images
              </Button>
            </Tooltip>
          }
        />
      }
    >
      {project.stopped
        ? <Box mx={16} mb={14} p="10px 12px" bg="#FDF0D9" c="#6E4200" fz={14} style={{ borderRadius: 10 }}>The project is stopped, there are no containers on the server.</Box>
        : <ContainersTable containers={project.containers} />}
    </UnitShell>
  );
}

export function ManualCard({ project, actions, onEditProject, onDeleteProject }: {
  project: ManualProjectView;
  actions: CommandActions;
  onEditProject: () => void;
  onDeleteProject: () => void;
}) {
  return (
    <UnitShell
      kind="manual"
      kindLabel="Manual project"
      name={project.name}
      path={project.folder}
      footer={
        <CommandFooter
          commands={project.commands}
          actions={actions}
          trailing={
            <>
              <Button size="xs" h={36} variant="default" onClick={onEditProject}>Edit</Button>
              <Button size="xs" h={36} variant="default" c="red" onClick={onDeleteProject}>Delete</Button>
            </>
          }
        />
      }
    />
  );
}

export function StandaloneCard({ containers }: { containers: ContainerView[] }) {
  const running = containers.filter((c) => c.state === 'running').length;
  return (
    <UnitShell
      kind="standalone"
      kindLabel="No compose"
      name="Containers"
      pill={<Badge color={running === containers.length ? 'green' : 'yellow'} variant="light" tt="none">running {running} of {containers.length}</Badge>}
    >
      <Stack gap={0} px={16} pb={14}>
        {containers.map((container) => (
          <Group key={container.name} justify="space-between" py={9} gap="4px 12px" style={{ borderTop: '1px solid #F0F1F4' }}>
            <Stack gap={1}>
              <Group gap={8} wrap="nowrap">
                <ContainerDot container={container} />
                <Text ff="monospace" fz={13} fw={600}>{container.name}</Text>
              </Group>
              <Text ff="monospace" fz={12} c="dimmed" pl={15}>{container.image}</Text>
            </Stack>
            <Text ff="monospace" fz={12} c="#4A505C">{container.ports}</Text>
          </Group>
        ))}
      </Stack>
    </UnitShell>
  );
}

function timerSchedule(timer: TimerView): string {
  const next = timer.next ? `next ${formatWhen(timer.next)}` : 'no next run scheduled';
  const last = timer.last ? `last ${formatWhen(timer.last)}` : 'has not run yet';
  return `${next} · ${last}`;
}

export function TimersCard({ timers }: { timers: TimerView[] }) {
  return (
    <UnitShell kind="timers" kindLabel="systemd" name="Timers">
      <Stack gap={0} px={16} pb={14}>
        {timers.map((timer) => (
          <Stack key={timer.unit} gap={4} py={9} style={{ borderTop: '1px solid #F0F1F4' }}>
            <Group justify="space-between" gap={8}>
              <Text ff="monospace" fz={13} fw={600}>{timer.unit}</Text>
              {timer.result && (
                <Badge color={timer.result === 'success' ? 'green' : 'red'} variant="light" tt="none">
                  {timer.result === 'success' ? 'succeeded' : timer.result}
                </Badge>
              )}
            </Group>
            <Text fz={13} c="dimmed">{timerSchedule(timer)}</Text>
          </Stack>
        ))}
      </Stack>
    </UnitShell>
  );
}

export type { CommandActions };
