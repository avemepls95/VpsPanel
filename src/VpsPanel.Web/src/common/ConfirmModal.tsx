import type { ReactNode } from 'react';
import { Button, Group, Modal, Stack } from '@mantine/core';

interface Props {
  title: string;
  children?: ReactNode;
  confirmLabel: string;
  danger?: boolean;
  loading?: boolean;
  confirmDisabled?: boolean;
  onConfirm: () => void;
  onClose: () => void;
}

export function ConfirmModal({ title, children, confirmLabel, danger, loading, confirmDisabled, onConfirm, onClose }: Props) {
  return (
    <Modal opened onClose={onClose} title={title} centered radius="lg" size="lg">
      <Stack>
        {children}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>Cancel</Button>
          <Button color={danger ? 'red' : undefined} loading={loading} disabled={confirmDisabled} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
