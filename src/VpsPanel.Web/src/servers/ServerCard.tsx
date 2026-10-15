import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ActionIcon, Badge, Box, Button, Card, Code, Collapse, Group, Loader, Stack, Text, Tooltip } from '@mantine/core';
import { IconChevronDown, IconRefresh } from '@tabler/icons-react';
import { useNavigate } from 'react-router';
import { api } from '../api/endpoints';
import type { CommandView, ManualProjectView, ServerOverview, ServerView } from '../api/types';
import { ConfirmModal } from '../common/ConfirmModal';
import { notifyError } from '../common/notify';
import { formatUptime, plural } from '../format';
import { useRuns } from '../runs/RunsContext';
import { ui } from '../theme';
import { CommandModal, type CommandTarget } from './CommandModal';
import { ManualProjectModal } from './ManualProjectModal';
import { RebootModal } from './RebootModal';
import { StatusLed, SummaryChips, UsageMeters } from './ServerSummary';
import { ComposeCard, ManualCard, StandaloneCard, TimersCard, type CommandActions } from './UnitCards';

export type OverviewState = { status: 'loading' } | { status: 'loaded'; overview: ServerOverview };

type Dialog =
  | { kind: 'confirm'; title: string; body: ReactNode; confirmLabel: string; danger?: boolean; action: () => Promise<unknown> }
  | { kind: 'command'; target: CommandTarget; command: CommandView | null }
  | { kind: 'manual'; project: ManualProjectView | null }
  | { kind: 'reboot' };

interface Props {
  server: ServerView;
  state: OverviewState | undefined;
  onRefresh: () => void;
  onEdit: () => void;
}

export function ServerCard({ server, state, onRefresh, onEdit }: Props) {
  const [opened, setOpened] = useState(false);
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [confirming, setConfirming] = useState(false);
  const runs = useRuns();
  const navigate = useNavigate();
  const overview = state?.status === 'loaded' ? state.overview : null;

  // After any run on this server finishes, the overview reloads. A run that managed to finish before the first
  // poll shows up right away in its final status, so runs started after the card was mounted are counted too.
  const mountedAt = useRef(Date.now());
  const active = useRef(new Set<string>());
  const handled = useRef(new Set<string>());
  useEffect(() => {
    let finished = false;
    for (const run of runs.runs.filter((r) => r.serverId === server.id)) {
      if (run.status === 'running') {
        active.current.add(run.id);
      } else if (!handled.current.has(run.id) && (active.current.delete(run.id) || new Date(run.startedAt).getTime() >= mountedAt.current)) {
        handled.current.add(run.id);
        finished = true;
      }
    }
    if (finished) onRefresh();
  }, [runs.runs, server.id, onRefresh]);

  const openFiles = (path: string) => navigate(`/files?server=${encodeURIComponent(server.id)}&path=${encodeURIComponent(path)}`);
  const closeDialog = () => setDialog(null);
  const saved = () => {
    setDialog(null);
    onRefresh();
  };

  const confirm = async (action: () => Promise<unknown>) => {
    setConfirming(true);
    try {
      await action();
      setDialog(null);
    } catch (error) {
      notifyError(error);
    } finally {
      setConfirming(false);
    }
  };

  const runCommand = (command: CommandView) => setDialog({
    kind: 'confirm',
    title: `Run "${command.name}"?`,
    body: <Code block>{command.script}</Code>,
    confirmLabel: 'Run',
    action: () => runs.track(api.runCommand(server.id, command.id)),
  });

  const deleteCommand = (command: CommandView, remove: () => Promise<void>) => setDialog({
    kind: 'confirm',
    title: `Delete command "${command.name}"?`,
    body: <Code block>{command.script}</Code>,
    confirmLabel: 'Delete',
    danger: true,
    action: async () => {
      await remove();
      onRefresh();
    },
  });

  const header = (
    <Box
      component="button"
      type="button"
      aria-expanded={opened}
      onClick={() => setOpened((value) => !value)}
      px={22}
      py={18}
      w="100%"
      style={{ background: 'transparent', border: 0, cursor: 'pointer', textAlign: 'left', font: 'inherit', color: 'inherit' }}
    >
      <Group gap="14px 28px" align="center">
        <Group gap={14} wrap="nowrap" style={{ flex: '1 1 380px', minWidth: 0 }}>
          {state?.status === 'loading' || !overview ? <Loader size={12} /> : <StatusLed overview={overview} />}
          <Stack gap={8} style={{ minWidth: 0 }}>
            <Group gap="4px 12px" align="baseline">
              <Text fz={20} fw={700}>{server.name}</Text>
              <Text ff="monospace" size="sm" c="dimmed">
                {server.user}@{server.ssm?.instanceId ?? server.host}{server.port === 22 ? '' : `:${server.port}`}{server.ssm ? ' via SSM' : ''}
              </Text>
              {server.provider && <Badge color="dark" radius="sm" tt="none">{server.provider.name}</Badge>}
            </Group>
            {overview && <SummaryChips overview={overview} />}
          </Stack>
        </Group>
        {overview?.system && <UsageMeters system={overview.system} />}
        <IconChevronDown
          color={ui.muted}
          style={{ transform: opened ? 'rotate(180deg)' : undefined, transition: 'transform 150ms' }}
          aria-hidden="true"
        />
      </Group>
    </Box>
  );

  const body = overview && overview.status !== 'error' && (
    <>
      <Group justify="space-between" gap={12}>
        <Text size="sm" c="dimmed">
          {overview.system &&
            `${overview.system.os} · up ${formatUptime(overview.system.uptimeSeconds)} · ${overview.system.cpus} ${plural(overview.system.cpus, 'core', 'cores')}`}
          {!overview.dockerAvailable && ' · Docker not found'}
        </Text>
        <Group gap={8}>
          <Tooltip label="Refresh server data" withArrow>
            <ActionIcon variant="default" size={40} aria-label="Refresh server data" onClick={onRefresh}>
              <IconRefresh size={16} />
            </ActionIcon>
          </Tooltip>
          <Button variant="default" onClick={() => openFiles('/')}>Files</Button>
          <Button variant="default" onClick={() => setDialog({ kind: 'manual', project: null })}>Add project</Button>
          <Button variant="default" onClick={onEdit}>Settings</Button>
          <Button variant="outline" color="red" onClick={() => setDialog({ kind: 'reboot' })}>Reboot</Button>
        </Group>
      </Group>
      <Box style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', gap: 14 }}>
        {overview.composeProjects.map((project) => {
          const target: CommandTarget = { type: 'compose', project: project.name, folder: project.workingDir };
          const actions: CommandActions = {
            busy: runs.isBusy(server.id, `compose:${project.name}`),
            onRun: runCommand,
            onAdd: () => setDialog({ kind: 'command', target, command: null }),
            onEdit: (command) => setDialog({ kind: 'command', target, command }),
            onDelete: (command) => deleteCommand(command, () => api.deleteComposeCommand(server.id, project.name, command.id)),
            onFiles: () => openFiles(project.workingDir),
          };
          return (
            <ComposeCard
              key={project.name}
              project={project}
              actions={actions}
              onUpdate={() => setDialog({
                kind: 'confirm',
                title: `Update images of project ${project.name}?`,
                body: <Text c="#4A505C">docker compose pull and docker compose up -d will run in the project folder.</Text>,
                confirmLabel: 'Update images',
                action: () => runs.track(api.updateCompose(server.id, project.name)),
              })}
            />
          );
        })}
        {overview.manualProjects.map((project) => {
          const target: CommandTarget = { type: 'manual', projectId: project.id, projectName: project.name, folder: project.folder };
          const actions: CommandActions = {
            busy: runs.isBusy(server.id, `manual:${project.id}`),
            onRun: runCommand,
            onAdd: () => setDialog({ kind: 'command', target, command: null }),
            onEdit: (command) => setDialog({ kind: 'command', target, command }),
            onDelete: (command) => deleteCommand(command, () => api.deleteManualCommand(server.id, project.id, command.id)),
            onFiles: () => openFiles(project.folder),
          };
          return (
            <ManualCard
              key={project.id}
              project={project}
              actions={actions}
              onEditProject={() => setDialog({ kind: 'manual', project })}
              onDeleteProject={() => setDialog({
                kind: 'confirm',
                title: `Remove project "${project.name}" from the panel?`,
                body: <Text c="#4A505C">The project and its commands will be removed from the panel. The folder on the server stays.</Text>,
                confirmLabel: 'Delete',
                danger: true,
                action: async () => {
                  await api.deleteManualProject(server.id, project.id);
                  onRefresh();
                },
              })}
            />
          );
        })}
        {overview.standaloneContainers.length > 0 && <StandaloneCard containers={overview.standaloneContainers} />}
        {overview.timers.length > 0 && <TimersCard timers={overview.timers} />}
      </Box>
    </>
  );

  const failure = overview?.status === 'error' && (
    <Group justify="space-between" gap={12} p="12px 14px" bg="#FDE8E6" c="#8C1D13" style={{ borderRadius: 12 }}>
      <Text size="sm">{overview.error}</Text>
      <Group gap={8}>
        <Button variant="default" onClick={onRefresh}>Retry</Button>
        <Button variant="default" onClick={onEdit}>Settings</Button>
      </Group>
    </Group>
  );

  return (
    <Card withBorder radius={18} p={0} shadow={opened ? 'md' : 'xs'} style={{ borderColor: opened ? '#B9C2D3' : ui.border, overflow: 'hidden' }}>
      {header}
      <Collapse in={opened}>
        <Stack gap={16} px={22} pt={16} pb={22} bg={ui.panel} style={{ borderTop: `1px solid ${ui.border}` }}>
          {body}
          {failure}
          {!overview && <Loader size="sm" />}
        </Stack>
      </Collapse>

      {dialog?.kind === 'confirm' && (
        <ConfirmModal
          title={dialog.title}
          confirmLabel={dialog.confirmLabel}
          danger={dialog.danger}
          loading={confirming}
          onConfirm={() => void confirm(dialog.action)}
          onClose={closeDialog}
        >
          {dialog.body}
        </ConfirmModal>
      )}
      {dialog?.kind === 'command' && (
        <CommandModal
          serverId={server.id}
          target={dialog.target}
          command={dialog.command}
          onClose={closeDialog}
          onSaved={saved}
          onDelete={(command) => deleteCommand(command, () =>
            dialog.target.type === 'compose'
              ? api.deleteComposeCommand(server.id, dialog.target.project, command.id)
              : api.deleteManualCommand(server.id, dialog.target.projectId, command.id))}
        />
      )}
      {dialog?.kind === 'manual' && (
        <ManualProjectModal serverId={server.id} project={dialog.project} onClose={closeDialog} onSaved={saved} />
      )}
      {dialog?.kind === 'reboot' && <RebootModal server={server} onClose={closeDialog} />}
    </Card>
  );
}
