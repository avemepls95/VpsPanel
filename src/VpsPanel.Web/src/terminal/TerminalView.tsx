import { useEffect, useRef } from 'react';
import { Box } from '@mantine/core';
import { FitAddon } from '@xterm/addon-fit';
import { Terminal, type ITheme } from '@xterm/xterm';
import '@xterm/xterm/css/xterm.css';
import { api } from '../api/endpoints';
import { ui } from '../theme';

export type TerminalState = { status: 'connecting' } | { status: 'connected' } | { status: 'ended' } | { status: 'error'; message: string };

type ServerMessage = { type: 'ready' } | { type: 'error'; message: string };

const font = '"JetBrains Mono", monospace';

// The default xterm colors are hard to read on the dark background, blue in particular.
const colors: ITheme = {
  background: ui.console,
  foreground: '#E6E8EE',
  cursor: '#E6E8EE',
  cursorAccent: ui.console,
  selectionBackground: '#3A4A80',
  black: '#2A2E37',
  red: '#E5484D',
  green: '#2FB670',
  yellow: '#E5A83A',
  blue: '#6B84F6',
  magenta: '#B47CF0',
  cyan: '#2FB8A8',
  white: '#C4C9D4',
  brightBlack: '#727989',
  brightRed: '#FF6B70',
  brightGreen: '#5AD99A',
  brightYellow: '#F5C451',
  brightBlue: '#8EA2FF',
  brightMagenta: '#CB9CFF',
  brightCyan: '#63D9CB',
  brightWhite: '#F4F5F8',
};

interface Props {
  serverId: string;
  onState: (state: TerminalState) => void;
}

/**
 * One shell session: it opens on mount and ends on unmount.
 */
export function TerminalView({ serverId, onState }: Props) {
  const host = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const term = new Terminal({
      fontFamily: font,
      fontSize: 14,
      cursorBlink: true,
      scrollback: 5000,
      theme: colors,
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    const encoder = new TextEncoder();
    let socket: WebSocket | null = null;
    let observer: ResizeObserver | null = null;
    let disposed = false;
    let ready = false;
    let failed = false;

    const send = (data: Uint8Array) => {
      if (socket?.readyState === WebSocket.OPEN) socket.send(data);
    };

    // Ctrl+C with a selection copies it, Ctrl+V pastes through the browser paste event.
    term.attachCustomKeyEventHandler((event) => {
      if (event.type !== 'keydown' || !event.ctrlKey || event.altKey) return true;
      if (event.code === 'KeyC' && term.hasSelection()) {
        void navigator.clipboard.writeText(term.getSelection());
        term.clearSelection();
        return false;
      }
      return event.code !== 'KeyV';
    });
    term.onData((data) => send(encoder.encode(data)));
    term.onBinary((data) => send(Uint8Array.from(data, (c) => c.charCodeAt(0))));
    term.onResize(({ cols, rows }) => {
      if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify({ type: 'resize', cols, rows }));
    });

    // xterm measures the cell size when it opens, so the font has to be loaded by then.
    void document.fonts.load(`14px ${font}`).finally(() => {
      if (disposed || !host.current) return;
      term.open(host.current);
      fit.fit();
      observer = new ResizeObserver(() => fit.fit());
      observer.observe(host.current);

      socket = new WebSocket(api.terminalUrl(serverId, term.cols, term.rows));
      socket.binaryType = 'arraybuffer';
      socket.onmessage = (event: MessageEvent<string | ArrayBuffer>) => {
        if (typeof event.data !== 'string') {
          term.write(new Uint8Array(event.data));
          return;
        }
        const message = JSON.parse(event.data) as ServerMessage;
        if (message.type === 'ready') {
          ready = true;
          onState({ status: 'connected' });
          term.focus();
        } else {
          failed = true;
          onState({ status: 'error', message: message.message });
        }
      };
      socket.onclose = () => {
        if (disposed || failed) return;
        onState(ready ? { status: 'ended' } : { status: 'error', message: 'Could not open the terminal.' });
      };
    });

    return () => {
      disposed = true;
      observer?.disconnect();
      socket?.close();
      term.dispose();
    };
  }, [serverId, onState]);

  return <Box ref={host} h="100%" />;
}