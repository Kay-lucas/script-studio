$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$appRoot = Join-Path $repoRoot 'build\ScriptStudio-Windows'
if (-not (Test-Path -LiteralPath (Join-Path $appRoot 'ScriptStudio.exe'))) { throw 'build.ps1을 먼저 실행하세요.' }
$cacheRoot = Join-Path $repoRoot 'build\downloads'
$engineRoot = Join-Path $appRoot 'runtime\engine\Release'
$modelRoot = Join-Path $appRoot 'models'
foreach ($dir in @($cacheRoot, $engineRoot, $modelRoot)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
function Get-VerifiedFile {
    param([string]$Url, [string]$Destination, [string]$Algorithm, [string]$ExpectedHash)
    if (Test-Path -LiteralPath $Destination) {
        if ((Get-FileHash -LiteralPath $Destination -Algorithm $Algorithm).Hash -eq $ExpectedHash) {
            Write-Output "확인된 파일 재사용: $Destination"
            return
        }
    }
    $partial = $Destination + '.part'
    try {
        Write-Output "다운로드: $Url"
        $web = New-Object Net.WebClient
        try { $web.DownloadFile($Url, $partial) } finally { $web.Dispose() }
        if ((Get-FileHash -LiteralPath $partial -Algorithm $Algorithm).Hash -ne $ExpectedHash) { throw "파일 검증 실패: $Destination" }
        Move-Item -LiteralPath $partial -Destination $Destination -Force
    } finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    }
}
$engineZip = Join-Path $cacheRoot 'whisper-bin-x64-b5454.zip'
Get-VerifiedFile -Url 'https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-bin-x64.zip' -Destination $engineZip -Algorithm SHA256 -ExpectedHash '6ba69e3482d7826214f90a6a9c84ca07782aec1e1d0c6a7c30c994fd5d816ccb'
$extractRoot = Join-Path $cacheRoot 'engine-b5454'
Expand-Archive -LiteralPath $engineZip -DestinationPath $extractRoot -Force
$nativeDir = Split-Path -Path (Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter 'whisper-cli.exe' | Select-Object -First 1).FullName -Parent
if (-not $nativeDir) { throw '공식 배포 파일에서 실행 도구를 찾지 못했습니다.' }
Get-ChildItem -LiteralPath $nativeDir -File | Where-Object { $_.Name -eq 'whisper-cli.exe' -or $_.Name -eq 'whisper.dll' -or $_.Name -like 'ggml*.dll' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $engineRoot -Force }
Get-VerifiedFile -Url 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin?download=true' -Destination (Join-Path $modelRoot 'ggml-base.en.bin') -Algorithm SHA1 -ExpectedHash '137c40403d78fd54d454da0f9bd998f78703390c'
Write-Output '실행 도구와 기본 모델 준비 완료'
