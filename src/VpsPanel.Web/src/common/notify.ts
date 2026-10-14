import { notifications } from '@mantine/notifications';

export function notifyError(error: unknown): void {
  notifications.show({
    color: 'red',
    title: 'Error',
    message: error instanceof Error ? error.message : String(error),
    autoClose: 8000,
  });
}

export function notifySuccess(title: string, message?: string): void {
  notifications.show({ color: 'green', title, message: message ?? '', autoClose: 8000 });
}
