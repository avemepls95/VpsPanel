import { useEffect, useRef, useState } from 'react';
import { Box, ScrollArea } from '@mantine/core';
import { ui } from '../theme';

const maxLines = 10_000;
const serverDropNotice = /^Lines dropped from the start of the output: (\d+)$/;

export function RunOutput({ runId, onEnd }: { runId: string; onEnd: () => void }) {
  const [output, setOutput] = useState<{ lines: string[]; dropped: number }>({ lines: [], dropped: 0 });
  const [serverDropped, setServerDropped] = useState(0);
  const viewport = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setOutput({ lines: [], dropped: 0 });
    setServerDropped(0);
    let buffer: string[] = [];
    let timer: number | undefined;
    const flush = () => {
      timer = undefined;
      const chunk = buffer;
      buffer = [];
      setOutput((previous) => {
        const merged = previous.lines.concat(chunk);
        const excess = Math.max(0, merged.length - maxLines);
        return excess > 0 ? { lines: merged.slice(excess), dropped: previous.dropped + excess } : { lines: merged, dropped: previous.dropped };
      });
    };

    // EventSource reconnects on its own with Last-Event-ID, and the server continues from the next line.
    const source = new EventSource(`/api/runs/${encodeURIComponent(runId)}/stream`);
    source.addEventListener('line', (event) => {
      const data = (event as MessageEvent<string>).data;
      const notice = serverDropNotice.exec(data);
      if (notice) {
        setServerDropped(Number(notice[1]));
        return;
      }
      buffer.push(data);
      timer ??= window.setTimeout(flush, 100);
    });
    source.addEventListener('end', () => {
      source.close();
      if (timer !== undefined) window.clearTimeout(timer);
      flush();
      onEnd();
    });
    // For a finished run with no new lines the server answers 204 without an end event, so the final status comes from the runs list.
    source.onerror = () => {
      if (source.readyState === EventSource.CLOSED) onEnd();
    };

    return () => {
      source.close();
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [runId, onEnd]);

  useEffect(() => {
    const element = viewport.current;
    if (element) element.scrollTop = element.scrollHeight;
  }, [output]);

  const dropped = serverDropped + output.dropped;
  const text = [...(dropped > 0 ? [`Lines dropped from the start of the output: ${dropped}`] : []), ...output.lines].join('\n');

  return (
    <ScrollArea viewportRef={viewport} style={{ flex: 1, minHeight: 0, borderRadius: 14, background: ui.console }}>
      <Box component="pre" m={0} p="md" c="#D7DAE0" ff="monospace" fz={12.5} style={{ whiteSpace: 'pre-wrap', lineHeight: 1.65 }}>
        {text.length === 0 ? 'No output yet' : text}
      </Box>
    </ScrollArea>
  );
}
