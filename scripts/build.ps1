param(
    [ValidateSet('x64','ARM64')][string]$Architecture = 'x64',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version = '0.1.0.0',
    [string]$OutputDirectory = '',
    [switch]$Store,
    [string]$SigningCertificate = '',
    [string]$Publisher = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts/$Architecture" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'MSBuild with the Windows App SDK build tools is required.' }
$properties = @('/restore','/nologo','/verbosity:minimal','/t:Build;PrepareMsixPackage',"/p:Configuration=Release","/p:Platform=$Architecture",'/p:GenerateAppxPackageOnBuild=false',"/p:AppxPackageVersion=$Version",("/p:AppxPackageDir=" + $OutputDirectory + '\'),'/p:AppxBundle=Never')
$properties += '/p:AppxSymbolPackageEnabled=false'
if ($Publisher) { $properties += "/p:AppxPackagePublisher=$Publisher" }
if ($Store) {
    if (!$Publisher) { throw 'Pass the exact Publisher identity from Partner Center for Store packages.' }
    $properties += '/p:UapAppxPackageBuildMode=StoreUpload','/p:AppxPackageSigningEnabled=false'
} elseif ($SigningCertificate) {
    $properties += '/p:UapAppxPackageBuildMode=SideloadOnly', '/p:AppxPackageSigningEnabled=false'
} else {
    $properties += '/p:UapAppxPackageBuildMode=SideloadOnly','/p:AppxPackageSigningEnabled=false'
}
& $msbuild (Join-Path $root 'src/LinksForAtlassian/LinksForAtlassian.csproj') @properties
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed: $LASTEXITCODE" }
$runtime = if ($Architecture -eq 'ARM64') { 'arm64' } else { 'x64' }
$recipePath = Join-Path $root "src/LinksForAtlassian/bin/$Architecture/Release/net10.0-windows10.0.26100.0/win-$runtime/LinksForAtlassian.build.appxrecipe"
$recipe = [xml](Get-Content -LiteralPath $recipePath)
$manifest = [xml](Get-Content -LiteralPath $recipe.SelectSingleNode("//*[local-name()='AppXManifest']").Include)
$manifest.Package.Identity.Version = $Version
if ($Publisher) { $manifest.Package.Identity.Publisher = $Publisher }
$manifestPath = Join-Path $OutputDirectory 'AppxManifest.xml'
$manifest.Save($manifestPath)
$mapping = @('[Files]')
foreach ($file in $recipe.SelectNodes("//*[local-name()='AppXManifest' or local-name()='AppxPackagedFile']")) {
    $source = if ($file.LocalName -eq 'AppXManifest') { $manifestPath } else { $file.Include }
    if ($file.PackagePath -notlike '*.pdb') { $mapping += '"' + $source + '" "' + $file.PackagePath + '"' }
}
$mapPath = Join-Path $OutputDirectory 'package-map.txt'
$mapping | Set-Content -LiteralPath $mapPath -Encoding utf8
$makeappx = Get-ChildItem 'C:/Program Files (x86)/Windows Kits/10/bin/*/x64/makeappx.exe' | Sort-Object FullName -Descending | Select-Object -First 1
if (!$makeappx) { throw 'Windows SDK makeappx.exe was not found.' }
# Use the SDK-generated payload with MakeAppx; the MSIX NuGet target unconditionally probes absent C++ symbol tools.
$packagePath = Join-Path $OutputDirectory "LinksForAtlassian_${Version}_$Architecture.msix"
& $makeappx.FullName pack /o /f $mapPath /p $packagePath | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx packaging failed.' }
$zip = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $reader = [IO.StreamReader]::new($zip.GetEntry('AppxManifest.xml').Open())
    try { $packed = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($packed.Package.Identity.Version -ne $Version -or $packed.Package.Identity.Publisher -ne $manifest.Package.Identity.Publisher) { throw 'Packed identity does not match requested version/publisher.' }
} finally { $zip.Dispose() }
$packages = @(Get-Item -LiteralPath $packagePath)
if (!$packages.Count) { throw 'No MSIX package was produced.' }
if ($SigningCertificate) {
    $signtool = Get-ChildItem 'C:/Program Files (x86)/Windows Kits/10/bin/*/x64/signtool.exe' | Sort-Object FullName -Descending | Select-Object -First 1
    if (!$signtool) { throw 'signtool.exe was not found.' }
    foreach ($package in $packages) {
        & $signtool.FullName sign /fd SHA256 /sha1 $SigningCertificate /tr 'http://timestamp.digicert.com' /td SHA256 $package.FullName
        if ($LASTEXITCODE -ne 0) { throw 'Package signing failed.' }
        & $signtool.FullName verify /pa $package.FullName
        if ($LASTEXITCODE -ne 0) { throw 'Package signature verification failed.' }
    }
}
$packages | ForEach-Object { $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $($_.Name)" } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8
Write-Output "Packages: $($packages.Count), output: $OutputDirectory"
