const MB = 1024 * 1024;
const GB = MB * 1024;
const decimal = new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 });
const time = new Intl.DateTimeFormat('en-US', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
const dayMonth = new Intl.DateTimeFormat('en-US', { day: 'numeric', month: 'short' });
const dayMonthYear = new Intl.DateTimeFormat('en-US', { day: 'numeric', month: 'short', year: 'numeric' });

export function plural(n: number, one: string, many: string): string {
  return n === 1 ? one : many;
}

export function formatUsage(used: number, total: number): string {
  if (total >= GB) return `${decimal.format(used / GB)} / ${decimal.format(total / GB)} GB`;
  return `${Math.round(used / MB)} / ${Math.round(total / MB)} MB`;
}

export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < MB) return `${decimal.format(bytes / 1024)} KB`;
  if (bytes < GB) return `${decimal.format(bytes / MB)} MB`;
  return `${decimal.format(bytes / GB)} GB`;
}

export function formatUptime(seconds: number): string {
  const days = Math.floor(seconds / 86400);
  if (days >= 1) return `${days} ${plural(days, 'day', 'days')}`;
  const hours = Math.floor(seconds / 3600);
  if (hours >= 1) return `${hours} h`;
  return `${Math.max(1, Math.floor(seconds / 60))} min`;
}

function startOfDay(date: Date): number {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
}

export function formatWhen(iso: string): string {
  const date = new Date(iso);
  const days = Math.round((startOfDay(date) - startOfDay(new Date())) / 86400000);
  const at = time.format(date);
  if (days === 0) return `today at ${at}`;
  if (days === -1) return `yesterday at ${at}`;
  if (days === 1) return `tomorrow at ${at}`;
  return `${dayMonth.format(date)} at ${at}`;
}

export function formatModified(iso: string): string {
  const date = new Date(iso);
  return date.getFullYear() === new Date().getFullYear()
    ? `${dayMonth.format(date)} ${time.format(date)}`
    : dayMonthYear.format(date);
}

export function formatDuration(ms: number): string {
  const total = Math.max(0, Math.round(ms / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = String(total % 60).padStart(2, '0');
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`;
}

export function parentPath(path: string): string {
  const index = path.lastIndexOf('/');
  return index <= 0 ? '/' : path.slice(0, index);
}

export function childPath(directory: string, name: string): string {
  return directory === '/' ? `/${name}` : `${directory}/${name}`;
}
