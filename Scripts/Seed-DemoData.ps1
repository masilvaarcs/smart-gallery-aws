param(
    [Parameter(Mandatory = $true)]
    [string]$ApiUrl,
    [string]$OutputDir = "Dados/DemoSeed",
    [switch]$SkipDownload,
    [switch]$DownloadOnly,
    [switch]$ForceDownload
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

Add-Type -AssemblyName System.Net.Http

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$targetDir = Join-Path $repoRoot $OutputDir
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

$records = @(
    @{ Seed = "amazonia-rio"; Titulo = "Rio Amazonas ao amanhecer"; Descricao = "Imagem de demonstração com foco em natureza e escala."; Tags = "natureza,amazonia,agua" },
    @{ Seed = "sampa-noturna"; Titulo = "Centro urbano noturno"; Descricao = "Cena urbana para validar metadados por tema."; Tags = "cidade,noturno,arquitetura" },
    @{ Seed = "litoral-nordeste"; Titulo = "Litoral brasileiro"; Descricao = "Praia e mar para teste de busca por contexto."; Tags = "praia,mar,turismo" },
    @{ Seed = "serra-neblina"; Titulo = "Serra com neblina"; Descricao = "Paisagem montanhosa para diversidade de coleção."; Tags = "serra,neblina,paisagem" },
    @{ Seed = "ponte-metal"; Titulo = "Ponte metálica histórica"; Descricao = "Estrutura urbana para validar listagem e filtros."; Tags = "ponte,estrutura,urbano" },
    @{ Seed = "floresta-densa"; Titulo = "Floresta densa"; Descricao = "Composição verde para teste de dados ambientais."; Tags = "floresta,verde,ecologia" },
    @{ Seed = "deserto-luz"; Titulo = "Deserto com luz dura"; Descricao = "Cena árida para ampliar variedade visual da base."; Tags = "deserto,sol,geografia" },
    @{ Seed = "porto-industrial"; Titulo = "Porto industrial"; Descricao = "Imagem temática de logística e infraestrutura."; Tags = "porto,industria,logistica" },
    @{ Seed = "campo-aereo"; Titulo = "Campo visto do alto"; Descricao = "Perspectiva aérea para validar metadados diversos."; Tags = "aereo,campo,agro" },
    @{ Seed = "trilha-cachoeira"; Titulo = "Trilha para cachoeira"; Descricao = "Foto de aventura para enriquecer dataset de demonstração."; Tags = "trilha,cachoeira,aventura" }
)

$baseApi = $ApiUrl.TrimEnd('/')
$manifest = @()
$httpClient = [System.Net.Http.HttpClient]::new()

for ($i = 0; $i -lt $records.Count; $i++) {
    $index = $i + 1
    $record = $records[$i]
    $fileName = "demo_{0:D2}_{1}.jpg" -f $index, $record.Seed
    $filePath = Join-Path $targetDir $fileName
    $sourceUrl = "https://picsum.photos/seed/smart-gallery-{0}/1200/800.jpg" -f $record.Seed

    if (-not $SkipDownload -and ($ForceDownload -or -not (Test-Path $filePath))) {
        Write-Host "[DOWNLOAD] $fileName" -ForegroundColor Cyan
        Invoke-WebRequest -Uri $sourceUrl -OutFile $filePath -MaximumRedirection 5
    }

    if (-not (Test-Path $filePath)) {
        throw "Arquivo ausente para upload: $filePath"
    }

    $entry = [ordered]@{
        index = $index
        seed = $record.Seed
        titulo = $record.Titulo
        descricao = $record.Descricao
        tags = $record.Tags
        sourceUrl = $sourceUrl
        fileName = $fileName
        filePath = $filePath
        uploaded = $false
        apiId = $null
        apiUrl = $null
    }

    if (-not $DownloadOnly) {
        Write-Host "[UPLOAD] $fileName" -ForegroundColor Yellow
        $multipart = [System.Net.Http.MultipartFormDataContent]::new()
        $stream = [System.IO.File]::OpenRead($filePath)
        try {
            $fileContent = [System.Net.Http.StreamContent]::new($stream)
            $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("image/jpeg")
            $multipart.Add($fileContent, "arquivo", $fileName)
            $multipart.Add([System.Net.Http.StringContent]::new($record.Titulo), "titulo")
            $multipart.Add([System.Net.Http.StringContent]::new($record.Descricao), "descricao")
            $multipart.Add([System.Net.Http.StringContent]::new($record.Tags), "tags")

            $httpResponse = $httpClient.PostAsync("$baseApi/api/imagens", $multipart).GetAwaiter().GetResult()
            $responseBody = $httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $httpResponse.IsSuccessStatusCode) {
                throw "Falha no upload ($($httpResponse.StatusCode)): $responseBody"
            }
            $response = $responseBody | ConvertFrom-Json
        }
        finally {
            $stream.Dispose()
            $multipart.Dispose()
        }

        $entry.uploaded = $true
        $entry.apiId = $response.id
        $entry.apiUrl = $response.url
    }

    $manifest += [pscustomobject]$entry
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$manifestPath = Join-Path $targetDir "manifest_$stamp.json"
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "" 
Write-Host "Manifesto gerado em: $manifestPath" -ForegroundColor Green
if (-not $DownloadOnly) {
    Write-Host "Registros enviados: $($manifest.Count)" -ForegroundColor Green
}
$httpClient.Dispose()
