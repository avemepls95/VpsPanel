import { useEffect, useMemo, useState } from 'react';
import { Alert, Badge, Button, Container, Group, Loader, Stack, Text, Title } from '@mantine/core';
import CodeMirror from '@uiw/react-codemirror';
import { HighlightStyle, StreamLanguage, syntaxHighlighting } from '@codemirror/language';
import { properties } from '@codemirror/legacy-modes/mode/properties';
import { EditorView } from '@codemirror/view';
import { tags } from '@lezer/highlight';
import { useNavigate, useSearchParams } from 'react-router';
import { ApiError } from '../api/client';
import { api } from '../api/endpoints';
import type { FileContentView, FileNotEditableReason, ServerView } from '../api/types';
import { AppHeader } from '../common/AppHeader';
import { ConfirmModal } from '../common/ConfirmModal';
import { notifyError, notifySuccess } from '../common/notify';
import { parentPath } from '../format';

const highlightStyle = HighlightStyle.define([
  { tag: tags.definition(tags.variableName), class: 'cm-env-key' },
  { tag: tags.comment, class: 'cm-env-comment' },
  { tag: tags.heading, class: 'cm-env-heading' },
]);

const highlightTheme = EditorView.theme({
  '.cm-scroller': { fontFamily: 'var(--mantine-font-family-monospace)' },
  '.cm-env-key': { color: '#61AFEF' },
  '.cm-env-comment': { color: '#7D8799', fontStyle: 'italic' },
  '.cm-env-heading': { color: '#E5C07B', fontWeight: 'bold' },
});

const toEditorText = (text: string | null) => (text ?? '').replace(/\r\n/g, '\n');

const reasons: Record<FileNotEditableReason, string> = {
  tooLarge: 'The file is larger than 1 MB and can only be downloaded.',
  binary: 'The file looks binary and can only be downloaded.',
  notUtf8: 'The file is not UTF-8 encoded and can only be downloaded.',
};

export function EditorPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const serverId = params.get('server') ?? '';
  const path = params.get('path') ?? '';

  const [server, setServer] = useState<ServerView | null>(null);
  const [content, setContent] = useState<FileContentView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [text, setText] = useState('');
  const [savedText, setSavedText] = useState('');
  const [sha, setSha] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [leaving, setLeaving] = useState(false);

  useEffect(() => {
    api.servers().then((list) => setServer(list.find((s) => s.id === serverId) ?? null)).catch(notifyError);
    api.readFile(serverId, path)
      .then((file) => {
        setContent(file);
        setText(toEditorText(file.text));
        setSavedText(toEditorText(file.text));
        setSha(file.sha256);
      })
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));
  }, [serverId, path]);

  const extensions = useMemo(
    () => [StreamLanguage.define(properties), syntaxHighlighting(highlightStyle), highlightTheme, EditorView.lineWrapping],
    [],
  );

  const dirty = text !== savedText;

  const save = async (force: boolean) => {
    if (!content || !sha) return;
    setSaving(true);
    try {
      const payload = content.lineSeparator === 'crlf' ? text.replace(/\n/g, '\r\n') : text;
      const result = await api.saveFile(serverId, { path: content.path, text: payload, sha256: sha, hasBom: content.hasBom, force });
      setSha(result.sha256);
      setSavedText(text);
      setConflict(false);
      notifySuccess('File saved', `Previous version: ${result.backupPath}`);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) setConflict(true);
      else notifyError(e);
    } finally {
      setSaving(false);
    }
  };

  const close = () => navigate(`/files?server=${encodeURIComponent(serverId)}&path=${encodeURIComponent(parentPath(path))}`);

  return (
    <>
      <AppHeader section="Editor" />
      <Container size={1240} py={24}>
        <Group justify="space-between" align="flex-end" mb="md" gap={12}>
          <Stack gap={4} style={{ minWidth: 0 }}>
            <Group gap={8}>
              {server && <Badge color="dark" radius="sm" tt="none">{server.name}</Badge>}
              {content?.editable && (
                <Badge variant="outline" color="gray" radius="sm" tt="none" ff="monospace">
                  UTF-8 · {content.lineSeparator === 'crlf' ? 'CRLF' : 'LF'}{content.hasBom ? ' · BOM' : ''}
                </Badge>
              )}
              {dirty && <Badge color="yellow" variant="light" tt="none">unsaved</Badge>}
            </Group>
            <Title order={1} fz={17} ff="monospace" style={{ overflowWrap: 'anywhere' }}>{path}</Title>
          </Stack>
          <Group gap={8}>
            <Button variant="default" onClick={() => (dirty ? setLeaving(true) : close())}>Close</Button>
            {content?.editable && (
              <Button loading={saving} disabled={!dirty} onClick={() => void save(false)}>Save</Button>
            )}
          </Group>
        </Group>

        {error && <Alert color="red">{error}</Alert>}
        {!content && !error && <Loader size="sm" />}

        {content && !content.editable && content.reason && (
          <Alert color="gray" title="The file cannot be opened in the editor">
            <Stack gap="sm" align="flex-start">
              <Text>{reasons[content.reason]}</Text>
              <Button component="a" href={api.downloadUrl(serverId, path)} download>Download</Button>
            </Stack>
          </Alert>
        )}

        {content?.editable && (
          <CodeMirror
            value={text}
            onChange={setText}
            extensions={extensions}
            theme="dark"
            height="calc(100vh - 210px)"
            style={{ borderRadius: 16, overflow: 'hidden', fontSize: 13 }}
          />
        )}
      </Container>

      {conflict && (
        <ConfirmModal
          title="The file changed on the server"
          confirmLabel="Overwrite"
          danger
          loading={saving}
          onConfirm={() => void save(true)}
          onClose={() => setConflict(false)}
        >
          <Text c="#4A505C">
            The file was changed on the server after you opened it. Those changes will be lost when you save, and the previous version goes to a backup copy.
          </Text>
        </ConfirmModal>
      )}
      {leaving && (
        <ConfirmModal title="Close without saving?" confirmLabel="Close" danger onConfirm={close} onClose={() => setLeaving(false)}>
          <Text c="#4A505C">Unsaved changes will be lost.</Text>
        </ConfirmModal>
      )}
    </>
  );
}
