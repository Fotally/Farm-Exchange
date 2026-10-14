param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [string]$OutputDirectory = 'build/issue130-validation/release-probe'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$sourceAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$weatherRulesPath = (Resolve-Path -LiteralPath (Join-Path $repository 'assets/gameplay/weather/weather_rules.json')).Path
$terrainRulesPath = (Resolve-Path -LiteralPath (Join-Path $repository 'assets/gameplay/terrain/terrain_rules.json')).Path
$assemblyDirectory = Split-Path -Parent $sourceAssembly
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
if (Test-Path -LiteralPath $outputRoot) { throw "输出目录已存在：$outputRoot" }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$projectDirectory = Join-Path $outputRoot 'probe'
New-Item -ItemType Directory -Path $projectDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'logging-release-probe/Program.cs.txt') -Destination (Join-Path $projectDirectory 'Program.cs')
$references = @($sourceAssembly, (Join-Path $assemblyDirectory 'GodotSharp.dll'))
$references += @(Get-ChildItem -LiteralPath $assemblyDirectory -Filter 'Serilog*.dll' -File | Select-Object -ExpandProperty FullName)
$references += @(Get-ChildItem -LiteralPath $assemblyDirectory -Filter 'Microsoft.Extensions.*.dll' -File | Select-Object -ExpandProperty FullName)
$referenceXml = foreach ($path in $references) {
    $name = [Security.SecurityElement]::Escape([IO.Path]::GetFileNameWithoutExtension($path))
    $escaped = [Security.SecurityElement]::Escape($path)
    "<Reference Include=`"$name`"><HintPath>$escaped</HintPath><Private>true</Private></Reference>"
}
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
  $($referenceXml -join [Environment]::NewLine)
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $projectDirectory 'LoggingReleaseProbe.csproj'
Set-Content -LiteralPath $projectPath -Value $project -Encoding utf8
dotnet run --project $projectPath --configuration Release -- (Join-Path $outputRoot 'evidence') $weatherRulesPath $terrainRulesPath
if ($LASTEXITCODE -ne 0) { throw "真实 Release 程序集门禁失败：$sourceAssembly" }
$report = Get-Content -LiteralPath (Join-Path $outputRoot 'evidence/report.json') -Raw | ConvertFrom-Json
$hash = (Get-FileHash -LiteralPath $sourceAssembly -Algorithm SHA256).Hash
if ($hash -ne $report.Sha256) { throw '探针载入程序集与指定的发布程序集哈希不同' }
$weatherRulesHash = (Get-FileHash -LiteralPath $weatherRulesPath -Algorithm SHA256).Hash
if ($weatherRulesPath -ne $report.WeatherRulesPath -or $weatherRulesHash -ne $report.WeatherRulesSha256) {
    throw "探针天气配置与指定的正式配置路径或哈希不同：$weatherRulesPath"
}
$terrainRulesHash = (Get-FileHash -LiteralPath $terrainRulesPath -Algorithm SHA256).Hash
if ($terrainRulesPath -ne $report.TerrainRulesPath -or $terrainRulesHash -ne $report.TerrainRulesSourceSha256) {
    throw "探针地形配置来源与指定的正式配置路径或哈希不同：$terrainRulesPath"
}
$effectiveTerrainPath = Join-Path $outputRoot 'evidence/terrain-all-land.json'
if ((Get-FileHash -LiteralPath $effectiveTerrainPath -Algorithm SHA256).Hash -ne $report.TerrainRulesEffectiveSha256) {
    throw "探针显式纯陆地配置哈希不同：$effectiveTerrainPath"
}
$report | Add-Member -NotePropertyName SourceAssembly -NotePropertyValue $sourceAssembly
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'report.json') -Encoding utf8
