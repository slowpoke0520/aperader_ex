[CmdletBinding()]
param(
    [string]$ProjectJsonDirectory = (Join-Path $PSScriptRoot 'ApeRadar_EX\ApeRadar_Src\ApeRadar\Resources\Json'),
    [string]$GameDataRepository = 'wowsinfo/data',
    [string]$PrDataRepository = 'wowsinfo/WoWs-Info-Seven',
    [string]$PrDataRef = 'API',
    [string]$PrDataPath = 'json/personal_rating.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-GitHubHeaders {
    $headers = @{ 'User-Agent' = 'ApeRadar-ReleaseData' }
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)"
        $headers['X-GitHub-Api-Version'] = '2022-11-28'
    }
    return $headers
}

function Invoke-GitHubJson([string]$Uri) {
    Invoke-RestMethod -Headers (Get-GitHubHeaders) -Uri $Uri -TimeoutSec 60
}

function Save-RemoteFile([string]$Uri, [string]$Path) {
    Invoke-WebRequest -Headers (Get-GitHubHeaders) -Uri $Uri -OutFile $Path -TimeoutSec 120
    if (-not (Test-Path -LiteralPath $Path) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
        throw "Downloaded release data is empty: $Uri"
    }
}

function Get-FileSha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Read-JsonHashTable([string]$Path) {
    Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
}

function Get-LatestLiveGameDataTag {
    $tags = Invoke-GitHubJson "https://api.github.com/repos/$GameDataRepository/tags?per_page=100"
    $versions = foreach ($tag in $tags) {
        $tagName = [string]$tag.name
        $match = [regex]::Match($tagName, '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\.(?<revision>\d+)\.(?<build>\d+)$')
        if (-not $match.Success) { continue }
        [PSCustomObject]@{
            Name = $tagName
            CommitSha = [string]$tag.commit.sha
            CommitUrl = [string]$tag.commit.url
            Major = [int]$match.Groups['major'].Value
            Minor = [int]$match.Groups['minor'].Value
            Patch = [int]$match.Groups['patch'].Value
            Revision = [int]$match.Groups['revision'].Value
            Build = [long]$match.Groups['build'].Value
        }
    }
    $latest = $versions | Sort-Object Major, Minor, Patch, Revision, Build -Descending | Select-Object -First 1
    if ($null -eq $latest) {
        throw 'No stable live World of Warships data tag was found.'
    }
    return $latest
}

function Get-ExistingNumericVersion([string]$Path, [string]$PropertyName) {
    if (-not (Test-Path -LiteralPath $Path)) { return 0L }
    try {
        $json = Read-JsonHashTable $Path
        return [long]$json[$PropertyName]
    }
    catch {
        throw "Existing release data is invalid: $Path"
    }
}

$jsonDirectory = [IO.Path]::GetFullPath($ProjectJsonDirectory)
[IO.Directory]::CreateDirectory($jsonDirectory) | Out-Null
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$tempDirectory = [IO.Path]::GetFullPath((Join-Path $tempRoot "ApeRadar.ReleaseData.$([Guid]::NewGuid().ToString('N'))"))
if (-not $tempDirectory.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unable to create a safe temporary release-data directory.'
}
[IO.Directory]::CreateDirectory($tempDirectory) | Out-Null

try {
    $gameTag = Get-LatestLiveGameDataTag
    $gameCommit = Invoke-GitHubJson $gameTag.CommitUrl
    $gameCommitDate = [DateTimeOffset]$gameCommit.commit.committer.date
    $shipDate = $gameCommitDate.UtcDateTime.ToString('yyyyMMdd')
    $shipVersion = "$($gameTag.Major).$($gameTag.Minor)"

    $gameDataFile = Join-Path $tempDirectory 'wowsinfo.json'
    $languageFile = Join-Path $tempDirectory 'lang.json'
    $tagSegment = [Uri]::EscapeDataString($gameTag.Name)
    Save-RemoteFile "https://raw.githubusercontent.com/$GameDataRepository/$tagSegment/live/app/data/wowsinfo.json" $gameDataFile
    Save-RemoteFile "https://raw.githubusercontent.com/$GameDataRepository/$tagSegment/live/app/lang/lang.json" $languageFile

    $gameData = Read-JsonHashTable $gameDataFile
    $languages = Read-JsonHashTable $languageFile
    if ([string]$gameData['version'] -ne $gameTag.Name) {
        throw "Game data version does not match its tag: $($gameData['version']) != $($gameTag.Name)"
    }
    if (-not $gameData.ContainsKey('ships') -or -not $languages.ContainsKey('en') -or -not $languages.ContainsKey('zh_sg')) {
        throw 'World of Warships game or language data has an unsupported structure.'
    }

    $allowedTypes = @('AirCarrier', 'Battleship', 'Cruiser', 'Destroyer', 'Submarine')
    $ships = [ordered]@{}
    foreach ($entry in $gameData['ships'].GetEnumerator() | Sort-Object Key) {
        $ship = $entry.Value
        $id = [string]$entry.Key
        $type = [string]$ship['type']
        $region = [string]$ship['region']
        if ($id -notmatch '^\d+$' -or $allowedTypes -notcontains $type -or $region -eq 'Events') { continue }

        $tier = [int]$ship['tier']
        $nameKey = [string]$ship['name']
        if ($tier -lt 1 -or $tier -gt 11 -or [string]::IsNullOrWhiteSpace($nameKey)) { continue }
        $englishName = [string]$languages['en'][$nameKey]
        $chineseName = [string]$languages['zh_sg'][$nameKey]
        if ([string]::IsNullOrWhiteSpace($englishName)) { $englishName = [string]$ship['index'] }
        if ([string]::IsNullOrWhiteSpace($chineseName)) { $chineseName = $englishName }
        if ([string]::IsNullOrWhiteSpace($englishName)) { continue }

        $ships[$id] = [ordered]@{
            'name_en-us' = $englishName
            'name_zh-cn' = $chineseName
            nation = $region.ToLowerInvariant()
            tier = $tier
            type = $type
        }
    }
    if ($ships.Count -lt 900) {
        throw "Generated ship list is unexpectedly small: $($ships.Count)"
    }

    $shipOutput = [ordered]@{
        version = $shipVersion
        date = $shipDate
        source = [ordered]@{
            repository = $GameDataRepository
            tag = $gameTag.Name
            commit = $gameTag.CommitSha
        }
        ships = $ships
    }
    $shipOutputFile = Join-Path $tempDirectory 'ships.json'
    $shipOutput | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $shipOutputFile -Encoding UTF8NoBOM

    $escapedPrPath = [Uri]::EscapeDataString($PrDataPath)
    $prCommits = Invoke-GitHubJson "https://api.github.com/repos/$PrDataRepository/commits?path=$escapedPrPath&sha=$PrDataRef&per_page=1"
    if ($prCommits.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$prCommits[0].sha)) {
        throw 'Unable to resolve the latest PR data commit.'
    }
    $prCommitSha = [string]$prCommits[0].sha
    $prDataFile = Join-Path $tempDirectory 'expected_values.json'
    Save-RemoteFile "https://raw.githubusercontent.com/$PrDataRepository/$prCommitSha/$PrDataPath" $prDataFile
    $prData = Read-JsonHashTable $prDataFile
    $prTimestamp = [long]$prData['time']
    $prEntries = $prData['data']
    if ($prTimestamp -le 0 -or $null -eq $prEntries -or $prEntries.Count -lt 500) {
        throw 'PR expected-values data has an unsupported or incomplete structure.'
    }
    $validPrEntries = @($prEntries.Values | Where-Object {
        $_ -is [System.Collections.IDictionary] -and
        [double]$_['average_damage_dealt'] -gt 0 -and
        [double]$_['average_frags'] -gt 0 -and
        [double]$_['win_rate'] -gt 0
    }).Count
    if ($validPrEntries -lt 400) {
        throw "PR expected-values data contains too few usable ships: $validPrEntries"
    }

    $shipTarget = Join-Path $jsonDirectory 'ships.json'
    $prTarget = Join-Path $jsonDirectory 'expected_values.json'
    $existingShipDate = Get-ExistingNumericVersion $shipTarget 'date'
    $existingPrTimestamp = Get-ExistingNumericVersion $prTarget 'time'
    if ($existingShipDate -gt [long]$shipDate) {
        throw "Remote ship data would downgrade $existingShipDate to $shipDate."
    }
    if ($existingPrTimestamp -gt $prTimestamp) {
        throw "Remote PR data would downgrade $existingPrTimestamp to $prTimestamp."
    }

    $manifest = [ordered]@{
        shipList = [ordered]@{
            version = $shipVersion
            date = $shipDate
            ships = $ships.Count
            repository = $GameDataRepository
            tag = $gameTag.Name
            commit = $gameTag.CommitSha
            sha256 = Get-FileSha256 $shipOutputFile
        }
        prData = [ordered]@{
            timestamp = $prTimestamp
            date = [DateTimeOffset]::FromUnixTimeSeconds($prTimestamp).UtcDateTime.ToString('yyyy-MM-dd')
            ships = $prEntries.Count
            usableShips = $validPrEntries
            repository = $PrDataRepository
            ref = $PrDataRef
            commit = $prCommitSha
            sha256 = Get-FileSha256 $prDataFile
        }
    }
    $manifestFile = Join-Path $tempDirectory 'release_data.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestFile -Encoding UTF8NoBOM

    [IO.File]::Copy($shipOutputFile, $shipTarget, $true)
    [IO.File]::Copy($prDataFile, $prTarget, $true)
    [IO.File]::Copy($manifestFile, (Join-Path $jsonDirectory 'release_data.json'), $true)

    Write-Host "Ship list: $shipVersion ($shipDate), $($ships.Count) ships, source tag $($gameTag.Name)"
    Write-Host "PR data: $([DateTimeOffset]::FromUnixTimeSeconds($prTimestamp).UtcDateTime.ToString('yyyy-MM-dd')), $validPrEntries usable ships, source commit $($prCommitSha.Substring(0, 12))"
}
finally {
    if ($tempDirectory.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $tempDirectory)) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}
