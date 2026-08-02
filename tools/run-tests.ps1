# Воротарь коммита: гоняет ВСЕ тесты (EditMode + PlayMode) через Unity batchmode.
# Использование:  powershell -File tools/run-tests.ps1  [-UnityPath <путь к Unity.exe>]
# Выход: 0 — всё зелёное; 1 — есть провалы/нет результатов (смотри лог).
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\Unity.exe"
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent

function Invoke-TestRun([string]$platform) {
    # Результаты — в Logs/ проекта (в .gitignore): $env:TEMP на профилях с
    # кириллицей отдаёт короткий 8.3-путь, на котором давятся cmdlet'ы.
    $outDir = Join-Path $proj 'Logs'
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $res = Join-Path $outDir "tests-$platform.xml"
    $log = Join-Path $outDir "tests-$platform.log"
    if (Test-Path $res) { Remove-Item $res -Force }

    Write-Host "[$platform] запуск..." -ForegroundColor Cyan
    $p = Start-Process -FilePath $UnityPath -PassThru -ArgumentList @(
        '-runTests', '-batchmode', '-projectPath', "`"$proj`"",
        '-testPlatform', $platform, '-testResults', "`"$res`"", '-logFile', "`"$log`"")
    $p.WaitForExit()

    if (-not (Test-Path $res)) {
        Write-Host "[$platform] НЕТ res.xml — вероятно, ошибка компиляции. Лог: $log" -ForegroundColor Red
        Select-String -Path $log -Pattern 'error CS|Compilation failed' |
            Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
        return $false
    }

    $xml = [xml](Get-Content $res)
    $tr = $xml.'test-run'
    $ok = ([int]$tr.failed -eq 0) -and ([int]$tr.total -gt 0)
    $color = 'Red'; if ($ok) { $color = 'Green' }
    Write-Host "[$platform] total=$($tr.total) passed=$($tr.passed) failed=$($tr.failed) => $($tr.result)" -ForegroundColor $color
    if (-not $ok) {
        $xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
            Write-Host "  FAILED: $($_.fullname)" -ForegroundColor Red
        }
    }
    return $ok
}

$editOk = Invoke-TestRun 'EditMode'
$playOk = Invoke-TestRun 'PlayMode'

if ($editOk -and $playOk) {
    Write-Host 'ВСЕ ТЕСТЫ ЗЕЛЁНЫЕ' -ForegroundColor Green
    exit 0
}
Write-Host 'ЕСТЬ ПРОВАЛЫ — коммитить нельзя' -ForegroundColor Red
exit 1
