#!/bin/sh
if [ "$1" = "ps" ]; then
  cat /opt/stub/ps.jsonl
  exit 0
fi
if [ "$1" = "restart" ] || [ "$1" = "stop" ] || [ "$1" = "start" ]; then
  if grep -q "\"name\":\"$2\"" /opt/stub/ps.jsonl; then
    echo "docker $*"
    exit 0
  fi
  echo "Error response from daemon: No such container: $2" >&2
  exit 1
fi
if [ "$1" = "compose" ]; then
  shift
  echo "docker compose $*"
  exit 0
fi
echo "docker stub: unsupported command $*" >&2
exit 1
