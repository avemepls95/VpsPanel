import { useState } from 'react';
import { Button, Code, Group, Input, Modal, NumberInput, SegmentedControl, SimpleGrid, Stack, Text, TextInput } from '@mantine/core';
import { api } from '../api/endpoints';
import type { ServerView } from '../api/types';
import { ConfirmModal } from '../common/ConfirmModal';
import { notifyError } from '../common/notify';

interface Props {
  server: ServerView | null;
  onClose: () => void;
  onSaved: () => void;
  onChanged?: () => void;
}

const monospace = { input: { fontFamily: 'var(--mantine-font-family-monospace)' } };

type Connection = 'ssh' | 'ssm';

export function ServerModal({ server, onClose, onSaved, onChanged }: Props) {
  const [current, setCurrent] = useState(server);
  const [name, setName] = useState(server?.name ?? '');
  const [host, setHost] = useState(server?.host ?? '');
  const [port, setPort] = useState<number | string>(server?.port ?? 22);
  const [user, setUser] = useState(server?.user ?? 'root');
  const [keyPath, setKeyPath] = useState(server?.keyPath ?? '');
  const [connection, setConnection] = useState<Connection>(server?.ssm ? 'ssm' : 'ssh');
  const [instanceId, setInstanceId] = useState(server?.ssm?.instanceId ?? '');
  const [awsProfile, setAwsProfile] = useState(server?.ssm?.profile ?? '');
  const [awsRegion, setAwsRegion] = useState(server?.ssm?.region ?? '');
  const [provider, setProvider] = useState(server?.provider?.name ?? '');
  const [busy, setBusy] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const run = async (key: string, action: () => Promise<void>) => {
    setBusy(key);
    try {
      await action();
    } catch (error) {
      notifyError(error);
    } finally {
      setBusy(null);
    }
  };

  // The backend treats the hosting name as entered manually only if it differs from the saved one.
  const save = () => run('save', async () => {
    const ssm = connection === 'ssm'
      ? { instanceId, profile: awsProfile.trim() || null, region: awsRegion.trim() || null }
      : null;
    const body = { name, host: ssm ? '' : host, port: Number(port), user, keyPath, providerName: provider.trim() || null, ssm };
    if (current) await api.updateServer(current.id, body);
    else await api.createServer(body);
    onSaved();
  });

  const refreshProvider = () => run('provider', async () => {
    const updated = await api.refreshProvider(current!.id);
    setCurrent(updated);
    setProvider(updated.provider?.name ?? '');
    onChanged?.();
  });

  const resetHostKey = () => run('hostKey', async () => {
    setCurrent(await api.resetHostKey(current!.id));
    onChanged?.();
  });

  const remove = () => run('delete', async () => {
    await api.deleteServer(current!.id);
    onSaved();
  });

  return (
    <>
      <Modal opened onClose={onClose} centered radius="lg" size="lg" title={current ? `Server "${current.name}"` : 'New server'}>
        <Stack>
          <TextInput label="Name" value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
          <Input.Wrapper label="Connection">
            <SegmentedControl
              fullWidth
              value={connection}
              onChange={(value) => setConnection(value as Connection)}
              data={[{ value: 'ssh', label: 'SSH' }, { value: 'ssm', label: 'AWS SSM' }]}
            />
          </Input.Wrapper>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            {connection === 'ssh' ? (
              <TextInput label="Address" value={host} onChange={(e) => setHost(e.currentTarget.value)} styles={monospace} />
            ) : (
              <TextInput
                label="Instance ID"
                placeholder="i-0123456789abcdef0"
                value={instanceId}
                onChange={(e) => setInstanceId(e.currentTarget.value)}
                styles={monospace}
              />
            )}
            <NumberInput label="Port" value={port} onChange={setPort} min={1} max={65535} styles={monospace} />
            <TextInput label="User" value={user} onChange={(e) => setUser(e.currentTarget.value)} styles={monospace} />
          </SimpleGrid>
          {connection === 'ssm' && (
            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <TextInput
                label="AWS profile"
                placeholder="default"
                value={awsProfile}
                onChange={(e) => setAwsProfile(e.currentTarget.value)}
                styles={monospace}
              />
              <TextInput
                label="AWS region"
                placeholder="default"
                value={awsRegion}
                onChange={(e) => setAwsRegion(e.currentTarget.value)}
                styles={monospace}
              />
            </SimpleGrid>
          )}
          <TextInput
            label="SSH key path"
            placeholder="%USERPROFILE%\.ssh\id_ed25519"
            value={keyPath}
            onChange={(e) => setKeyPath(e.currentTarget.value)}
            styles={monospace}
          />
          <Stack gap={6}>
            <Group align="flex-end" gap={8}>
              <TextInput
                label="Hosting"
                value={provider}
                onChange={(e) => setProvider(e.currentTarget.value)}
                style={{ flex: 1 }}
                description={current?.provider?.isManual ? 'Entered manually' : undefined}
              />
              {current && (
                <Button variant="default" loading={busy === 'provider'} onClick={() => void refreshProvider()}>
                  Detect again
                </Button>
              )}
            </Group>
          </Stack>
          {current && (
            <Stack gap={8} p={14} bg="#F6F7F9" style={{ borderRadius: 12, border: '1px solid #E6E8ED' }}>
              <Group justify="space-between">
                <Text fw={600} size="sm">Server key fingerprint</Text>
                <Button size="xs" variant="default" loading={busy === 'hostKey'} disabled={!current.hostKeyFingerprint} onClick={() => void resetHostKey()}>
                  Reset
                </Button>
              </Group>
              <Text ff="monospace" fz={13} style={{ overflowWrap: 'anywhere' }}>
                {current.hostKeyFingerprint ?? 'Saved on the next connection'}
              </Text>
              <Text size="sm" c="dimmed">
                Check on the server: <Code>ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub</Code>
              </Text>
            </Stack>
          )}
          <Group justify={current ? 'space-between' : 'flex-end'}>
            {current && <Button variant="outline" color="red" onClick={() => setConfirmDelete(true)}>Delete server</Button>}
            <Group>
              <Button variant="default" onClick={onClose}>Cancel</Button>
              <Button loading={busy === 'save'} onClick={() => void save()}>Save</Button>
            </Group>
          </Group>
        </Stack>
      </Modal>
      {confirmDelete && current && (
        <ConfirmModal
          title={`Remove server "${current.name}" from the panel?`}
          confirmLabel="Delete"
          danger
          loading={busy === 'delete'}
          onConfirm={() => void remove()}
          onClose={() => setConfirmDelete(false)}
        >
          <Text c="#4A505C">The server and its commands will be removed from the panel. Nothing changes on the server itself.</Text>
        </ConfirmModal>
      )}
    </>
  );
}
