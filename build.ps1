param([Parameter(Mandatory = $true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $managed = Join-Path $GameDir '7DaysToDieServer_Data\Managed'
    if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw 'GameDir must contain a v3.3 dedicated server installation.' }
    dotnet build src/ThreatAwareness/ThreatAwareness.csproj -c Release "-p:GameManagedDir=$managed" -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
    dotnet run --project tests/ThreatAwareness.Tests -c Release -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    # v3.3 loads mod folders alphabetically. Register chat before blanket slash guards.
    $destination = Join-Path $PSScriptRoot 'artifacts\0_ThreatAwareness'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -Path 'Mods\ThreatAwareness\*' -Destination $destination -Recurse -Force
    Copy-Item -LiteralPath 'src\ThreatAwareness\bin\Release\net48\ThreatAwareness.dll' -Destination $destination -Force
    Copy-Item -LiteralPath 'README.md' -Destination $destination -Force
    Copy-Item -LiteralPath 'LICENSE' -Destination $destination -Force
    Copy-Item -LiteralPath 'docs' -Destination $destination -Recurse -Force
    $version = ([xml](Get-Content -LiteralPath 'Mods\ThreatAwareness\ModInfo.xml' -Raw)).xml.Version.value
    Compress-Archive -LiteralPath $destination -DestinationPath "artifacts\ThreatAwareness-$version.zip" -Force
    Write-Output "Package: $destination"
} finally { Pop-Location }
