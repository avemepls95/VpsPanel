import { Box } from '@mantine/core';
import { Route, Routes } from 'react-router';
import { EditorPage } from './editor/EditorPage';
import { FilesPage } from './files/FilesPage';
import { RunsDrawer } from './runs/RunsDrawer';
import { RunsProvider } from './runs/RunsContext';
import { ServersPage } from './servers/ServersPage';
import { ui } from './theme';

export function App() {
  return (
    <RunsProvider>
      <Box mih="100vh" bg={ui.ground}>
        <Routes>
          <Route path="/" element={<ServersPage />} />
          <Route path="/files" element={<FilesPage />} />
          <Route path="/editor" element={<EditorPage />} />
        </Routes>
      </Box>
      <RunsDrawer />
    </RunsProvider>
  );
}
