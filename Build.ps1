param(
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$CecilPath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$managed = Join-Path (Resolve-Path -LiteralPath $GameRoot) 'Probably Stolen_Data/Managed'
$cecil = (Resolve-Path -LiteralPath $CecilPath).Path
$sdkVersion = & dotnet --version
$sdkLine = & dotnet --list-sdks | Where-Object { $_.StartsWith("$sdkVersion [") } | Select-Object -First 1
if ($sdkLine -notmatch '^([^ ]+) \[(.+)\]$') { throw 'A .NET SDK is required.' }
$sdk = Join-Path $Matches[2] $Matches[1]
$dotnetRoot = Split-Path -Parent $Matches[2]
$sdkTools = Join-Path $sdk 'Sdks/Microsoft.NET.Sdk/tools/net472'
$project = Join-Path $PSScriptRoot 'ModDevelopment/ResourceStoryMod'
$manifest = Join-Path $PSScriptRoot 'Mods/Millsage/manifest.xml'
$mod = ([xml](Get-Content -LiteralPath $manifest -Raw)).Mod
$build = Join-Path $PSScriptRoot ('build/' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $build 'payload'
$dist = Join-Path $PSScriptRoot 'dist'
$modDir = Join-Path $payload 'Mods/Millsage'
$references = Join-Path $build 'references'
foreach ($folder in $modDir, $references, $dist) { $null = New-Item -ItemType Directory -Force -Path $folder }

$null = [Reflection.Assembly]::LoadFrom($cecil)
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
function Expose-Type($type) {
    if ($type.IsNested) { $type.IsNestedPublic = $true } else { $type.IsPublic = $true }
    foreach ($field in $type.Fields) { $field.IsPublic = $true; $field.IsInitOnly = $false }
    foreach ($method in $type.Methods) { $method.IsPublic = $true }
    foreach ($nested in $type.NestedTypes) { Expose-Type $nested }
}
foreach ($name in 'Assembly-CSharp.dll', 'Unity.Localization.dll') {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed $name), $reader)
    try {
        foreach ($type in $assembly.MainModule.Types) { Expose-Type $type }
        $assembly.Write((Join-Path $references $name))
    } finally { $assembly.Dispose() }
}

$dll = Join-Path $modDir 'ResourceStoryMod.dll'
$deps = @($mod.Assemblies.Assembly | Where-Object { $_ -ne 'ResourceStoryMod.dll' } | ForEach-Object {
    Copy-Item -LiteralPath (Join-Path $sdkTools $_) -Destination $modDir -Force -PassThru
})
$compileRefs = @(Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File | Where-Object Name -notin @('Assembly-CSharp.dll', 'Unity.Localization.dll')) +
    @(Get-ChildItem -LiteralPath $references -Filter '*.dll' -File) + $deps
$args = @('-nologo', '-nostdlib+', '-target:library', '-langversion:latest', '-optimize+', '-deterministic+', ('-out:"' + $dll + '"'))
$args += $compileRefs | ForEach-Object { '-r:"' + $_.FullName + '"' }
$args += Get-ChildItem -LiteralPath $project -Filter '*.cs' -File | ForEach-Object { '"' + $_.FullName + '"' }
$rsp = Join-Path $build 'compile.rsp'
$args | Set-Content -LiteralPath $rsp -Encoding utf8
& dotnet (Join-Path $sdk 'Roslyn/bincore/csc.dll') -noconfig ('@' + $rsp)
if (-not ('MonoAccessWeaver' -as [type])) {
    $refs = @($cecil) + @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' -File | ForEach-Object FullName)
    Add-Type -Path (Join-Path $PSScriptRoot 'tools/MonoAccessWeaver.cs') -ReferencedAssemblies $refs
}
[MonoAccessWeaver]::Run($dll, $managed, $modDir)

$runtime = @(
    'README.md',
    'Mods/Millsage/manifest.xml',
    'ModDevelopment/ResourceStoryMod/Assets',
    'ModDevelopment/ResourceStoryMod/Content',
    'ModWorkbench/res/prepared'
)
foreach ($relative in $runtime) {
    $target = Join-Path $payload $relative
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $relative) -PathType Container) {
        $null = New-Item -ItemType Directory -Force -Path $target
        Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $relative) | Copy-Item -Destination $target -Recurse -Force
    } else {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $relative) -Destination $target -Force
    }
}
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination (Join-Path $modDir 'DOTNET-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $modDir 'DOTNET-THIRD-PARTY-NOTICES.txt') -Force
$zip = Join-Path $dist ('PS_Millsage_Mod_v' + $mod.ModVersion + '_Windows.zip')
$tempZip = Join-Path $build 'release.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $tempZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
[IO.File]::Move($tempZip, $zip, $true)
$hash = (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Output $zip
