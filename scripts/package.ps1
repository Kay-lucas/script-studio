$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$appRoot = Join-Path $repoRoot 'build\ScriptStudio-Windows'
foreach ($file in @('ScriptStudio.exe','models\ggml-base.en.bin','runtime\engine\Release\whisper-cli.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $appRoot $file))) { throw "필요한 파일이 없습니다: $file. build.ps1과 prepare-runtime.ps1을 먼저 실행하세요." }
}
$stageParent = Join-Path $repoRoot ('build\package-' + [Guid]::NewGuid().ToString('N'))
$stageRoot = Join-Path $stageParent 'ScriptStudio-Windows'
$distRoot = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $stageRoot,$distRoot -Force | Out-Null
try {
    foreach ($file in @('ScriptStudio.exe','사용안내.txt','COPYRIGHT.md','THIRD_PARTY.txt','WHISPER_CPP_LICENSE.txt','WHISPER_MODEL_LICENSE.txt')) { Copy-Item -LiteralPath (Join-Path $appRoot $file) -Destination $stageRoot }
    foreach ($folder in @('models','runtime')) { Copy-Item -LiteralPath (Join-Path $appRoot $folder) -Destination $stageRoot -Recurse }
    $sourceRoot = Join-Path $stageRoot 'source'
    New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src\ScriptStudio.cs') -Destination $sourceRoot
    $archive = Join-Path $distRoot 'ScriptStudio-Windows.zip'
    Compress-Archive -LiteralPath $stageRoot -DestinationPath $archive -CompressionLevel Optimal -Force
    Write-Output "패키지 완료: $archive"
} finally {
    $resolvedStage = (Resolve-Path -LiteralPath $stageParent).Path
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'build')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStage.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '패키지 임시 폴더 범위 확인 실패' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
