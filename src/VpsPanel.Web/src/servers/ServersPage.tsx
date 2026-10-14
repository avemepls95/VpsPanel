import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Container, Group, Stack, Text, Title } from '@mantine/core';
import { IconRefresh } from '@tabler/icons-react';
import { api } from '../api/endpoints';
import type { ServerOverview, ServerView } from '../api/types';
import { AppHeader } from '../common/AppHeader';
import { Logo } from '../common/Logo';
import { notifyError } from '../common/notify';
import { plural } from '../format';
import { ui } from '../theme';
import { ServerCard, type OverviewState } from './ServerCard';
import { ServerModal } from './ServerModal';

function failedOverview(serverId: string, error: unknown): ServerOverview {
  return {
    serverId,
    status: 'error',
    error: error instanceof Error ? error.message : String(error),
    hostKeyMismatch: false,
    system: null,
    dockerAvailable: false,
    composeProjects: [],
    standaloneContainers: [],
    timers: [],
    manualProjects: [],
  };
}

const timeFormat = new Intl.DateTimeFormat('en-US', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });

export function ServersPage() {
  const [servers, setServers] = useState<ServerView[] | null>(null);
  const [settingsError, setSettingsError] = useState<string | null>(null);
  const [overviews, setOverviews] = useState<Record<string, OverviewState>>({});
  const [loadedAt, setLoadedAt] = useState<Date | null>(null);
  const [editing, setEditing] = useState<ServerView | 'new' | null>(null);

  const loadOverview = useCallback(async (serverId: string) => {
    setOverviews((current) => ({ ...current, [serverId]: { status: 'loading' } }));
    let overview: ServerOverview;
    try {
      overview = await api.overview(serverId);
    } catch (error) {
      overview = failedOverview(serverId, error);
    }
    setOverviews((current) => ({ ...current, [serverId]: { status: 'loaded', overview } }));
    setLoadedAt(new Date());
  }, []);

  const loadAll = useCallback(async () => {
    try {
      const status = await api.settingsStatus();
      setSettingsError(status.error);
      if (status.error) {
        setServers(null);
        return;
      }
      const list = await api.servers();
      setServers(list);
      list.forEach((server) => void loadOverview(server.id));
    } catch (error) {
      notifyError(error);
    }
  }, [loadOverview]);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);

  const reloadServers = useCallback(async () => {
    try {
      setServers(await api.servers());
    } catch (error) {
      notifyError(error);
    }
  }, []);

  const saved = () => {
    setEditing(null);
    void loadAll();
  };

  const hasServers = servers !== null && servers.length > 0;

  return (
    <>
      <AppHeader actions={<Button onClick={() => setEditing('new')}>Add server</Button>} />
      <Container size={1240} pt={28} pb={40}>
        {(hasServers || settingsError) && (
          <Group justify="space-between" align="flex-end" mb="md">
            <Stack gap={2}>
              <Title order={1} fz={28} style={{ letterSpacing: '-0.02em' }}>Servers</Title>
              {hasServers && (
                <Text c="dimmed" size="sm">
                  {servers.length} {plural(servers.length, 'server', 'servers')}
                  {loadedAt && ` · data as of ${timeFormat.format(loadedAt)}`}
                </Text>
              )}
            </Stack>
            {hasServers && (
              <Button variant="default" leftSection={<IconRefresh size={16} />} onClick={() => servers.forEach((s) => void loadOverview(s.id))}>
                Refresh all
              </Button>
            )}
          </Group>
        )}

        {settingsError && <Alert color="red" title="Failed to read settings">{settingsError}</Alert>}

        {servers?.length === 0 && (
          <Stack align="center" gap={14} py={60}>
            <Stack w={64} h={64} align="center" justify="center" bg={ui.ink} style={{ borderRadius: 18 }}>
              <Logo size={32} dots={ui.neutral} />
            </Stack>
            <Text fz={20} fw={700}>No servers yet</Text>
            <Button size="md" onClick={() => setEditing('new')}>Add server</Button>
          </Stack>
        )}

        <Stack gap={14}>
          {servers?.map((server) => (
            <ServerCard
              key={server.id}
              server={server}
              state={overviews[server.id]}
              onRefresh={() => void loadOverview(server.id)}
              onEdit={() => setEditing(server)}
            />
          ))}
        </Stack>
      </Container>

      {editing && <ServerModal server={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={saved} onChanged={() => void reloadServers()} />}
    </>
  );
}
