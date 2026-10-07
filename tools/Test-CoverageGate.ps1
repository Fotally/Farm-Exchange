$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('farm-coverage-' + [guid]::NewGuid().ToString('N'))
$gatePath = Join-Path $PSScriptRoot 'Test-Coverage.ps1'
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

function New-FixtureModule([string]$Name) {
    $directory = Join-Path $fixtureRoot "scripts/$Name"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $directory 'Example.cs') -Value '// 覆盖率夹具'
}

function New-ClassXml([string]$Filename, [int]$Covered, [int]$Valid) {
    $lines = for ($number = 1; $number -le $Valid; $number++) {
        '<line number="{0}" hits="{1}" />' -f $number, [int]($number -le $Covered)
    }
    '<class filename="{0}"><lines>{1}</lines></class>' -f [Security.SecurityElement]::Escape($Filename), ($lines -join '')
}

function Invoke-Fixture([string]$Name, [string]$Classes, [string]$ExpectedError = '', [string]$Sources = '') {
    $reportPath = Join-Path $fixtureRoot 'coverage.xml'
    Set-Content -LiteralPath $reportPath -Value "<coverage line-rate=`"0.9`">$Sources<packages><package><classes>$Classes</classes></package></packages></coverage>"
    $caught = $null
    $rows = @()
    try { $rows = @(& $gatePath -RepositoryRoot $fixtureRoot -ReportPath $reportPath) }
    catch { $caught = $_.Exception.Message }
    if ($ExpectedError) {
        if (-not $caught -or -not $caught.Contains($ExpectedError)) {
            throw "夹具 $Name 应拒绝并包含 [$ExpectedError]，实际：$caught"
        }
    } elseif ($caught) {
        throw "夹具 $Name 应通过，实际：$caught"
    }
    Write-Host "通过：$Name"
    $rows
}

try {
    New-FixtureModule 'gameplay'
    New-FixtureModule 'economy'
    $good = New-ClassXml 'scripts/gameplay/Example.cs' 90 90
    $bad = New-ClassXml 'scripts/economy/Example.cs' 0 10
    Invoke-Fixture '总体90%，单模块0%必须失败' ($good + $bad) 'economy 低于 80%' | Out-Null
    $game80 = New-ClassXml 'scripts/gameplay/Example.cs' 4 5
    $economy80 = New-ClassXml 'scripts/economy/Example.cs' 4 5
    $rows = @(Invoke-Fixture '恰好80%通过' ($game80 + $economy80))
    if ($rows[-1].Covered -ne 8 -or $rows[-1].Valid -ne 10) { throw '总体必须从去重行重新计算' }
    Invoke-Fixture '低于80%失败，不采信根line-rate' ((New-ClassXml 'scripts/gameplay/Example.cs' 3 5) + $economy80) '总体 低于 80%' | Out-Null
    $duplicate = New-ClassXml 'scripts\gameplay\Example.cs' 0 5
    $absolute = New-ClassXml (Join-Path $fixtureRoot 'scripts/gameplay/Example.cs') 5 5
    $rows = @(Invoke-Fixture '绝对/相对/反斜线同文件去重，任一命中有效' ($game80 + $duplicate + $absolute + $economy80))
    if ($rows[-1].Covered -ne 9 -or $rows[-1].Valid -ne 10) { throw '重复文件行计数错误' }
    $sources = '<sources><source>{0}</source></sources>' -f [Security.SecurityElement]::Escape((Join-Path $fixtureRoot 'scripts'))
    Invoke-Fixture '按Cobertura source解析相对路径' ((New-ClassXml 'gameplay/Example.cs' 4 5) + (New-ClassXml 'economy/Example.cs' 4 5)) '' $sources | Out-Null
    Invoke-Fixture '缺少既有模块失败' $game80 'economy 无有效业务行' | Out-Null
    Invoke-Fixture '空报告无业务行失败' '' '总体 无有效业务行' | Out-Null
    New-FixtureModule 'newmodule'
    Invoke-Fixture '动态新增模块不得漏验' ($game80 + $economy80) 'newmodule 无有效业务行' | Out-Null
    Invoke-Fixture '新增模块满足80%后通过' ($game80 + $economy80 + (New-ClassXml 'scripts/newmodule/Example.cs' 4 5)) | Out-Null
    $missingRejected = $false
    try { & $gatePath -RepositoryRoot $fixtureRoot -ReportPath (Join-Path $fixtureRoot 'missing.xml') | Out-Null }
    catch { $missingRejected = $_.Exception.Message.Contains('覆盖率报告不存在') }
    if (-not $missingRejected) { throw '缺失报告必须失败' }
    Write-Host '通过：缺失报告失败'
    Write-Host '覆盖率门禁全部夹具通过。'
} finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedFixture.StartsWith($tempRoot) -or [IO.Path]::GetFileName($resolvedFixture) -notlike 'farm-coverage-*') {
        throw "拒绝清理临时目录外的路径：$resolvedFixture"
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
