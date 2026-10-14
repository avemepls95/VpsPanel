#!/bin/sh
set -e
# Host keys are generated on start, so each container has its own fingerprint.
ssh-keygen -A >/dev/null
mkdir -p /root/.ssh
chmod 700 /root/.ssh
printf '%s\n' "$AUTHORIZED_KEY" > /root/.ssh/authorized_keys
chmod 600 /root/.ssh/authorized_keys
if [ "$DOCKER_STUB" = "1" ]; then
  ln -sf /opt/stub/docker /usr/local/bin/docker
  mkdir -p /srv/demo
fi
exec /usr/sbin/sshd -D -e
