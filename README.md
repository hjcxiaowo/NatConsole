# NATConsole

一个轻量的“内网穿透/反向代理”控制台工具：在 **公网 Linux** 上运行 `server`，在 **内网 Windows/Linux** 上运行 `client`，把本地服务通过公网中转暴露出去，便于外网联调与测试。

## 功能概览

- **控制通道**：客户端与服务端通过 `ControlPort` 建立长连接注册隧道
- **HTTP 入口（路径模式）**：统一入口 `HttpPort`，按路径前缀访问：`/t/{隧道名}/...`
- **HTTP 入口（独立端口模式）**：为某条隧道分配独立公网端口，直接映射到站点根路径 `/`
- **多隧道并行**：一次启动多个 `Client.Tunnels`
- **可选响应重写**：用于路径前缀模式下的页面/资源引用修正（如 HTML/CSS/JS 链接）

## 快速开始

### 1) 准备配置（强烈建议不要提交真实配置）

仓库内提供：

- `appsettings.Development.json.example`：开发环境示例（无真实 IP/Token）
- `appsettings.Production.json.example`：生产环境示例（无真实 IP/Token）

在实际运行目录中准备 `appsettings.json`（建议做法）：

- 开发：复制 `appsettings.Development.json.example` 为 `appsettings.Development.local.json`（仅本地）或直接复制为 `appsettings.json`
- 生产：复制 `appsettings.Production.json.example` 为 `appsettings.Production.local.json`（仅本地）或直接复制为 `appsettings.json`，并填入真实公网 IP/域名、内网地址、Token

> 本仓库已在 `.gitignore` 中忽略 `appsettings.json`，避免误提交 Token/内网 IP 等敏感信息。

### 2) 运行服务端（公网机）

```bash
NATConsole server
```

### 3) 运行客户端（内网机）

```bash
NATConsole client
```

启动后会打印每条隧道对应的外网访问地址。

## 配置说明

默认从程序所在目录读取 `appsettings.json`。

也支持显式指定配置文件：

```bash
NATConsole server --config /path/to/appsettings.json
NATConsole client --config /path/to/appsettings.json
```

### 配置结构

#### `Token`

服务端与客户端的共享密钥。**请勿提交到 Git**，也不要在公开场合泄露。

#### `Server`

- `BindHost`：监听地址（通常 `0.0.0.0`）
- `ControlPort`：控制通道端口（默认 `7000`）
- `HttpPort`：路径模式 HTTP 入口端口（默认 `8080`），访问规则：`http://{PublicHost}:{HttpPort}/t/{隧道名}/...`
- `PublicHost`：公网 IP/域名（用于打印提示 URL；推荐填写真实公网 IP 或域名）

#### `Client`

- `ServerHost` / `ServerPort`：服务端公网地址与 `ControlPort`
- `Tunnels[]`：隧道列表

每条隧道 `TunnelEntry`：

- `Id`：隧道名（URL 中使用）
- `LocalHost` / `LocalPort`：本地服务监听地址与端口
- `ForwardHost`：可选，转发请求时使用的 `Host`（用于适配后端/反代对 Host 的校验）
- `RewriteResponsePaths`：是否启用路径前缀模式的响应重写（当 `DedicatedHttpPort > 0` 时通常应为 `false`）
- `DedicatedHttpPort`：独立公网端口。`>0` 时，该隧道对外访问为：`http://{PublicHost}:{DedicatedHttpPort}/`（根路径）

## 开发 / 生产配置推荐（dev/prod 拆分）

### 开发（Development）

- `Token`：可用测试 token
- `Server.PublicHost`：可填 `127.0.0.1`
- `Client.ServerHost`：可填 `127.0.0.1`（同机跑 server+client 时）
- `Client.Tunnels[].ForwardHost`：按内网环境填写（这属于隐私信息，建议只放在 `appsettings.Development.local.json`）

### 生产（Production）

- `Token`：务必更换为随机高强度值
- `Server.PublicHost`：填写公网 IP 或域名
- `Client.ServerHost`：同上
- 若暴露后台页面且希望前端不改路径：推荐给后台隧道设置 `DedicatedHttpPort`，使其根路径访问（避免 `/t/{id}/` 前缀导致的资源路径问题）

> 生产配置建议放在 **不入库** 的 `appsettings.json` 或 `appsettings.Production.local.json` 中，通过 `--config` 指定。

## 环境变量覆盖（适合 CI / 容器）

程序支持环境变量前缀 `NATCONSOLE_` 覆盖配置（例如 `NATCONSOLE_Token=...`）。

## Linux + Docker 部署（推荐方式：自包含 + runtime-deps）

本项目提供 `publish-linux.ps1` 发布脚本（Windows PowerShell）：

```powershell
.\publish-linux.ps1 docker
```

将生成的目录（默认 `bin\Release\net10.0\publish\linux-x64-sc`）上传到 Linux，例如 `/root/nat/linux-x64-sc/`，并确保其中包含你的 `appsettings.json`。

在 Linux 上使用脚本一键启动：

```bash
bash linux-docker-run.sh /root/nat/linux-x64-sc
docker logs -f natconsole
```

脚本内部使用镜像：

- `mcr.microsoft.com/dotnet/runtime-deps:10.0`

以及启动命令：

- `./NATConsole server`

## 安全建议

- **不要把真实 `appsettings.json` 提交 Git**（Token、公网 IP、内网 IP/端口都属于敏感信息）
- 公网安全组/防火墙只放行必须端口（通常为 `ControlPort`、`HttpPort`、以及用到的 `DedicatedHttpPort`）
- Token 一旦泄露，等同于“任何人都可能注册隧道并转发你的内网服务”，请立即更换

## 常见问题（FAQ）

### 1) 访问页面白屏/资源 404？

- 如果通过 `HttpPort` 的 `/t/{id}/` 前缀访问静态站点，可能出现资源路径问题
- 解决：为该隧道设置 `DedicatedHttpPort`（根路径访问），并将该隧道 `RewriteResponsePaths=false`

### 2) Docker 里启动报 `Could not resolve CoreCLR path`？

- 多见于“框架依赖（FDD）+ 运行时版本不匹配”
- 建议使用本 README 的 **自包含（SC）+ runtime-deps** 方式部署

---

如果你准备把它开源发布，建议再加一份 `LICENSE`（MIT/Apache-2.0 等）并在 README 顶部标注许可证。
