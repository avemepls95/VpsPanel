#!/bin/sh
if [ "$1" = "ps" ]; then
  cat /opt/stub/ps.jsonl
  exit 0
fi
if [ "$1" = "compose" ]; then
  shift
  echo "docker compose $*"
  exit 0
fi
echo "docker stub: unsupported command $*" >&2
exit 1
