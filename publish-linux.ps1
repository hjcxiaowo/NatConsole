# 发布 Linux 可执行包
# 用法: .\publish-linux.ps1 docker  # 推荐：Docker + runtime-deps:10.0
#       .\publish-linux.ps1 fdd     # 框架依赖，容器内易遇 CoreCLR 问题
#       .\publish-linux.ps1 musl    # musl 自包含（部分宿主机缺 loader）

param(
    [ValidateSet("musl", "fdd", "x64", "docker")]
    [string]$Target = "docker"
)

$OutRoot = Join-Path $PSScriptRoot "bin\Release\net10.0\publish"

switch ($Target) {
    "musl" {
        $rid = "linux-musl-x64"
        $sc = "true"
        $dir = Join-Path $OutRoot "linux-musl-x64"
    }
    "fdd" {
        $rid = "linux-x64"
        $sc = "false"
        $dir = Join-Path $OutRoot "linux-x64-fdd"
    }
    "docker" {
        $rid = "linux-x64"
        $sc = "true"
        $dir = Join-Path $OutRoot "linux-x64-sc"
    }
    default {
        $rid = "linux-x64"
        $sc = "true"
        $dir = Join-Path $OutRoot "linux-x64"
    }
}

Write-Host "发布 $rid self-contained=$sc -> $dir" -ForegroundColor Cyan
dotnet publish -c Release -r $rid --self-contained $sc -o $dir

Write-Host "`n完成。上传到 Linux 后:" -ForegroundColor Green
if ($sc -eq "true") {
    Write-Host "  chmod +x NATConsole && ./NATConsole server"
    if ($Target -eq "docker") {
        Write-Host "  Docker: bash linux-docker-run.sh /root/nat/linux-x64-sc"
    }
} else {
    Write-Host "  Docker: 用 ./NATConsole server，勿用 dotnet NATConsole.dll"
    Write-Host "  docker run ... mcr.microsoft.com/dotnet/runtime:10.0 /app/NATConsole server"
}
