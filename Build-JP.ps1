param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$taskOutput = Join-Path $PSScriptRoot 'publish'
Push-Location $PSScriptRoot
try {
    $pathMap = $PSScriptRoot + '=/_/LyricBarJP'
    dotnet publish LyricBar.csproj -c Release -r win-x64 --self-contained $SelfContained.IsPresent -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:UseSharedCompilation=false "-p:PathMap=$pathMap" -o $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Write-Host "Built: $taskOutput\LyricBarJP.exe"
} finally { Pop-Location }
