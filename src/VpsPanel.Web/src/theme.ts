import { createTheme, type MantineColorsTuple } from '@mantine/core';

const brand: MantineColorsTuple = [
  '#eef1ff', '#dce3ff', '#b6c4ff', '#8ea2ff', '#6b84f6',
  '#4a66eb', '#2f4fe0', '#2643c7', '#1d38ad', '#152c8f',
];

export const theme = createTheme({
  primaryColor: 'brand',
  primaryShade: 6,
  colors: { brand },
  fontFamily: 'Onest, sans-serif',
  fontFamilyMonospace: '"JetBrains Mono", monospace',
  headings: { fontFamily: 'Onest, sans-serif', fontWeight: '700' },
  defaultRadius: 'md',
});

export const ui = {
  ink: '#14161A',
  ground: '#ECEEF2',
  panel: '#F6F7F9',
  border: '#DDE0E6',
  muted: '#5B6170',
  ok: '#1FA463',
  warn: '#E59A1A',
  bad: '#E5484D',
  neutral: '#9AA1AE',
  console: '#0F1115',
} as const;

export const unitColors = {
  compose: { bg: '#E8ECFF', fg: '#2F4FE0' },
  manual: { bg: '#EFE7FF', fg: '#6B3FD4' },
  standalone: { bg: '#DDF3EF', fg: '#0E7A6E' },
  timers: { bg: '#FDF0D9', fg: '#9A5B00' },
} as const;
