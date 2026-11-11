import { useEffect, useState } from 'react';
import { Alert, Box, Button, Container, Group, Select, Text } from '@mantine/core';
import { useSearchParams } from 'react-router';
import { api } from '../api/endpoints';
import type { ServerView } from '../api/types';
import { AppHeader } from '../common/AppHeader';
import { notifyError } from '../common/notify';
import { ui } from '../theme';
import { TerminalView, type TerminalState } from './TerminalView';

const statusText: Record<Exclude<TerminalState['status'], 'error'>, string> = {
  connecting: 'Connecting…',
  connected: 'Connected',
  ended: 'Session ended',
};

export function TerminalPage() {
  const [params, setParams] = useSearchParams();
  const [servers, setServers] = useState<ServerView[]>([]);
  const [state, setState] = useState<TerminalState>({ status: 'connecting' });
  const [attempt, setAttempt] = useState(0);

  const serverId = params.get('server') ?? servers[0]?.id ?? '';

  useEffect(() => {
    api.servers().then(setServers).catch(notifyError);
  }, []);

  const reconnect = () => {
    setState({ status: 'connecting' });
    setAttempt((value) => value + 1);
  };

  const selectServer = (value: string) => {
    setState({ status: 'connecting' });
    setParams({ server: value });
  };

  return (
    <Box h="100vh" style={{ display: 'flex', flexDirection: 'column' }}>
      <AppHeader section="Terminal" />
      <Container size={1240} w="100%" py={16} style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 12, minHeight: 0 }}>
        <Group justify="space-between" gap={12}>
          <Select
            aria-label="Server"
            w={240}
            data={servers.map((s) => ({ value: s.id, label: s.name }))}
            value={serverId || null}
            onChange={(value) => value && selectServer(value)}
            allowDeselect={false}
          />
          <Group gap={12}>
            {state.status !== 'error' && <Text size="sm" c={state.status === 'connected' ? ui.ok : ui.muted}>{statusText[state.status]}</Text>}
            {(state.status === 'ended' || state.status === 'error') && <Button variant="default" onClick={reconnect}>Reconnect</Button>}
          </Group>
        </Group>
        {state.status === 'error' && <Alert color="red">{state.message}</Alert>}
        <Box p={10} bg={ui.console} style={{ flex: 1, minHeight: 0, borderRadius: 12, overflow: 'hidden' }}>
          {serverId && <TerminalView key={`${serverId}:${attempt}`} serverId={serverId} onState={setState} />}
        </Box>
      </Container>
    </Box>
  );
}