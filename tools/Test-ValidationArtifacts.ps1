param(
    [string]$EvidenceDirectory = 'build/issue137-138-validation'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "验收产物契约失败：$Message" }
}

function Assert-Rejected {
    param([scriptblock]$Action, [string]$ExpectedError)
    $caught = $null
    try { & $Action | Out-Null }
    catch { $caught = $_.Exception.Message }
    Assert-Contract ($null -ne $caught -and $caught.Contains($ExpectedError)) "应拒绝并包含 [$ExpectedError]，实际：$caught"
    Write-Host "通过：拒绝 $ExpectedError"
}

function Get-WorkflowStep {
    param([string]$Content, [string]$Name)
    $match = [regex]::Match($Content, "(?ms)^      - name: $([regex]::Escape($Name))\r?`n(?<body>.*?)(?=^      - name: |\z)")
    Assert-Contract $match.Success "工作流缺少步骤：$Name"
    return $match.Groups['body'].Value
}

function Test-WorkflowCondition {
    param([string]$Step, [string]$Outcome, [bool]$Cancelled = $false)
    $match = [regex]::Match($Step, '(?m)^        if: (?<condition>.+)\r?$')
    $condition = if ($match.Success) { $match.Groups['condition'].Value.Trim() } else { 'success()' }
    $condition = $condition -replace '^\$\{\{\s*|\s*\}\}$', ''
    # GitHub 对未显式使用状态函数的条件附加 success()。
    if ($condition -notmatch '\b(success|failure|cancelled|always)\(' -and ($Cancelled -or $Outcome -eq 'failure')) {
        return $false
    }
    foreach ($stepId in @('distribution', 'package', 'resources', 'macos-package')) {
        $condition = $condition.Replace("steps.$stepId.outcome", "'$Outcome'")
    }
    $condition = $condition.Replace('cancelled()', "`$$($Cancelled.ToString().ToLowerInvariant())")
    $condition = $condition.Replace('success()', "`$$(((-not $Cancelled) -and $Outcome -ne 'failure').ToString().ToLowerInvariant())")
    $condition = $condition.Replace('failure()', "`$$(($Outcome -eq 'failure').ToString().ToLowerInvariant())").Replace('always()', '$true')
    $condition = $condition -replace '(?<!\$)\btrue\b', '$true' -replace '(?<!\$)\bfalse\b', '$false'
    $condition = $condition.Replace('&&', ' -and ').Replace('||', ' -or ').Replace('==', ' -eq ').Replace('!', ' -not ')
    $remaining = $condition -replace "\`$(true|false)|'(success|failure|skipped|cancelled)'|-and|-or|-eq|-not|[\s()]", ''
    Assert-Contract ($remaining -eq '') "未识别的工作流条件：$condition"
    return [bool](& ([scriptblock]::Create($condition)))
}

$ci = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw
$events = [regex]::Match($ci, '(?ms)^on:\r?\n(?<events>.*?)(?=^\S|\z)')
$pullRequest = [regex]::Match($events.Groups['events'].Value, '(?ms)^  pull_request:\r?\n(?<config>.*?)(?=^  \S|\z)')
Assert-Contract $pullRequest.Success '向 dev 的 PR 必须在合并前触发原生 Windows 门禁'
$branchFilter = [regex]::Match($pullRequest.Groups['config'].Value, '(?ms)^    branches:[ \t]*(?:\[(?<inline>[^\]]+)\]|\r?\n(?<block>(?:^      - [^\r\n]*\r?\n?)+))')
[string[]]$targets = if ($branchFilter.Groups['inline'].Success) {
    @($branchFilter.Groups['inline'].Value.Split(',') | ForEach-Object { $_.Trim().Trim("'", '"') })
} else {
    @([regex]::Matches($branchFilter.Groups['block'].Value, '(?m)^      - ([^\r\n]+)') | ForEach-Object { $_.Groups[1].Value.Trim().Trim("'", '"') })
}
Assert-Contract ($targets.Count -eq 1 -and $targets[0] -ceq 'dev') 'PR 门禁仅对 dev 目标启用'
$windows = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/windows.yml') -Raw
$macosWorkflow = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/macos.yml') -Raw
$groups = @()
foreach ($workflow in @($ci, $windows, $macosWorkflow)) {
    Assert-Contract ($workflow -match '(?ms)^  push:\r?\n    branches:\r?\n      - dev\r?\n      - main' -and $workflow -match '(?m)^  workflow_dispatch:') '三条 CI 均须支持 dev/main 推送及手动运行'
    Assert-Contract ($workflow -notmatch '(?m)^  needs:|^    needs:|workflow_run:') '测试与两平台构建须独立启动'
    Assert-Contract ($workflow -notmatch 'inputs\.performance|Run-PerformanceTests\.ps1|Save-PerformanceEvidence\.ps1') 'GitHub CI 不得提供或执行有窗口 FPS 测量'
    $groups += [regex]::Match($workflow, '(?m)^  group: (.+)\r?$').Groups[1].Value.Trim()
}
Assert-Contract (@($groups | Select-Object -Unique).Count -eq 3) '三条 CI 的并发取消组须相互独立'
$headless = Get-WorkflowStep $ci '运行场景测试并检查行覆盖率'
Assert-Contract ($headless -match 'Run-Tests\.ps1' -and $headless -notmatch '-Performance') '测试 CI 须保留普通 headless 与覆盖率入口'
Assert-Contract ($ci -notmatch 'export-release|Invoke-WindowsDistribution|FarmExchange-windows-x86_64') '测试 CI 不再承担 Windows 导出'
$export = Get-WorkflowStep $windows '导出 Windows x86_64'
Assert-Contract ($export -match '--export-release Windows' -and $export -notmatch '(?m)^        if:') 'Windows 导出须在 dev/main 与手动运行中执行'
Get-WorkflowStep $windows '导入项目资源' | Out-Null
$release = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/release.yml') -Raw
foreach ($binding in @(
    @{ Content = $windows; Startup = '启动导出程序验收'; Upload = '上传 Windows 启动验收证据' },
    @{ Content = $release; Startup = '验收 Windows 游戏并生成版本压缩包'; Upload = '上传 Windows 发布验收证据' }
)) {
    $step = Get-WorkflowStep $binding.Content $binding.Startup
    Assert-Contract ($step -match 'Invoke-WindowsDistribution\.ps1' -and $step -match '-SchemaPath docs/project/runtime-log-schema-v1\.md' -and $step -notmatch 'Start-Process') 'CI 与发布须经同一真实分发命令'
    $step = Get-WorkflowStep $binding.Content $binding.Upload
    Assert-Contract (Test-WorkflowCondition $step 'failure') 'Windows 启动失败时仍须上传已有证据'
    Assert-Contract (-not (Test-WorkflowCondition $step 'skipped')) '未执行 Windows 验收不能上传旧证据'
    Assert-Contract (-not (Test-WorkflowCondition $step 'cancelled' $true)) '取消后不承诺继续上传 Windows 证据'
}
$checkout = Get-WorkflowStep $release '检出本次发布提交的分发工具与 schema'
Assert-Contract ($checkout -match 'ref: \$\{\{ github.sha \}\}') '发布 helper 与 schema 须从精确发布 SHA 检出'
$macos = Get-WorkflowStep $release '检查 macOS 应用包并保留原始 ZIP'
Assert-Contract ($macos -match 'Copy-Item -LiteralPath \$sourceArchive -Destination \$archive' -and $macos -notmatch 'CreateFromDirectory') 'macOS 发布须保留原 ZIP 字节'
$static = Get-WorkflowStep $ci '修复规则链接并运行静态检查'
Assert-Contract ($static -match '\./tools/Test-ValidationArtifacts\.ps1') '验收产物契约须进入真实 CI'
& (Join-Path $PSScriptRoot 'Test-RuntimePackChecks.ps1')
foreach ($content in @($macosWorkflow, $release)) {
    Assert-Contract ($content -match 'Test-RuntimePack\.ps1 -PackPath') 'macOS CI 与发布须检查实际 ZIP 内的 PCK'
}
foreach ($binding in @(
    @{ Content = $macosWorkflow; Upload = '上传 macOS 资源清单' },
    @{ Content = $release; Upload = '上传 macOS 发布资源清单' }
)) {
    $step = Get-WorkflowStep $binding.Content $binding.Upload
    Assert-Contract (Test-WorkflowCondition $step 'failure') 'macOS 资源检查失败仍须保存实际清单'
    Assert-Contract (-not (Test-WorkflowCondition $step 'skipped')) '未执行 macOS 资源检查不能上传旧清单'
    Assert-Contract (-not (Test-WorkflowCondition $step 'cancelled' $true)) '取消后不承诺上传 macOS 资源清单'
}

$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
$runRoot = Join-Path $evidenceRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

# 执行发布工作流中的真实预检脚本，仅替换 GitHub API，覆盖三条 CI 和两平台产物。
$sourceStep = Get-WorkflowStep $release '核对版本与同提交成功 CI'
$sourceMatch = [regex]::Match($sourceStep, '(?s)        run: \|\r?\n(?<script>.*)')
Assert-Contract $sourceMatch.Success '发布来源预检必须提供可执行脚本'
$sourceScript = [scriptblock]::Create(($sourceMatch.Groups['script'].Value -replace '(?m)^          ', ''))
foreach ($case in @(
    @{ Name = '成功'; Workflow = ''; Failure = ''; Error = $null },
    @{ Name = '测试未成功'; Workflow = 'ci.yml'; Failure = 'missing'; Error = '尚无成功的 main 测试 CI' },
    @{ Name = 'Windows 未成功'; Workflow = 'windows.yml'; Failure = 'missing'; Error = '尚无成功的 main Windows CI' },
    @{ Name = 'macOS 未成功'; Workflow = 'macos.yml'; Failure = 'missing'; Error = '尚无成功的 main macOS CI' },
    @{ Name = '测试提交不符'; Workflow = 'ci.yml'; Failure = 'sha'; Error = '来源、提交或完成状态' },
    @{ Name = 'Windows 提交不符'; Workflow = 'windows.yml'; Failure = 'sha'; Error = '来源、提交或完成状态' },
    @{ Name = 'macOS 提交不符'; Workflow = 'macos.yml'; Failure = 'sha'; Error = '来源、提交或完成状态' },
    @{ Name = 'Windows 产物缺失'; Workflow = 'windows.yml'; Failure = 'artifact'; Error = '缺失或已过期' },
    @{ Name = 'macOS 产物过期'; Workflow = 'macos.yml'; Failure = 'expired'; Error = '缺失或已过期' }
)) {
    & {
        $names = @('GITHUB_REF', 'GITHUB_SHA', 'GITHUB_REPOSITORY', 'RELEASE_VERSION', 'GITHUB_OUTPUT', 'GITHUB_STEP_SUMMARY')
        $saved = @{}
        foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
        $requests = [Collections.Generic.List[string]]::new()
        $fixtureOutput = Join-Path $runRoot 'release-output.txt'
        try {
            $env:GITHUB_REF = 'refs/heads/main'
            $env:GITHUB_SHA = 'fixture-sha'
            $env:GITHUB_REPOSITORY = 'fixture/repository'
            $env:RELEASE_VERSION = 'v0.1.0'
            $env:GITHUB_OUTPUT = $fixtureOutput
            $env:GITHUB_STEP_SUMMARY = Join-Path $runRoot 'release-summary.txt'
            Set-Content -LiteralPath $fixtureOutput -Value ''
            function gh {
                param([string]$Operation, [string]$Endpoint)
                Assert-Contract ($Operation -eq 'api') '预检仅允许只读 API'
                $requests.Add($Endpoint)
                $global:LASTEXITCODE = 0
                if ($Endpoint -match '/git/matching-refs/') { return '[]' }
                if ($Endpoint -match '/workflows/(?<workflow>[^/]+)/runs\?') {
                    $workflowName = $Matches['workflow']
                    Assert-Contract ($Endpoint -match 'branch=main&head_sha=fixture-sha&status=success') '来源查询须限定 main、同提交与成功结果'
                    if ($workflowName -eq $case.Workflow -and $case.Failure -eq 'missing') { return '{"workflow_runs":[]}' }
                    $sha = if ($workflowName -eq $case.Workflow -and $case.Failure -eq 'sha') { 'other-sha' } else { $env:GITHUB_SHA }
                    return @{ workflow_runs = @(@{ id = $workflowName; head_branch = 'main'; head_sha = $sha; head_repository = @{ full_name = $env:GITHUB_REPOSITORY }; status = 'completed'; conclusion = 'success'; html_url = "https://example.invalid/$workflowName" }) } | ConvertTo-Json -Depth 5 -Compress
                }
                if ($Endpoint -match '/runs/(?<workflow>[^/]+)/artifacts\?name=(?<artifact>[^&]+)') {
                    $workflowName = $Matches['workflow']
                    $artifactName = $Matches['artifact']
                    Assert-Contract ($workflowName -ne 'ci.yml') '测试 CI 不应提供平台构建产物'
                    if ($workflowName -eq $case.Workflow -and $case.Failure -eq 'artifact') { return '{"artifacts":[]}' }
                    $expired = $workflowName -eq $case.Workflow -and $case.Failure -eq 'expired'
                    return @{ artifacts = @(@{ name = $artifactName; expired = $expired; expires_at = [DateTimeOffset]::UtcNow.AddDays(1).ToString('o') }) } | ConvertTo-Json -Depth 4 -Compress
                }
                throw "未预期的 API：$Endpoint"
            }
            if ($case.Error) { Assert-Rejected { & $sourceScript } $case.Error }
            else {
                & $sourceScript
                Assert-Contract (@($requests | Where-Object { $_ -match '/workflows/.+/runs\?' }).Count -eq 3) '发布须核对三条独立 CI'
                $outputs = Get-Content -LiteralPath $fixtureOutput -Raw
                Assert-Contract ($outputs.Contains('windows-run-id=windows.yml') -and $outputs.Contains('macos-run-id=macos.yml')) '下载来源须来自各自独立构建'
            }
        } finally {
            foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
        }
    }
}

$probeRoot = Join-Path $runRoot 'probe'
New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $evidenceRoot '.gdignore'), '')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'validation-artifacts-fixture/Program.cs.txt') -Destination (Join-Path $probeRoot 'Program.cs')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $probeRoot 'ValidationFixture.csproj') -Encoding utf8
dotnet build (Join-Path $probeRoot 'ValidationFixture.csproj') --configuration Release --output (Join-Path $probeRoot 'app') --verbosity quiet
Assert-Contract ($LASTEXITCODE -eq 0) '必须成功构建真实可执行夹具'
$probeName = if ($IsWindows) { 'ValidationFixture.exe' } else { 'ValidationFixture' }
$probe = Join-Path $probeRoot "app/$probeName"
$schema = Join-Path $runRoot 'schema-source.md'
[IO.File]::WriteAllBytes($schema, [byte[]](239, 187, 191, 35, 32, 83, 99, 104, 101, 109, 97, 13, 10))
$distributionCommand = Join-Path $PSScriptRoot 'Invoke-WindowsDistribution.ps1'
$performanceCommand = Join-Path $PSScriptRoot 'Run-PerformanceTests.ps1'
$savePerformanceCommand = Join-Path $PSScriptRoot 'Save-PerformanceEvidence.ps1'
. (Join-Path $PSScriptRoot 'runtime-pack-fixture/New-RuntimePack.ps1')

function New-DistributionFixture {
    param([string]$Name, [hashtable]$Settings = @{})
    $directory = Join-Path $runRoot $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    Copy-Item -Path (Join-Path $probeRoot 'app/*') -Destination $directory
    Copy-Item -LiteralPath $probe -Destination (Join-Path $directory 'FarmExchange.exe')
    New-Item -ItemType Directory -Path (Join-Path $directory 'data_FarmExchange_windows_x86_64') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $directory 'logs/runtime') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $directory 'logs/debug') | Out-Null
    New-RuntimePackFixture -Path (Join-Path $directory 'FarmExchange.pck') -RepoRoot $repoRoot
    Set-Content -LiteralPath (Join-Path $directory 'data_FarmExchange_windows_x86_64/FarmExchange.dll') -Value 'fixture managed assembly'
    Set-Content -LiteralPath (Join-Path $directory 'engine-dependency.bin') -Value 'other runtime dependency'
    Set-Content -LiteralPath (Join-Path $directory 'logs/runtime/sentinel.log') -Value 'historical runner runtime session'
    Set-Content -LiteralPath (Join-Path $directory 'logs/debug/sentinel.log') -Value 'historical runner debug session'
    $Settings | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $directory 'fixture.json') -Encoding utf8
    return $directory
}

$build = New-DistributionFixture 'clean-build'
$archive = Join-Path $runRoot 'clean.zip'
& $distributionCommand -BuildDirectory $build -SchemaPath $schema -EvidenceDirectory (Join-Path $runRoot 'clean-evidence') -ArchivePath $archive
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    Assert-Contract (@($zip.Entries | Where-Object { $_.FullName -match '^logs/(runtime|debug)/' }).Count -eq 0) '最终 ZIP 不能包含哨兵或本次验收会话'
    foreach ($name in @('FarmExchange.exe', 'FarmExchange.pck', 'data_FarmExchange_windows_x86_64/FarmExchange.dll', 'engine-dependency.bin', 'logs/schema-v1.md')) {
        Assert-Contract ($null -ne $zip.GetEntry($name)) "最终 ZIP 必须保留 $name"
    }
    $stream = $zip.GetEntry('logs/schema-v1.md').Open()
    try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
    Assert-Contract ($hash -ceq (Get-FileHash -LiteralPath $schema).Hash) '最终 ZIP 内 schema 必须与源字节相同'
} finally { $zip.Dispose() }
foreach ($kind in @('runtime', 'debug')) {
    Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $build "logs/$kind"))) "分发目录不能遗留 $kind 会话"
    foreach ($name in @('sentinel.log', 'current.log')) {
        Assert-Contract (Test-Path -LiteralPath (Join-Path $runRoot "clean-evidence/logs/$kind/$name")) "验收证据必须保留 $kind/$name"
    }
}
Assert-Contract ((Get-Content -LiteralPath (Join-Path $runRoot 'clean-evidence/stdout.log') -Raw).Contains('fixture startup stdout')) 'stdout 须保存'
Assert-Contract ((Get-Content -LiteralPath (Join-Path $runRoot 'clean-evidence/stderr.log') -Raw).Contains('fixture startup stderr')) 'stderr 须保存'

# CI 上传目录与 Release 二次启动走同一命令，第二次新会话仍不能进入 ZIP。
$secondEvidence = Join-Path $runRoot 'second-evidence'
$secondArchive = Join-Path $runRoot 'second.zip'
& $distributionCommand -BuildDirectory $build -SchemaPath $schema -EvidenceDirectory $secondEvidence -ArchivePath $secondArchive
Assert-Contract (Test-Path -LiteralPath (Join-Path $secondEvidence 'logs/runtime/current.log')) '第二次启动须单独保存新会话'
Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $build 'logs/runtime'))) '第二次启动的分发目录也须干净'
$firstStart = Join-Path $runRoot 'first-player-start'
[IO.Compression.ZipFile]::ExtractToDirectory($secondArchive, $firstStart)
$process = Start-Process -FilePath (Join-Path $firstStart 'FarmExchange.exe') -WorkingDirectory $firstStart -ArgumentList @('--headless', '--quit-after', '5') -Wait -PassThru
Assert-Contract ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $firstStart 'logs/runtime/current.log'))) '干净解压后真实首次启动须生成本次会话'
$process.Dispose()
Assert-Contract (Test-Path -LiteralPath (Join-Path $firstStart 'engine-dependency.bin')) '其他运行依赖须原样保留'

$failedBuild = New-DistributionFixture 'nonzero-build' @{ ExitCode = 17 }
$failedEvidence = Join-Path $runRoot 'nonzero-evidence'
$failedArchive = Join-Path $runRoot 'nonzero.zip'
Assert-Rejected { & $distributionCommand -BuildDirectory $failedBuild -SchemaPath $schema -EvidenceDirectory $failedEvidence -ArchivePath $failedArchive } '退出码：17'
Assert-Contract (-not (Test-Path -LiteralPath $failedArchive)) '启动失败不能留下最终 ZIP'
Assert-Contract (Test-Path -LiteralPath (Join-Path $failedEvidence 'logs/runtime/current.log')) '失败启动的真实会话须保存'
Assert-Contract ((Get-Content -LiteralPath (Join-Path $failedEvidence 'startup.json') -Raw | ConvertFrom-Json).ExitCode -eq 17) '收集成功不能盖掉原退出码'

$blockedBuild = New-DistributionFixture 'blocked-evidence-build' @{ ExitCode = 23; BlockEvidencePath = (Join-Path $runRoot 'blocked-evidence/logs/runtime/sentinel.log') }
$blockedEvidence = Join-Path $runRoot 'blocked-evidence'
$caught = $null
try { & $distributionCommand -BuildDirectory $blockedBuild -SchemaPath $schema -EvidenceDirectory $blockedEvidence }
catch { $caught = $_.Exception.Message }
Assert-Contract ($caught -and $caught.Contains('退出码：23') -and $caught.Contains('源数据保留')) '日志保存失败须同时保留原启动错误'
Assert-Contract (Test-Path -LiteralPath (Join-Path $blockedBuild 'logs/runtime/sentinel.log')) '保存失败不得先删源哨兵日志'
Assert-Contract (Test-Path -LiteralPath (Join-Path $blockedBuild 'logs/runtime/current.log')) '保存失败不得删除源本次日志'

foreach ($case in @(
    @{ Name = 'corrupt-schema'; Settings = @{ CorruptSchema = $true }; Error = 'schema 字节' },
    @{ Name = 'deleted-pck'; Settings = @{ DeletePck = $true }; Error = 'FarmExchange.pck' }
)) {
    $invalidBuild = New-DistributionFixture $case.Name $case.Settings
    $invalidArchive = Join-Path $runRoot "$($case.Name).zip"
    Assert-Rejected { & $distributionCommand -BuildDirectory $invalidBuild -SchemaPath $schema -EvidenceDirectory (Join-Path $runRoot "$($case.Name)-evidence") -ArchivePath $invalidArchive } $case.Error
    Assert-Contract (-not (Test-Path -LiteralPath $invalidArchive)) '内容检查失败不能生成最终 ZIP'
}
$missingBuild = New-DistributionFixture 'missing-exe'
Remove-Item -LiteralPath (Join-Path $missingBuild 'FarmExchange.exe')
Assert-Rejected { & $distributionCommand -BuildDirectory $missingBuild -SchemaPath $schema -EvidenceDirectory (Join-Path $runRoot 'missing-exe-evidence') } '构建缺少有效运行文件'
foreach ($case in @(
    @{ Name = 'pck-document'; Remove = @(); Add = @{ 'docs/unexpected.png.import' = 'unexpected' }; Error = '混入文档附件' },
    @{ Name = 'pck-retired'; Remove = @(); Add = @{ '.godot/imported/npc_animation_001.png-00000000000000000000000000000000.ctex' = 'unexpected' }; Error = '退役 NPC' },
    @{ Name = 'pck-missing'; Remove = @('assets/gameplay/terrain/roads/atlas_regions.json'); Add = @{}; Error = '必需运行资源' }
)) {
    $invalidBuild = New-DistributionFixture $case.Name
    New-RuntimePackFixture -Path (Join-Path $invalidBuild 'FarmExchange.pck') -RepoRoot $repoRoot -Remove $case.Remove -Add $case.Add
    $invalidArchive = Join-Path $runRoot "$($case.Name).zip"
    Assert-Rejected { & $distributionCommand -BuildDirectory $invalidBuild -SchemaPath $schema -EvidenceDirectory (Join-Path $runRoot "$($case.Name)-evidence") -ArchivePath $invalidArchive } $case.Error
    Assert-Contract (-not (Test-Path -LiteralPath $invalidArchive)) 'PCK 检查失败不能留下最终 ZIP'
}
Assert-Rejected { & $distributionCommand -BuildDirectory $build -SchemaPath $schema -EvidenceDirectory (Join-Path $build 'bad-evidence') } '不能相同或互相包含'
Assert-Rejected { & $distributionCommand -BuildDirectory $build -SchemaPath $schema -EvidenceDirectory (Join-Path $runRoot 'bad-archive-evidence') -ArchivePath (Join-Path $build 'bad.zip') } '最终 ZIP 必须'
Assert-Rejected { & $distributionCommand -BuildDirectory $build -SchemaPath (Join-Path $build 'logs/schema-v1.md') -EvidenceDirectory (Join-Path $runRoot 'bad-schema-evidence') } 'schema 源必须'

foreach ($case in @(
    @{ Name = 'success'; ExitCode = 0; Files = @('performance.json', 'performance.png', 'performance-end.png'); Error = $null },
    @{ Name = 'threshold-failure'; ExitCode = 19; Files = @('performance.json', 'performance.png', 'performance-end.png'); Error = '退出码：19' },
    @{ Name = 'partial-failure'; ExitCode = 29; Files = @('performance.png'); Error = '退出码：29' },
    @{ Name = 'empty-failure'; ExitCode = 31; Files = @(); Error = '退出码：31' },
    @{ Name = 'missing-report'; ExitCode = 0; Files = @('performance.png'); Error = '没有生成 coverage/performance.json' }
)) {
    $measurementRoot = Join-Path $runRoot "fps-$($case.Name)"
    New-Item -ItemType Directory -Path (Join-Path $measurementRoot 'coverage') -Force | Out-Null
    foreach ($name in @('performance.json', 'performance.png', 'performance-end.png')) {
        Set-Content -LiteralPath (Join-Path $measurementRoot "coverage/$name") -Value 'old run must not survive'
    }
    @{ ExitCode = $case.ExitCode; PerformanceFiles = $case.Files } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $measurementRoot 'fixture.json') -Encoding utf8
    Push-Location $measurementRoot
    try {
        $caught = $null
        try { & $performanceCommand -GodotConsole $probe }
        catch { $caught = $_.Exception.Message }
        if ($case.Error) { Assert-Contract ($caught -and $caught.Contains($case.Error)) "FPS $($case.Name)须保留原测量错误：$caught" }
        else { Assert-Contract ($null -eq $caught) "成功 FPS 不应失败：$caught" }
        $outcome = if ($caught) { 'failure' } else { 'success' }
        $destination = Join-Path $measurementRoot 'saved'
        & $savePerformanceCommand -SourceDirectory coverage -DestinationDirectory saved -MeasurementOutcome $outcome
        $manifest = Get-Content -LiteralPath (Join-Path $destination 'manifest.json') -Raw | ConvertFrom-Json
        Assert-Contract ($manifest.MeasurementOutcome -eq $outcome) '保存证据不得把失败测量写成成功'
        Assert-Contract ($manifest.Files.Count -eq 3) '清单须明确三项存在或缺失'
        foreach ($entry in $manifest.Files) {
            $expected = $case.Files -contains $entry.Name
            Assert-Contract ($entry.Present -eq $expected) "FPS $($case.Name)清单必须记录实际 $($entry.Name)"
            Assert-Contract ((Test-Path -LiteralPath (Join-Path $destination $entry.Name)) -eq $expected) '不存在的性能文件不得伪造或复用旧产物'
            if ($expected) {
                Assert-Contract ($entry.Sha256 -ceq (Get-FileHash -LiteralPath (Join-Path $measurementRoot "coverage/$($entry.Name)")).Hash) '证据复制必须保留真实字节'
                Assert-Contract ($entry.Sha256 -ceq (Get-FileHash -LiteralPath (Join-Path $destination $entry.Name)).Hash) '保存的证据字节须与清单相同'
            }
        }
    } finally { Pop-Location }
}
foreach ($outcome in @('skipped', 'cancelled')) {
    $destination = Join-Path $runRoot "fps-$outcome"
    Assert-Rejected { & $savePerformanceCommand -SourceDirectory (Join-Path $runRoot 'missing-coverage') -DestinationDirectory $destination -MeasurementOutcome $outcome } 'ValidateSet'
    Assert-Contract (-not (Test-Path -LiteralPath $destination)) '未测量或取消不能生成性能证据'
}

$timeoutBuild = New-DistributionFixture 'timeout-build' @{ Timeout = $true }
$timeoutEvidence = Join-Path $runRoot 'timeout-evidence'
$timer = [Diagnostics.Stopwatch]::StartNew()
Assert-Rejected { & $distributionCommand -BuildDirectory $timeoutBuild -SchemaPath $schema -EvidenceDirectory $timeoutEvidence -ArchivePath (Join-Path $runRoot 'timeout.zip') } '超过 60 秒'
$timer.Stop()
$timeoutFacts = Get-Content -LiteralPath (Join-Path $timeoutEvidence 'startup.json') -Raw | ConvertFrom-Json
Assert-Contract ($timeoutFacts.TimedOut -and $timeoutFacts.ProcessExited -and $timer.Elapsed.TotalSeconds -ge 59 -and $timer.Elapsed.TotalSeconds -lt 90) '真实进程超时须在 60 秒结束并记录'
Assert-Contract (Test-Path -LiteralPath (Join-Path $timeoutEvidence 'logs/runtime/current.log')) '终止超时进程后须保存实际日志'
Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $runRoot 'timeout.zip'))) '超时启动不能留下最终 ZIP'

Write-Host "验收产物契约通过；证据：$runRoot"
