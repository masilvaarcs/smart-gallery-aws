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

function Get-UrlSemAssinatura {
    param([string]$Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return $null
    }

    try {
        $uri = [System.Uri]$Url
        return $uri.GetLeftPart([System.UriPartial]::Path)
    }
    catch {
        return ($Url -split '\?')[0]
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$targetDir = Join-Path $repoRoot $OutputDir
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

$records = @(
    @{ Seed = "amazonia-rio"; Titulo = "Pôr do sol na rodovia"; Descricao = "Luz dourada sobre rodovia vista através de grade metálica de passarela."; Tags = "rodovia,pôr do sol,trânsito" },
    @{ Seed = "sampa-noturna"; Titulo = "Coiote na neve"; Descricao = "Retrato aproximado de coiote caminhando em cenário nevado."; Tags = "animal,neve,vida selvagem" },
    @{ Seed = "litoral-nordeste"; Titulo = "Show ao vivo"; Descricao = "Músico performando com guitarra em palco iluminado, foto em preto e branco."; Tags = "música,show,palco" },
    @{ Seed = "serra-neblina"; Titulo = "Pico nevado ao entardecer"; Descricao = "Montanha coberta de neve sob céu rosado ao fim do dia."; Tags = "montanha,neve,paisagem" },
    @{ Seed = "ponte-metal"; Titulo = "Trilha na floresta"; Descricao = "Caminho de terra entre árvores em floresta densa e úmida."; Tags = "trilha,floresta,natureza" },
    @{ Seed = "floresta-densa"; Titulo = "Coqueiros tropicais"; Descricao = "Coqueiros ao vento sob céu azul em paisagem tropical."; Tags = "coqueiro,tropical,verão" },
    @{ Seed = "deserto-luz"; Titulo = "Paredão rochoso na neblina"; Descricao = "Falésias rochosas cobertas de vegetação emergindo da neblina densa."; Tags = "neblina,rocha,vegetação" },
    @{ Seed = "porto-industrial"; Titulo = "Horizonte ao anoitecer"; Descricao = "Silhueta de montanhas ao anoitecer com céu em gradiente azul e laranja."; Tags = "horizonte,anoitecer,silhueta" },
    @{ Seed = "campo-aereo"; Titulo = "Porto na névoa"; Descricao = "Embarcações ancoradas em baía encoberta por neblina densa."; Tags = "barco,névoa,porto" },
    @{ Seed = "trilha-cachoeira"; Titulo = "Abutre em voo"; Descricao = "Grande abutre com asas abertas pousando sobre rocha."; Tags = "ave,voo,vida selvagem" }
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
            $utf8 = [System.Text.Encoding]::UTF8
            $multipart.Add($fileContent, "arquivo", $fileName)
            $multipart.Add([System.Net.Http.StringContent]::new($record.Titulo,  $utf8, "text/plain"), "titulo")
            $multipart.Add([System.Net.Http.StringContent]::new($record.Descricao, $utf8, "text/plain"), "descricao")
            $multipart.Add([System.Net.Http.StringContent]::new($record.Tags,    $utf8, "text/plain"), "tags")
            $multipart.Add([System.Net.Http.StringContent]::new("true", $utf8, "text/plain"), "publica")

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
        $entry.apiUrl = Get-UrlSemAssinatura -Url $response.url
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
