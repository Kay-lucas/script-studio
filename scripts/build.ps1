$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$appRoot = Join-Path $repoRoot 'build\ScriptStudio-Windows'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.8이 필요합니다.' }
New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
$source = Join-Path $repoRoot 'src\ScriptStudio.cs'
$output = Join-Path $appRoot 'ScriptStudio.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$output" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Net.Http.dll /reference:System.Runtime.Serialization.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $source
if ($LASTEXITCODE -ne 0) { throw '빌드하지 못했습니다.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\사용안내.txt') -Destination $appRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY.txt') -Destination $appRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'COPYRIGHT.md') -Destination $appRoot -Force
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'licenses') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $appRoot -Force }
Write-Output "빌드 완료: $output"
