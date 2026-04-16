param(
    [string]$Profile = "marcos-admin",
    [string]$Region = "us-east-1",
    [string]$StackName = "dotnet-serverless-gallery",
    [string]$ApiUrl,
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

function Resolve-CommandPath {
    param(
        [string]$Command,
        [string]$Fallback
    )

    $resolved = Get-Command $Command -ErrorAction SilentlyContinue
    if ($resolved) {
        return $resolved.Source
    }

    if ($Fallback -and (Test-Path $Fallback)) {
        return $Fallback
    }

    throw "Comando '$Command' nao encontrado."
}

function Run-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    try {
        Write-Host "[RUN] $Name" -ForegroundColor Cyan
        $null = & $Action
        return @{ Name = $Name; Status = "OK"; Message = "Concluido" }
    }
    catch {
        Write-Host "[ERRO] $Name" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        return @{ Name = $Name; Status = "ERRO"; Message = $_.Exception.Message }
    }
}

function Invoke-Checked {
    param(
        [scriptblock]$Action,
        [string]$ErrorMessage
    )

    & $Action
    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { [int]$LASTEXITCODE }
    if ($exitCode -ne 0) {
        throw "$ErrorMessage (exit code: $exitCode)"
    }
}

function Invoke-LoggedCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$LogPath,
        [string]$ErrorMessage
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    $psi.Arguments = ($Arguments -join " ")
    $psi.WorkingDirectory = $repoRoot
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $null = $process.Start()

    # Leitura assincrona para evitar deadlock de buffer
    $stdOutTask = $process.StandardOutput.ReadToEndAsync()
    $stdErrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()

    $stdOut = $stdOutTask.GetAwaiter().GetResult()
    $stdErr = $stdErrTask.GetAwaiter().GetResult()

    $combined = ($stdOut + $stdErr).TrimEnd()
    Set-Content -Path $LogPath -Value $combined -Encoding utf8

    if ($combined) {
        Write-Output $combined
    }

    if ($process.ExitCode -ne 0) {
        throw "$ErrorMessage (exit code: $($process.ExitCode))"
    }
}

function Get-UrlSemAssinatura {
    param([string]$Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return $Url
    }

    try {
        $uri = [System.Uri]$Url
        return $uri.GetLeftPart([System.UriPartial]::Path)
    }
    catch {
        return ($Url -split '\?')[0]
    }
}

function Sanitizar-UrlsAssinadas {
    param([object]$Payload)

    if ($null -eq $Payload) {
        return $Payload
    }

    if ($Payload -is [System.Collections.IEnumerable] -and $Payload -isnot [string]) {
        foreach ($item in $Payload) {
            Sanitizar-UrlsAssinadas -Payload $item | Out-Null
        }
        return $Payload
    }

    $props = $Payload.PSObject.Properties
    if (-not $props) {
        return $Payload
    }

    foreach ($prop in $props) {
        $name = $prop.Name.ToLowerInvariant()
        $value = $prop.Value

        if ($value -is [string] -and ($name -eq "url" -or $name -eq "apiurl" -or $name -eq "urlthumbnail" -or $name -eq "urlassinada")) {
            $Payload.$($prop.Name) = Get-UrlSemAssinatura -Url $value
            continue
        }

        if ($value -isnot [string] -and $null -ne $value) {
            Sanitizar-UrlsAssinadas -Payload $value | Out-Null
        }
    }

    return $Payload
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$evidenceRoot = Join-Path $repoRoot "evidencias"
$runId = Get-Date -Format "yyyyMMdd_HHmmss"
$runDir = Join-Path $evidenceRoot "run_$runId"
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

$awsCmd = Resolve-CommandPath -Command "aws" -Fallback "C:\Program Files\Amazon\AWSCLIV2\aws.exe"
$samCmd = if (Test-Path "C:\Program Files\Amazon\AWSSAMCLI\bin\sam.cmd") {
    "C:\Program Files\Amazon\AWSSAMCLI\bin\sam.cmd"
}
else {
    Resolve-CommandPath -Command "sam" -Fallback "C:\Program Files\Amazon\AWSSAMCLI\runtime\Scripts\sam.exe"
}
$dotnetCmd = Resolve-CommandPath -Command "dotnet" -Fallback $null

Set-Location $repoRoot

$results = @()

$results += Run-Step -Name "AWS STS Identity" -Action {
    Invoke-Checked -ErrorMessage "Falha ao consultar STS" -Action {
        & $awsCmd sts get-caller-identity --profile $Profile --region $Region | Tee-Object -FilePath (Join-Path $runDir "01_sts_identity.json")
    }
}

$results += Run-Step -Name "Unit Tests" -Action {
    Invoke-LoggedCommand -FilePath $dotnetCmd -Arguments @(
        "test",
        "tests/SmartGallery.Tests/SmartGallery.Tests.csproj",
        "--configuration", "Release",
        "--logger", '"trx;LogFileName=unit-tests.trx"',
        "--results-directory", ('"' + $runDir + '"')
    ) -LogPath (Join-Path $runDir "02_unit_tests.log") -ErrorMessage "Falha nos testes unitarios"
}

$results += Run-Step -Name "SAM Validate" -Action {
    Invoke-LoggedCommand -FilePath $samCmd -Arguments @(
        "validate", "-t", "infra/template.yaml", "--profile", $Profile, "--region", $Region
    ) -LogPath (Join-Path $runDir "03_sam_validate.log") -ErrorMessage "Falha no sam validate"
}

$results += Run-Step -Name "SAM Build" -Action {
    Invoke-LoggedCommand -FilePath $samCmd -Arguments @(
        "build", "-t", "infra/template.yaml", "--profile", $Profile, "--region", $Region
    ) -LogPath (Join-Path $runDir "04_sam_build.log") -ErrorMessage "Falha no sam build"
}

if ($Deploy.IsPresent) {
    $results += Run-Step -Name "SAM Deploy" -Action {
        Invoke-LoggedCommand -FilePath $samCmd -Arguments @(
            "deploy",
            "--template-file", ".aws-sam/build/template.yaml",
            "--stack-name", $StackName,
            "--resolve-s3",
            "--capabilities", "CAPABILITY_IAM", "CAPABILITY_NAMED_IAM",
            "--profile", $Profile,
            "--region", $Region,
            "--no-fail-on-empty-changeset",
            "--no-confirm-changeset"
        ) -LogPath (Join-Path $runDir "05_sam_deploy.log") -ErrorMessage "Falha no sam deploy"
    }

    $stackApiUrl = & $awsCmd cloudformation describe-stacks --stack-name $StackName --profile $Profile --region $Region --query "Stacks[0].Outputs[?OutputKey=='ApiUrl'].OutputValue" --output text
    if ($stackApiUrl -and $stackApiUrl.Trim() -ne "None") {
        $ApiUrl = $stackApiUrl.Trim()
        Set-Content -Path (Join-Path $runDir "06_api_url.txt") -Value $ApiUrl
    }
}

if ($ApiUrl) {
    $base = $ApiUrl.TrimEnd('/')

    $results += Run-Step -Name "Integration Health" -Action {
        $health = Invoke-RestMethod -Method Get -Uri "$base/health" -TimeoutSec 30
        $health | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $runDir "07_health.json")
    }

    $results += Run-Step -Name "Integration List Images" -Action {
        $images = Invoke-RestMethod -Method Get -Uri "$base/api/imagens" -TimeoutSec 30
        $images = Sanitizar-UrlsAssinadas -Payload $images
        $images | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runDir "08_listagem.json")
    }

    $results += Run-Step -Name "Integration Stats" -Action {
        $stats = Invoke-RestMethod -Method Get -Uri "$base/api/imagens/stats" -TimeoutSec 30
        $stats | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runDir "09_stats.json")
    }
}
else {
    $results += @{ Name = "Integration Tests"; Status = "PULADO"; Message = "ApiUrl nao informada e deploy nao executado." }
}

$summaryPath = Join-Path $runDir "SUMMARY.md"
"# Evidencias - Dotnet Serverless Gallery" | Set-Content $summaryPath
"" | Add-Content $summaryPath
"- Data: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Add-Content $summaryPath
"- Profile: $Profile" | Add-Content $summaryPath
"- Region: $Region" | Add-Content $summaryPath
"- Stack: $StackName" | Add-Content $summaryPath
if ($ApiUrl) { "- ApiUrl: $ApiUrl" | Add-Content $summaryPath }
"" | Add-Content $summaryPath
"## Resultado das Etapas" | Add-Content $summaryPath

foreach ($result in $results) {
    "- [$($result.Status)] $($result.Name): $($result.Message)" | Add-Content $summaryPath
}

Write-Host ""
Write-Host "Evidencias geradas em: $runDir" -ForegroundColor Green
Write-Host "Resumo: $summaryPath" -ForegroundColor Green
