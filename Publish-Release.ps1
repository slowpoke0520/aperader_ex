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
$settingsFile = Join-Path $projectDir 'Properties\Settings.settings'
$designerFile = Join-Path $projectDir 'Properties\Settings.Designer.cs'
$appConfigFile = Join-Path $projectDir 'App.config'
$publishDir = Join-Path $projectDir 'bin\Release\net6.0-windows\publish\win-x64'
$artifactsDir = Join-Path $repoRoot 'artifacts'
$packageRoot = Join-Path $artifactsDir 'package\ApeRadar'
$archivePath = Join-Path $artifactsDir 'ApeRadar-win-x64.zip'
$assemblyVersion = $Version.Split('-')[0] + '.0'

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

dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained false -p:PublishProfile=FolderProfile
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

if (Test-Path -LiteralPath $artifactsDir) {
    Remove-Item -LiteralPath $artifactsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $packageRoot -Recurse -Force
Compress-Archive -Path (Join-Path $artifactsDir 'package\ApeRadar') -DestinationPath $archivePath -CompressionLevel Optimal

Write-Host "Release package created: $archivePath"
Write-Host "Create and push tag v$Version to publish it automatically."
