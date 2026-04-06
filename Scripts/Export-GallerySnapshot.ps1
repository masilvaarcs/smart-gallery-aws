param(
    [Parameter(Mandatory = $true)]
    [string]$ApiUrl,
    [string]$OutputDir = "evidencias"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$targetRoot = Join-Path $repoRoot $OutputDir
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$runDir = Join-Path $targetRoot "snapshot_$stamp"
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

$baseApi = $ApiUrl.TrimEnd('/')

$health = Invoke-RestMethod -Method Get -Uri "$baseApi/health" -TimeoutSec 60
$stats = Invoke-RestMethod -Method Get -Uri "$baseApi/api/imagens/stats" -TimeoutSec 60
$list = Invoke-RestMethod -Method Get -Uri "$baseApi/api/imagens?limite=100" -TimeoutSec 60

$health | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $runDir "health.json") -Encoding utf8
$stats | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runDir "stats.json") -Encoding utf8
$list | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runDir "listagem.json") -Encoding utf8

$summary = @()
$summary += "# Snapshot da Galeria"
$summary += ""
$summary += "- Data: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$summary += "- ApiUrl: $baseApi"
$summary += "- Health: $($health.status)"
$summary += "- Total de imagens: $($list.total)"
$summary += "- Total bytes: $($stats.totalBytes)"
$summary += ""
$summary += "## Arquivos"
$summary += "- health.json"
$summary += "- stats.json"
$summary += "- listagem.json"
$summaryPath = Join-Path $runDir "SUMMARY.md"
$summary -join "`r`n" | Set-Content -Path $summaryPath -Encoding utf8

Write-Host "Snapshot salvo em: $runDir" -ForegroundColor Green
