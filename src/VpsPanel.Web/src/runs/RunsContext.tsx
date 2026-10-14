import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api } from '../api/endpoints';
import type { RunStarted, RunView } from '../api/types';
import { notifyError } from '../common/notify';

interface RunsContextValue {
  runs: RunView[];
  opened: boolean;
  selectedId: string | null;
  open: (runId?: string) => void;
  close: () => void;
  refresh: () => Promise<void>;
  track: (started: Promise<RunStarted>) => Promise<boolean>;
  isBusy: (serverId: string, projectKey: string) => boolean;
  activeFor: (serverId: string) => RunView[];
}

const RunsContext = createContext<RunsContextValue | null>(null);

export function RunsProvider({ children }: { children: ReactNode }) {
  const [runs, setRuns] = useState<RunView[]>([]);
  const [opened, setOpened] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      setRuns(await api.runs());
    } catch (error) {
      notifyError(error);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // While runs are in progress, statuses are refreshed by polling. The SSE stream is open only for the run shown in the drawer.
  const hasActive = runs.some((run) => run.status === 'running');
  useEffect(() => {
    if (!hasActive) return;
    const timer = window.setInterval(() => void refresh(), 2000);
    return () => window.clearInterval(timer);
  }, [hasActive, refresh]);

  // When the drawer opens with no run selected, the newest run in the list is selected.
  useEffect(() => {
    if (opened && selectedId === null && runs.length > 0) setSelectedId(runs[0].id);
  }, [opened, selectedId, runs]);

  const track = useCallback(async (started: Promise<RunStarted>) => {
    try {
      const { runId } = await started;
      await refresh();
      setSelectedId(runId);
      setOpened(true);
      return true;
    } catch (error) {
      notifyError(error);
      return false;
    }
  }, [refresh]);

  const value = useMemo<RunsContextValue>(() => ({
    runs,
    opened,
    selectedId,
    open: (runId) => {
      if (runId) setSelectedId(runId);
      setOpened(true);
      void refresh();
    },
    close: () => setOpened(false),
    refresh,
    track,
    isBusy: (serverId, projectKey) =>
      runs.some((run) => run.status === 'running' && run.serverId === serverId && run.projectKey === projectKey),
    activeFor: (serverId) => runs.filter((run) => run.status === 'running' && run.serverId === serverId),
  }), [runs, opened, selectedId, refresh, track]);

  return <RunsContext.Provider value={value}>{children}</RunsContext.Provider>;
}

export function useRuns(): RunsContextValue {
  const value = useContext(RunsContext);
  if (!value) throw new Error('useRuns is called outside RunsProvider');
  return value;
}
