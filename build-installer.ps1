param(
    [switch]$SkipPublish,
    [string]$AppVersion = '1.3.1',
    [string]$PublishDirectory,
    [string]$StagingRoot,
    [string]$CompilerPath
)

$ErrorActionPreference = 'Stop'
$workspacePath = $PSScriptRoot
$publishPath = if ($PublishDirectory) {
    if ([IO.Path]::IsPathRooted($PublishDirectory)) { [IO.Path]::GetFullPath($PublishDirectory) }
    else { [IO.Path]::GetFullPath((Join-Path $workspacePath $PublishDirectory)) }
} else { Join-Path $workspacePath "dist\FolderNotifier-$AppVersion" }
$distPath = Join-Path $workspacePath 'dist'
$installerSource = Join-Path $workspacePath 'installer\FolderNotifier.iss'
$bootstrapVersion = '7.1.0'
$bootstrapHash = '0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f'
$compilerRoot = Join-Path $workspacePath 'tools\InnoSetup'

if ($AppVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw 'AppVersion must have three or four numeric components.'
}

if (-not $SkipPublish) {
    & (Join-Path $workspacePath 'build.ps1') -SelfContained
}
foreach ($requiredFile in @('FolderNotifier.exe', 'FolderNotifier.runtimeconfig.json', 'coreclr.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $requiredFile))) {
        throw "Self-contained publish is missing $requiredFile. Run .\build.ps1 -SelfContained first."
    }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishPath 'FolderNotifier.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtimeConfig.runtimeOptions.includedFrameworks) {
    throw 'The payload is not self-contained. A separate .NET install must not be required.'
}

if (-not $CompilerPath) {
    $CompilerPath = Join-Path $compilerRoot 'compiler\ISCC.exe'
    if (-not (Test-Path -LiteralPath $CompilerPath)) {
        New-Item -ItemType Directory -Force -Path $compilerRoot | Out-Null
        $bootstrapPath = Join-Path $compilerRoot "innosetup-$bootstrapVersion-x64.exe"
        if (-not (Test-Path -LiteralPath $bootstrapPath)) {
            $bootstrapUrl = "https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-$bootstrapVersion-x64.exe"
            Write-Host "Downloading official Inno Setup $bootstrapVersion compiler..."
            Invoke-WebRequest -Uri $bootstrapUrl -OutFile $bootstrapPath
        }
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $bootstrapPath).Hash
        if ($actualHash -ne $bootstrapHash) { throw 'Inno Setup download SHA-256 verification failed.' }
        $signature = Get-AuthenticodeSignature -LiteralPath $bootstrapPath
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Pyrsys B\.V\.') {
            throw 'Inno Setup publisher signature verification failed.'
        }
        $compilerTarget = Join-Path $compilerRoot 'compiler'
        $bootstrapLog = Join-Path $compilerRoot 'bootstrap.log'
        $bootstrapArgs = '/PORTABLE=1 /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /TASKS="" /DIR="' + $compilerTarget + '" /LOG="' + $bootstrapLog + '"'
        $bootstrapProcess = Start-Process -FilePath $bootstrapPath -ArgumentList $bootstrapArgs -WindowStyle Hidden -Wait -PassThru
        if ($bootstrapProcess.ExitCode -ne 0) { throw "Compiler preparation failed: $($bootstrapProcess.ExitCode)" }
    }
}
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw "Compiler not found: $CompilerPath" }

New-Item -ItemType Directory -Force -Path $distPath | Out-Null
$stagingRootPath = if ($StagingRoot) { [IO.Path]::GetFullPath($StagingRoot) } else { [IO.Path]::GetFullPath([IO.Path]::GetTempPath()) }
$stagingRootPath = $stagingRootPath.TrimEnd([IO.Path]::DirectorySeparatorChar)
New-Item -ItemType Directory -Force -Path $stagingRootPath | Out-Null
$stagingPath = Join-Path $stagingRootPath ('FolderNotifier-installer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingPath | Out-Null
# Compile on local temporary storage before copying the finished file to a
# synchronized workspace. In-place compiler seek/write on cloud files can
# otherwise leave zero-filled holes in the compressed setup metadata.
& $CompilerPath /Q "/DAppVersion=$AppVersion" "/DPublishDir=$publishPath" "/DOutputDir=$stagingPath" $installerSource
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed. Staging files: $stagingPath" }
$stagedInstallerPath = Join-Path $stagingPath 'FolderNotifier-Setup.exe'
$stagedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $stagedInstallerPath).Hash
$installerPath = Join-Path $distPath 'FolderNotifier-Setup.exe'
Copy-Item -LiteralPath $stagedInstallerPath -Destination $installerPath -Force
$installerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath).Hash.ToLowerInvariant()
if ($installerHash -ne $stagedHash) { throw "Installer copy verification failed. Original: $stagedInstallerPath" }
Set-Content -LiteralPath (Join-Path $distPath 'FolderNotifier-Setup.sha256') -Value "$installerHash  FolderNotifier-Setup.exe" -Encoding ascii
$resolvedStagingPath = (Resolve-Path -LiteralPath $stagingPath).ProviderPath
if (-not $resolvedStagingPath.Equals($stagingPath, [StringComparison]::OrdinalIgnoreCase) -or
    -not $resolvedStagingPath.StartsWith($stagingRootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected staging cleanup path: $resolvedStagingPath"
}
Remove-Item -LiteralPath $stagedInstallerPath
if (@(Get-ChildItem -LiteralPath $resolvedStagingPath -Force).Count -eq 0) {
    Remove-Item -LiteralPath $resolvedStagingPath
}
Write-Host "Installer: $installerPath"
Write-Host "Size: $([math]::Round((Get-Item -LiteralPath $installerPath).Length / 1MB, 1)) MB"
Write-Host "SHA-256: $installerHash"
