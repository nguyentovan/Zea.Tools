[CmdletBinding()]
param(
    [string] $AppSourceDirectory = (Join-Path $PSScriptRoot '../../src/Zea.Tweakr'),
    [string] $VpkPath,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/update-smoke'),
    [string] $PackagesDirectory = (Join-Path $PSScriptRoot '../../artifacts/update-smoke-packages'),
    [ValidateRange(30, 600)]
    [int] $TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-LoggedCommand {
    param([string] $Executable, [string[]] $Arguments, [string] $LogPath)
    & $Executable @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE. See $LogPath."
    }
}

function Copy-Feed {
    param([string] $Source, [string] $Destination)
    $null = New-Item -ItemType Directory -Path $Destination
    Get-ChildItem -LiteralPath $Source -File |
        Where-Object { $_.Extension -in '.json', '.nupkg' -or $_.Name -like 'RELEASES*' } |
        Copy-Item -Destination $Destination
}

function Invoke-Vpk {
    param([string[]] $Arguments, [string] $LogPath)
    if ($VpkPath) {
        Invoke-LoggedCommand $VpkPath $Arguments $LogPath
    } else {
        Invoke-LoggedCommand 'dotnet' (@('tool', 'run', 'vpk', '--', '--skip-updates') + $Arguments) $LogPath
    }
}

$updaterSource = Join-Path ([IO.Path]::GetFullPath($AppSourceDirectory)) 'AppUpdater.cs'
if (-not (Test-Path -LiteralPath $updaterSource -PathType Leaf)) {
    throw "Production updater source not found at $updaterSource."
}
if ($VpkPath) {
    $VpkPath = (Resolve-Path -LiteralPath $VpkPath).Path
}
$projectPath = Join-Path $PSScriptRoot 'UpdateSmoke/UpdateSmoke.csproj'
$nugetConfig = Join-Path $PSScriptRoot 'NuGet.Config'
$runName = 'run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $runName
$logsDirectory = Join-Path $runDirectory 'logs'
$releaseDirectory = Join-Path $runDirectory 'releases'
$portableDirectory = Join-Path $runDirectory 'portable'
$settingsDirectory = Join-Path $runDirectory 'settings'
$PackagesDirectory = [IO.Path]::GetFullPath($PackagesDirectory)
foreach ($directory in @($runDirectory, $logsDirectory, $releaseDirectory, $settingsDirectory, $PackagesDirectory)) {
    $null = New-Item -ItemType Directory -Path $directory -Force
}
$updaterSnapshot = Join-Path $runDirectory 'ProductionAppUpdater.cs'
Copy-Item -LiteralPath $updaterSource -Destination $updaterSnapshot
$vpkHelp = if ($VpkPath) { & $VpkPath --help 2>&1 | Out-String } else { & dotnet tool run vpk -- --help 2>&1 | Out-String }
if ($LASTEXITCODE -ne 0 -or $vpkHelp -notmatch 'Velopack CLI 1\.2\.161[,\s]') {
    throw 'The verification script requires Velopack CLI 1.2.161 to match the probe SDK.'
}
$sourceEvidence = [ordered]@{
    AppUpdaterSource = $updaterSource
    AppUpdaterSha256 = (Get-FileHash -LiteralPath $updaterSnapshot -Algorithm SHA256).Hash
    VpkPath = $VpkPath
    VpkVersion = '1.2.161'
    CreatedUtc = [DateTime]::UtcNow.ToString('O')
}
[IO.File]::WriteAllText((Join-Path $runDirectory 'source-evidence.json'), ($sourceEvidence | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

$oldWorkloadResolver = $env:MSBuildEnableWorkloadResolver
$oldSmokeConfig = $env:ZEA_TWEAKR_UPDATE_SMOKE_CONFIG
try {
    $env:MSBuildEnableWorkloadResolver = 'false'
    Invoke-LoggedCommand 'dotnet' @(
        'restore', $projectPath,
        '--configfile', $nugetConfig,
        '--runtime', 'win-x64',
        '--packages', $PackagesDirectory,
        '-p:SelfContained=true',
        '-p:NuGetAudit=false'
    ) (Join-Path $logsDirectory 'restore.log')

    foreach ($version in @('0.0.1', '0.0.2')) {
        $publishDirectory = Join-Path $runDirectory ('publish-' + $version)
        Invoke-LoggedCommand 'dotnet' @(
            'publish', $projectPath,
            '--no-restore', '--configuration', 'Release',
            '--runtime', 'win-x64', '--self-contained', 'true',
            '--output', $publishDirectory,
            ('-p:Version=' + $version),
            ('-p:AppUpdaterSource=' + $updaterSnapshot),
            '-p:UseSharedCompilation=false'
        ) (Join-Path $logsDirectory ('publish-' + $version + '.log'))

        Invoke-Vpk @(
            'pack', '--packId', 'Zea.Tweakr.App',
            '--packVersion', $version,
            '--packDir', $publishDirectory,
            '--mainExe', 'Zea.Tweakr.UpdateSmoke.exe',
            '--outputDir', $releaseDirectory,
            '--channel', 'stable', '--runtime', 'win-x64',
            '--noInst', '--shortcuts', 'None', '--delta', 'None',
            '--yes', '--legacyConsole', '--skip-updates'
        ) (Join-Path $logsDirectory ('pack-' + $version + '.log'))

        if ($version -eq '0.0.1') {
            Copy-Feed $releaseDirectory (Join-Path $runDirectory 'feed-current')
            $portableZip = @(Get-ChildItem -LiteralPath $releaseDirectory -File -Filter '*Portable.zip')
            if ($portableZip.Count -ne 1) {
                throw 'Expected exactly one portable ZIP after packaging version 0.0.1.'
            }
            Copy-Item -LiteralPath $portableZip[0].FullName -Destination (Join-Path $runDirectory 'portable-0.0.1.zip')
        }
    }

    if (@(Get-ChildItem -LiteralPath $releaseDirectory -File -Filter '*Setup*.exe').Count -ne 0) {
        throw 'Portable verification must not produce an installer.'
    }
    Copy-Feed $releaseDirectory (Join-Path $runDirectory 'feed-next')
    Copy-Feed $releaseDirectory (Join-Path $runDirectory 'feed-corrupt')
    $corruptPackages = @(Get-ChildItem -LiteralPath (Join-Path $runDirectory 'feed-corrupt') -File -Filter '*0.0.2*full.nupkg')
    if ($corruptPackages.Count -ne 1) {
        throw 'Expected exactly one full version 0.0.2 package to corrupt.'
    }
    $corruptStream = [IO.File]::Open($corruptPackages[0].FullName, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $position = [long][Math]::Floor($corruptStream.Length / 2)
        $corruptStream.Position = $position
        $originalByte = $corruptStream.ReadByte()
        $corruptStream.Position = $position
        $corruptStream.WriteByte([byte]($originalByte -bxor 255))
    }
    finally {
        $corruptStream.Dispose()
    }
    Expand-Archive -LiteralPath (Join-Path $runDirectory 'portable-0.0.1.zip') -DestinationPath $portableDirectory

    $sentinelPath = Join-Path $settingsDirectory 'FeatureSettings.json'
    $sentinel = '{"StartOnLogon":false,"SqlFormat":true,"Sentinel":"preserve-exact-bytes-123"}'
    [IO.File]::WriteAllText($sentinelPath, $sentinel, [Text.UTF8Encoding]::new($false))
    $settingsHash = (Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash
    $resultPath = Join-Path $runDirectory 'result.json'
    $configPath = Join-Path $runDirectory 'smoke-config.json'
    $configuration = [ordered]@{
        PortableRoot = $portableDirectory
        FeedCurrent = Join-Path $runDirectory 'feed-current'
        FeedNext = Join-Path $runDirectory 'feed-next'
        FeedCorrupt = Join-Path $runDirectory 'feed-corrupt'
        UnavailableUrl = 'http://127.0.0.1:1/'
        SentinelPath = $sentinelPath
        SentinelSha256 = $settingsHash
        RestartMarkerPath = Join-Path $runDirectory 'restart-requested.txt'
        EventPath = Join-Path $runDirectory 'events.jsonl'
        ResultPath = $resultPath
    }
    [IO.File]::WriteAllText($configPath, ($configuration | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $env:ZEA_TWEAKR_UPDATE_SMOKE_CONFIG = $configPath
    $entryPoint = Join-Path $portableDirectory 'Zea.Tweakr.UpdateSmoke.exe'
    if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
        $entryPoint = Join-Path $portableDirectory 'current/Zea.Tweakr.UpdateSmoke.exe'
    }
    $launcher = Start-Process -FilePath $entryPoint -WorkingDirectory $portableDirectory -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        $launcher.Refresh()
        if ($launcher.HasExited -and $launcher.ExitCode -ne 0) {
            throw "The probe exited with code $($launcher.ExitCode). See $runDirectory."
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "The updated probe did not report a result within $TimeoutSeconds seconds. See $runDirectory."
        }
        Start-Sleep -Milliseconds 250
    }

    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (-not $result.passed) {
        throw "Portable update verification failed. $($result.error) See $runDirectory."
    }
    if ((Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash -ne $settingsHash) {
        throw 'The settings sentinel changed after the application restarted.'
    }
    if ($result.firstProcessId -eq $result.restartedProcessId -or $result.currentVersion -ne '0.0.2' -or $result.assemblyVersion -ne '0.0.2') {
        throw 'The result does not prove a new process running the new executable and package versions.'
    }
    if ((Get-FileHash -LiteralPath $updaterSource -Algorithm SHA256).Hash -ne $sourceEvidence.AppUpdaterSha256) {
        throw 'Production AppUpdater.cs changed during verification. Rerun to verify the current source.'
    }
    Write-Output "PASS Portable update 0.0.1 to 0.0.2. Settings SHA256 $settingsHash."
    Write-Output "Evidence $runDirectory"
    Get-Content -LiteralPath $resultPath -Raw
}
finally {
    $env:MSBuildEnableWorkloadResolver = $oldWorkloadResolver
    $env:ZEA_TWEAKR_UPDATE_SMOKE_CONFIG = $oldSmokeConfig
}
