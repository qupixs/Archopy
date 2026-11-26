param(
    [string]$Dotnet = 'dotnet',
    [string]$Compiler,
    [string]$SignToolCommand,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Archopy\Archopy.csproj'
$publish = Join-Path $root 'artifacts\publish\Archopy-win-x64'
$installer = Join-Path $root 'installer\Archopy.iss'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version

if (-not $Compiler) {
    $available = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($available) {
        $Compiler = $available.Source
    } else {
        $Compiler = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
            (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $Compiler) { throw 'Install Inno Setup or pass -Compiler with the path to ISCC.exe.' }

if (-not $SkipPublish) {
    & $Dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publishing Archopy failed.' }
}

$options = @("/DAppVersion=$version", "/DPublishDir=$publish")
if ($SignToolCommand) {
    $options += '/DSignInstaller=1'
    $options += "/Sarchopy=$SignToolCommand"
    foreach ($binary in (Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.Extension -in '.exe', '.dll' })) {
        if ((Get-AuthenticodeSignature -LiteralPath $binary.FullName).Status -ne 'Valid') {
            throw "Sign the release payload before packaging. Missing valid signature on $($binary.Name). See docs/release.md."
        }
    }
}
& $Compiler @options $installer
if ($LASTEXITCODE -ne 0) { throw 'Building the Archopy installer failed.' }
