#!/bin/bash
# Linux 上启动 NATConsole 服务端（Docker 自包含包，推荐）
set -e
APP_DIR="${1:-/root/nat/linux-x64-sc}"

docker rm -f natconsole 2>/dev/null || true

docker run -d --name natconsole --restart unless-stopped \
  -p 7000:7000 -p 8080:8080 -p 8081:8081 \
  -v "$APP_DIR:/app" -w /app \
  mcr.microsoft.com/dotnet/runtime-deps:10.0 \
  ./NATConsole server

echo "日志: docker logs -f natconsole"
