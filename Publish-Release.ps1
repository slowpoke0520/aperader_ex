param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+-ex\.\d+$')]
    [string]$Version,

    [string]$ReleaseDate = (Get-Date -Format 'yyyyMMdd')
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$projectDir = Join-Path $repoRoot 'ApeRadar_EX\ApeRadar_Src\ApeRadar'
$projectFile = Join-Path $projectDir 'ApeRadar.csproj'
$solutionFile = Join-Path $repoRoot 'ApeRadar_EX\ApeRadar_Src\ApeRadar.sln'
$updaterProject = Join-Path $repoRoot 'ApeRadar_EX\ApeRadar_Src\ApeRadar.Updater\ApeRadar.Updater.csproj'
$settingsFile = Join-Path $projectDir 'Properties\Settings.settings'
$designerFile = Join-Path $projectDir 'Properties\Settings.Designer.cs'
$appConfigFile = Join-Path $projectDir 'App.config'
$publishDir = Join-Path $projectDir 'bin\Release\net8.0-windows\win-x64\publish'
$updaterPublishDir = Join-Path (Split-Path $updaterProject) 'bin\Release\net8.0-windows\win-x64\publish'
$artifactsDir = Join-Path $repoRoot 'artifacts'
$packageStagingDir = Join-Path $artifactsDir "package-$([Guid]::NewGuid().ToString('N'))"
$packageRoot = Join-Path $packageStagingDir 'ApeRadar'
$archivePath = Join-Path $artifactsDir 'ApeRadar-win-x64.zip'
$assemblyVersion = $Version.Split('-')[0] + '.0'
$changeLogFile = Join-Path $repoRoot 'CHANGELOG.md'
$releaseDataScript = Join-Path $repoRoot 'Update-ReleaseData.ps1'

if (-not (Test-Path -LiteralPath $changeLogFile)) {
    throw 'CHANGELOG.md is required before publishing'
}
$changeLog = [IO.File]::ReadAllText($changeLogFile)
$escapedVersion = [regex]::Escape($Version)
if ($changeLog -notmatch "(?m)^## \[$escapedVersion\]") {
    throw "CHANGELOG.md does not contain a release section for $Version"
}

if (-not (Test-Path -LiteralPath $releaseDataScript)) {
    throw 'Update-ReleaseData.ps1 is required before publishing'
}
Write-Host 'Refreshing and validating release data...'
& $releaseDataScript -ProjectJsonDirectory (Join-Path $projectDir 'Resources\Json')

function Update-TextFile([string]$Path, [scriptblock]$Transform) {
    $content = [IO.File]::ReadAllText($Path)
    $updated = & $Transform $content
    [IO.File]::WriteAllText($Path, $updated, [Text.UTF8Encoding]::new($false))
}

Update-TextFile $projectFile {
    param($text)
    $text = [regex]::Replace($text, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
    [regex]::Replace($text, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$assemblyVersion</AssemblyVersion>")
}
Update-TextFile $settingsFile {
    param($text)
    $text = [regex]::Replace($text, '(<Setting Name="SoftwareVersion"[\s\S]*?<Value Profile="\(Default\)">)[^<]+', { param($m) $m.Groups[1].Value + $Version }, 1)
    [regex]::Replace($text, '(<Setting Name="SoftwareDate"[\s\S]*?<Value Profile="\(Default\)">)[^<]+', { param($m) $m.Groups[1].Value + $ReleaseDate }, 1)
}
Update-TextFile $designerFile {
    param($text)
    $text = [regex]::Replace($text, '(DefaultSettingValueAttribute\(")[^"]+("\)\]\s*public string SoftwareVersion)', { param($m) $m.Groups[1].Value + $Version + $m.Groups[2].Value }, 1)
    [regex]::Replace($text, '(DefaultSettingValueAttribute\(")[^"]+("\)\]\s*public string SoftwareDate)', { param($m) $m.Groups[1].Value + $ReleaseDate + $m.Groups[2].Value }, 1)
}
Update-TextFile $appConfigFile {
    param($text)
    $text = [regex]::Replace($text, '(<setting name="SoftwareVersion"[\s\S]*?<value>)[^<]+', { param($m) $m.Groups[1].Value + $Version }, 1)
    [regex]::Replace($text, '(<setting name="SoftwareDate"[\s\S]*?<value>)[^<]+', { param($m) $m.Groups[1].Value + $ReleaseDate }, 1)
}

[xml]$settingsXml = [IO.File]::ReadAllText($settingsFile)
[xml]$appConfigXml = [IO.File]::ReadAllText($appConfigFile)
$settingsVersion = ($settingsXml.SettingsFile.Settings.Setting | Where-Object Name -eq 'SoftwareVersion').Value.InnerText
$configVersion = $appConfigXml.configuration.applicationSettings.'ApeRadar.Properties.Settings'.setting |
    Where-Object name -eq 'SoftwareVersion' |
    Select-Object -ExpandProperty value
if ($settingsVersion -ne $Version -or $configVersion -ne $Version) {
    throw 'Version synchronization failed'
}

dotnet test $solutionFile --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed' }

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if (Test-Path -LiteralPath $updaterPublishDir) {
    Remove-Item -LiteralPath $updaterPublishDir -Recurse -Force
}

dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

dotnet publish $updaterProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw 'updater publish failed' }
Copy-Item -LiteralPath (Join-Path $updaterPublishDir 'ApeRadar.Updater.exe') -Destination $publishDir -Force

foreach ($dataFileName in @('ships.json', 'expected_values.json', 'release_data.json')) {
    $sourceDataFile = Join-Path $projectDir "Resources\Json\$dataFileName"
    $publishedDataFile = Join-Path $publishDir "Resources\Json\$dataFileName"
    if (-not (Test-Path -LiteralPath $publishedDataFile) -or
        (Get-FileHash -LiteralPath $sourceDataFile -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $publishedDataFile -Algorithm SHA256).Hash) {
        throw "Published data verification failed: $dataFileName"
    }
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
$resolvedArtifactsDir = [IO.Path]::GetFullPath($artifactsDir).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedStagingDir = [IO.Path]::GetFullPath($packageStagingDir)
if (-not $resolvedStagingDir.StartsWith($resolvedArtifactsDir, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unsafe release package staging path'
}
try {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $publishDir '*') -Destination $packageRoot -Recurse -Force
    Compress-Archive -Path $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $resolvedStagingDir) {
        Remove-Item -LiteralPath $resolvedStagingDir -Recurse -Force
    }
}

Write-Host "Release package created: $archivePath"
Write-Host "Create and push tag v$Version to publish it automatically."
