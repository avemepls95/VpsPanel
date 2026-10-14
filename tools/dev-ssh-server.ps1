# Starts the test SSH server from tests/VpsPanel.Tests/SshServer on 127.0.0.1:2222 with a docker stub
# and writes .dev-data/settings.json with this server. Real servers are not used.
$root = Split-Path $PSScriptRoot -Parent
$dataDir = Join-Path $root '.dev-data'
$key = Join-Path $dataDir 'dev_key'
New-Item -ItemType Directory -Force $dataDir | Out-Null

if (-not (Test-Path $key)) {
    ssh-keygen -t ed25519 -N '""' -f $key -q
    if ($LASTEXITCODE -ne 0) { throw 'ssh-keygen failed' }
}

docker build -t vpspanel-dev-sshd (Join-Path $root 'tests\VpsPanel.Tests\SshServer')
if ($LASTEXITCODE -ne 0) { throw 'docker build failed' }

cmd /c "docker rm -f vpspanel-dev-sshd >nul 2>&1"
$publicKey = (Get-Content "$key.pub" -Raw).Trim()
docker run -d --name vpspanel-dev-sshd -p 127.0.0.1:2222:22 -e "AUTHORIZED_KEY=$publicKey" -e DOCKER_STUB=1 vpspanel-dev-sshd | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'docker run failed' }

$settings = @{
    servers = @(
        @{
            id = 'dev'
            name = 'Test'
            host = '127.0.0.1'
            port = 2222
            user = 'root'
            keyPath = $key
            composeProjects = @{}
            manualProjects = @()
        }
    )
}
$settings | ConvertTo-Json -Depth 6 | Out-File -FilePath (Join-Path $dataDir 'settings.json') -Encoding utf8
Write-Host "Done. Backend: dotnet run --project src/VpsPanel -- --VpsPanel:DataDir=$dataDir --VpsPanel:OpenBrowser=false"
