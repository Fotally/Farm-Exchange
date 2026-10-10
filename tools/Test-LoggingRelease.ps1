param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [string]$OutputDirectory = 'build/issue130-validation/release-probe'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$sourceAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
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
dotnet run --project $projectPath --configuration Release -- (Join-Path $outputRoot 'evidence')
if ($LASTEXITCODE -ne 0) { throw "真实 Release 程序集门禁失败：$sourceAssembly" }
$report = Get-Content -LiteralPath (Join-Path $outputRoot 'evidence/report.json') -Raw | ConvertFrom-Json
$hash = (Get-FileHash -LiteralPath $sourceAssembly -Algorithm SHA256).Hash
if ($hash -ne $report.Sha256) { throw '探针载入程序集与指定的发布程序集哈希不同' }
$report | Add-Member -NotePropertyName SourceAssembly -NotePropertyValue $sourceAssembly
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'report.json') -Encoding utf8
