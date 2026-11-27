[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Version,
    [string] $OutputDirectory,
    [string] $VpkPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/$Version"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$parsedVersion = [version]::Parse($Version)
if ($parsedVersion.Major -gt 65534 -or $parsedVersion.Minor -gt 65534 -or $parsedVersion.Build -gt 65534) {
    throw 'Each version component must be between 0 and 65534 for the .NET assembly version.'
}
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Use a new output directory to avoid mixing release versions: $OutputDirectory"
}

function Invoke-Dotnet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}

function Invoke-Vpk([string[]] $Arguments) {
    if ($VpkPath) {
        & $VpkPath @Arguments
    } else {
        & dotnet tool run vpk -- @Arguments
    }
    if ($LASTEXITCODE -ne 0) { throw "vpk failed with exit code $LASTEXITCODE." }
}

Push-Location $repositoryRoot
try {
    if (!$VpkPath) {
        Invoke-Dotnet @('tool', 'restore')
    }
    $vpkHelp = if ($VpkPath) { & $VpkPath --help } else { & dotnet tool run vpk -- --help }
    if ($LASTEXITCODE -ne 0 -or ($vpkHelp -join "`n") -notmatch 'Velopack CLI 1\.2\.161\b') {
        throw 'The release requires Velopack CLI 1.2.161 to match the SDK.'
    }

    $publishDirectory = Join-Path $OutputDirectory 'publish'
    $releasesDirectory = Join-Path $OutputDirectory 'Releases'
    foreach ($project in @('Zea.Tweakr', 'Zea.SqlFormat', 'Zea.SqlLogConvert')) {
        Invoke-Dotnet @(
            'publish', "src/$project/$project.csproj", '--configuration', 'Release',
            '--runtime', 'win-x64', '--self-contained', 'true', '--output', $publishDirectory,
            "-p:Version=$Version", '-p:PublishSelfContained=true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false'
        )
    }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE')
    $notices = @'
# Giấy phép và mã nguồn

Zea.SqlFormat sử dụng GNU AGPL v3.0. Toàn văn nằm trong [LICENSE](LICENSE).
Mã nguồn tương ứng với bản phát hành này nằm tại [tag v{{VERSION}}](https://github.com/nguyentovan/Zea.Tools/tree/v{{VERSION}}).

Các thông báo của thành phần bên thứ ba được giữ trong thư mục licenses.

| Thành phần | Giấy phép |
| --- | --- |
| Right Way SQL Formatter | [AGPL v3](licenses/RightWay.txt) |
| DBeaver | [Apache 2.0](licenses/DBeaver.txt) |
| ANTLR | [BSD 3-Clause](licenses/Antlr.txt) |
| SQLFluff | [MIT](licenses/SQLFluff.txt) |
| Microsoft ScriptDom | [MIT](licenses/ScriptDom.txt) |
| Velopack | [MIT](licenses/Velopack.txt) |
| Material3.Avalonia | [Apache 2.0](licenses/Material3.Avalonia.txt), [NOTICE](licenses/Material3.Avalonia.NOTICE.txt) |

Các package NuGet khác giữ giấy phép của upstream. Danh sách dependency và hướng dẫn build nằm trong mã nguồn của tag trên.
'@
    Set-Content -LiteralPath (Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.md') -Value $notices.Replace('{{VERSION}}', $Version) -Encoding utf8
    & (Join-Path $PSScriptRoot 'Test-Release.ps1') -PublishDirectory $publishDirectory -Version $Version

    Invoke-Vpk @(
        '--skip-updates', 'pack', '--packId', 'Zea.Tweakr.App', '--packVersion', $Version,
        '--packDir', $publishDirectory, '--mainExe', 'Zea.Tweakr.exe', '--packTitle', 'Zea.Tweakr',
        '--packAuthors', 'nguyentovan', '--runtime', 'win-x64', '--channel', 'stable',
        '--icon', 'src/Zea.Tweakr/Assets/Zea.Tweakr.ico', '--delta', 'None', '--noInst',
        '--outputDir', $releasesDirectory
    )
    & (Join-Path $PSScriptRoot 'Test-Release.ps1') -PublishDirectory $publishDirectory -ReleasesDirectory $releasesDirectory -Version $Version
    Write-Host "Portable release validated: $releasesDirectory"
} finally {
    Pop-Location
}
