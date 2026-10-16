param([string]$InstallerPath, [string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (-not $InstallerPath) { $InstallerPath = Join-Path $workspacePath 'dist\FolderNotifier-Setup.exe' }
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
$projectXml = [xml](Get-Content -LiteralPath (Join-Path $workspacePath 'FolderNotifier\FolderNotifier.csproj') -Raw)
$projectVersion = @($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$publishPath = if ($PublishDirectory) {
    if ([IO.Path]::IsPathRooted($PublishDirectory)) { [IO.Path]::GetFullPath($PublishDirectory) }
    else { [IO.Path]::GetFullPath((Join-Path $workspacePath $PublishDirectory)) }
} else { Join-Path $workspacePath "dist\FolderNotifier-$projectVersion" }
$smokeRoot = Join-Path $workspacePath ('dist\installer-smoke-' + [Guid]::NewGuid().ToString('N'))
$installPath = Join-Path $smokeRoot 'app'

function Assert-WorkspacePath([string]$Candidate) {
    $absolutePath = [IO.Path]::GetFullPath($Candidate)
    if (-not $absolutePath.StartsWith($workspacePath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Verification target is outside the workspace: $absolutePath"
    }
}
function Get-OptionalHash([string]$Path) {
    if (Test-Path -LiteralPath $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
    return $null
}
function Invoke-SmokeInstall([string]$LogName) {
    $logPath = Join-Path $smokeRoot $LogName
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /SMOKETEST=1 /TASKS="" /DIR="' + $installPath + '" /LOG="' + $logPath + '"'
    $process = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Smoke installation failed: $($process.ExitCode). See $logPath" }
}

Assert-WorkspacePath $smokeRoot
Assert-WorkspacePath $installPath
if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "Installer not found: $InstallerPath" }
New-Item -ItemType Directory -Path $smokeRoot | Out-Null
$settingsPath = Join-Path $env:LOCALAPPDATA 'FolderNotifier\settings.json'
$settingsBefore = Get-OptionalHash $settingsPath

Invoke-SmokeInstall 'install.log'
$payloadFiles = @(Get-ChildItem -LiteralPath $publishPath -Recurse -File | Where-Object Extension -ne '.pdb')
foreach ($sourceFile in $payloadFiles) {
    $relativePath = $sourceFile.FullName.Substring($publishPath.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
    $installedFile = Join-Path $installPath $relativePath
    if (-not (Test-Path -LiteralPath $installedFile)) { throw "Missing installed file: $relativePath" }
    if ((Get-OptionalHash $sourceFile.FullName) -ne (Get-OptionalHash $installedFile)) {
        throw "Installed file differs from publish: $relativePath"
    }
}
if (Get-ChildItem -LiteralPath $installPath -Recurse -Filter '*.pdb') { throw 'Debug symbols were unexpectedly installed.' }
Invoke-SmokeInstall 'upgrade.log'
if ((Get-OptionalHash (Join-Path $publishPath 'FolderNotifier.dll')) -ne (Get-OptionalHash (Join-Path $installPath 'FolderNotifier.dll'))) {
    throw 'Reinstall did not preserve the app payload.'
}
$uninstallerPath = Join-Path $installPath 'unins000.exe'
if (-not (Test-Path -LiteralPath $uninstallerPath)) { throw 'Uninstaller was not created.' }
$uninstallLog = Join-Path $smokeRoot 'uninstall.log'
$uninstallProcess = Start-Process -FilePath $uninstallerPath -ArgumentList ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SMOKETEST=1 /LOG="' + $uninstallLog + '"') -WindowStyle Hidden -Wait -PassThru
if ($uninstallProcess.ExitCode -ne 0) { throw "Uninstall failed: $($uninstallProcess.ExitCode)" }
for ($attempt = 0; $attempt -lt 20 -and (Test-Path -LiteralPath $installPath); $attempt++) {
    Start-Sleep -Milliseconds 250
}
if (Test-Path -LiteralPath $installPath) {
    $remainingFiles = @(Get-ChildItem -LiteralPath $installPath -Recurse -File)
    if ($remainingFiles.Count -gt 0) { throw 'The isolated installation still contains files after uninstall.' }
}
if ((Get-OptionalHash $settingsPath) -ne $settingsBefore) { throw 'User settings changed during installer verification.' }

$result = [ordered]@{
    success = $true
    installer = $InstallerPath
    installerSha256 = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    publishDirectory = $publishPath
    payloadFilesVerified = $payloadFiles.Count
    selfContained = (Test-Path -LiteralPath (Join-Path $publishPath 'coreclr.dll'))
    reinstallPassed = $true
    uninstallPassed = $true
    userSettingsUnchanged = $true
    userAppLaunched = $false
    shortcutsAndRegistrationSkippedForTest = $true
    smokeRoot = $smokeRoot
    verifiedAt = [DateTimeOffset]::Now.ToString('o')
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspacePath 'dist\installer-verification.json') -Encoding utf8
$result | ConvertTo-Json
