import type { ReactNode } from 'react';
import { Anchor, Badge, Box, Button, Container, Group, Text } from '@mantine/core';
import { Link } from 'react-router';
import { useRuns } from '../runs/RunsContext';
import { ui } from '../theme';
import { Logo } from './Logo';

export function AppHeader({ section, actions }: { section?: string; actions?: ReactNode }) {
  const { runs, open } = useRuns();
  const active = runs.filter((run) => run.status === 'running').length;

  return (
    <Box bg={ui.ink} c="#F1F2F5">
      <Container size={1240} py={12}>
        <Group justify="space-between">
          <Group gap={10}>
            <Logo />
            <Anchor component={Link} to="/" c="#F1F2F5" fw={700} fz={18} underline="never">VpsPanel</Anchor>
            {section && (
              <>
                <Text c="#8A90A0">/</Text>
                <Text c="#C9CDD6">{section}</Text>
              </>
            )}
          </Group>
          <Group gap={8}>
            <Button
              variant="outline"
              color="gray.3"
              c="#F1F2F5"
              styles={{ root: { borderColor: '#3A3F4A' } }}
              rightSection={active > 0 ? <Badge circle size="sm">{active}</Badge> : undefined}
              onClick={() => open()}
            >
              Runs
            </Button>
            {actions}
          </Group>
        </Group>
      </Container>
    </Box>
  );
}
