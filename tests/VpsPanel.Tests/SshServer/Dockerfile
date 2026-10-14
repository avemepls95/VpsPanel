FROM debian:trixie-slim
RUN apt-get update \
 && apt-get install -y --no-install-recommends openssh-server tar gzip coreutils findutils procps \
 && rm -rf /var/lib/apt/lists/* \
 && rm -f /etc/ssh/ssh_host_* \
 && mkdir -p /run/sshd /opt/stub \
 && sed -i 's/^#\?PermitRootLogin .*/PermitRootLogin prohibit-password/' /etc/ssh/sshd_config
COPY entrypoint.sh /entrypoint.sh
COPY docker-stub.sh /opt/stub/docker
COPY ps.jsonl /opt/stub/ps.jsonl
RUN sed -i 's/\r$//' /entrypoint.sh /opt/stub/docker \
 && chmod +x /entrypoint.sh /opt/stub/docker
EXPOSE 22
ENTRYPOINT ["/entrypoint.sh"]
