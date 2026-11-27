[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [Parameter(Mandatory)] [string] $Version,
    [string] $ReleasesDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require-File([string] $Directory, [string] $Name) {
    $path = Join-Path $Directory $Name
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Missing or empty release file: $path"
    }
}

function Invoke-SqlTool([string] $Name, [string[]] $Arguments, [string] $InputText) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PublishDirectory "$Name.exe"))
    $start.WorkingDirectory = [IO.Path]::GetFullPath($PublishDirectory)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($InputText)
        $process.StandardInput.Close()
        if (!$process.WaitForExit(30000)) {
            $process.Kill($true)
            throw "$Name timed out."
        }
        $output = $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "$Name failed with exit code $($process.ExitCode): $errorText" }
        if ([string]::IsNullOrWhiteSpace($output)) { throw "$Name returned no output." }
        return $output
    } finally {
        $process.Dispose()
    }
}

foreach ($name in @(
    'Zea.Tweakr.exe', 'Zea.Tweakr.dll', 'Zea.Tweakr.runtimeconfig.json', 'Zea.Tweakr.deps.json',
    'Zea.SqlFormat.exe', 'Zea.SqlFormat.dll', 'Zea.SqlFormat.runtimeconfig.json', 'Zea.SqlFormat.deps.json',
    'Zea.SqlLogConvert.exe', 'Zea.SqlLogConvert.dll', 'Zea.SqlLogConvert.runtimeconfig.json', 'Zea.SqlLogConvert.deps.json',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Velopack.dll', 'Material3.Avalonia.dll', 'LICENSE', 'THIRD-PARTY-NOTICES.md',
    'licenses/Antlr.txt', 'licenses/DBeaver.txt', 'licenses/RightWay.txt', 'licenses/ScriptDom.txt', 'licenses/SQLFluff.txt', 'licenses/Velopack.txt',
    'licenses/Material3.Avalonia.txt', 'licenses/Material3.Avalonia.NOTICE.txt'
)) {
    Require-File $PublishDirectory $name
}

foreach ($name in @('Zea.Tweakr', 'Zea.SqlFormat', 'Zea.SqlLogConvert')) {
    $runtime = Get-Content -LiteralPath (Join-Path $PublishDirectory "$name.runtimeconfig.json") -Raw | ConvertFrom-Json
    if (!$runtime.runtimeOptions.PSObject.Properties['includedFrameworks']) { throw "$name is not self-contained." }
    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishDirectory "$name.dll")).ProductVersion.Split('+')[0]
    if ($actualVersion -ne $Version) { throw "$name version $actualVersion does not match $Version." }
}

$formatted = Invoke-SqlTool 'Zea.SqlFormat' @('--dialect', 'tsql') 'select 1 as value'
if ($formatted -notmatch '(?is)SELECT\s+1\s+AS\s+value') { throw 'Published formatter did not format the sample SQL.' }
$converted = Invoke-SqlTool 'Zea.SqlLogConvert' @() "PreparedStatement: [select @param0 as value]`nParameters: [@param0=[VALUE,7]]`nTypes: [@param0=[Int32, System.Int32]]"
if ($converted -notmatch '(?is)select\s+7\s+as\s+value') { throw 'Published log converter did not substitute the sample parameter.' }

if ($ReleasesDirectory) {
    Require-File $ReleasesDirectory 'releases.stable.json'
    $feed = Get-Content -LiteralPath (Join-Path $ReleasesDirectory 'releases.stable.json') -Raw | ConvertFrom-Json
    $assets = @($feed.Assets | Where-Object { $_.Type -eq 'Full' -and $_.Version -eq $Version })
    if ($assets.Count -ne 1) { throw "Expected one full package for $Version in the stable feed." }
    foreach ($asset in $feed.Assets) {
        if ($asset.PackageId -ne 'Zea.Tweakr.App') { throw 'The update feed contains an unexpected package ID.' }
        if ([IO.Path]::GetFileName($asset.FileName) -ne $asset.FileName) { throw 'Feed contains a non-local asset name.' }
        Require-File $ReleasesDirectory $asset.FileName
        $package = Join-Path $ReleasesDirectory $asset.FileName
        if ((Get-Item -LiteralPath $package).Length -ne $asset.Size) { throw 'Package size does not match the update feed.' }
        if ((Get-FileHash -LiteralPath $package -Algorithm SHA1).Hash -ne $asset.SHA1) { throw 'Package checksum does not match the update feed.' }
        if ($asset.PSObject.Properties['SHA256'] -and $asset.SHA256 -and (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $asset.SHA256) {
            throw 'Package SHA256 checksum does not match the update feed.'
        }
    }
    $portableArchives = @(Get-ChildItem -LiteralPath $ReleasesDirectory -Filter '*-Portable.zip' -File)
    if ($portableArchives.Count -ne 1) { throw 'Expected one portable ZIP.' }
    if (@(Get-ChildItem -LiteralPath $ReleasesDirectory -Filter '*Setup*' -File).Count -ne 0) { throw 'Portable release must not contain an installer.' }
    $zip = [IO.Compression.ZipFile]::OpenRead($portableArchives[0].FullName)
    try {
        $entries = @($zip.Entries.FullName)
        foreach ($required in @(
            'Zea.Tweakr.exe', 'Update.exe', '.portable', 'current/Zea.Tweakr.exe',
            'current/Zea.SqlFormat.exe', 'current/Zea.SqlFormat.runtimeconfig.json',
            'current/Zea.SqlLogConvert.exe', 'current/Zea.SqlLogConvert.runtimeconfig.json',
            'current/THIRD-PARTY-NOTICES.md', 'current/LICENSE',
            'current/licenses/Antlr.txt', 'current/licenses/DBeaver.txt', 'current/licenses/RightWay.txt',
            'current/licenses/ScriptDom.txt', 'current/licenses/SQLFluff.txt', 'current/licenses/Velopack.txt',
            'current/Material3.Avalonia.dll', 'current/licenses/Material3.Avalonia.txt', 'current/licenses/Material3.Avalonia.NOTICE.txt'
        )) {
            if ($entries -notcontains $required) { throw "Portable ZIP is missing $required." }
        }
    } finally {
        $zip.Dispose()
    }
}
Write-Host 'Published executables, runtime files, versions and licenses passed verification.'
