param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'FolderNotifier\FolderNotifier.csproj'
$projectInfo = [xml](Get-Content -LiteralPath $projectPath -Raw)
$appVersion = [string]$projectInfo.Project.PropertyGroup.Version
$outputName = if ($SelfContained) { "FolderNotifier-$appVersion" } else { 'FolderNotifier' }
$outputPath = Join-Path $PSScriptRoot "dist\$outputName"
$contained = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish $projectPath -c Release -r win-x64 --self-contained $contained -o $outputPath -m:1 -nr:false
if ($LASTEXITCODE -ne 0) { throw '앱 빌드 실패' }
Write-Host "실행 파일: $(Join-Path $outputPath 'FolderNotifier.exe')"
