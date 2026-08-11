# Собирает играбельный билд для плейтеста (docs/PLAYTEST.md).
# Использование:  powershell -File tools/build.ps1  [-UnityPath <путь к Unity.exe>] [-SkipTests]
# Выход: 0 — билд в Build/Windows/Alpha.exe; 1 — тесты красные или билд не собрался.
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\Unity.exe",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent

# Раздавать людям непроверенный билд бессмысленно: час чужого времени дороже
# трёх минут прогона. -SkipTests — только для «посмотреть самому».
if (-not $SkipTests) {
    Write-Host 'Прогон тестов перед сборкой...' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'run-tests.ps1') -UnityPath $UnityPath
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Тесты красные — билд не собираем.' -ForegroundColor Red
        exit 1
    }
}

$outDir = Join-Path $proj 'Build\Windows'
$exe = Join-Path $outDir 'Alpha.exe'
$log = Join-Path $proj 'Logs\build.log'
New-Item -ItemType Directory -Force -Path (Join-Path $proj 'Logs') | Out-Null
if (Test-Path $exe) { Remove-Item $exe -Force }

Write-Host 'Сборка Windows-билда...' -ForegroundColor Cyan
$p = Start-Process -FilePath $UnityPath -PassThru -ArgumentList @(
    '-quit', '-batchmode', '-projectPath', "`"$proj`"",
    '-executeMethod', 'Game.EditorTools.Builder.BuildWindows',
    '-logFile', "`"$log`"")
$p.WaitForExit()

if (-not (Test-Path $exe)) {
    Write-Host "Билд не собрался. Лог: $log" -ForegroundColor Red
    Select-String -Path $log -Pattern 'error CS|\[Builder\]|BuildFailedException' |
        Select-Object -First 15 | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
    exit 1
}

# Размер КАТАЛОГА, а не .exe: сам exe весит меньше мегабайта, всё остальное
# лежит в Alpha_Data — по размеру exe нельзя понять, что копировать тестеру.
$size = [math]::Round(((Get-ChildItem $outDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 0)
Write-Host "ГОТОВО: $exe (каталог $size МБ)" -ForegroundColor Green
Write-Host 'Тестеру отдавать ВЕСЬ каталог Build\Windows (exe без _Data не запустится).' -ForegroundColor Yellow
Write-Host 'После сессии забрать: %USERPROFILE%\AppData\LocalLow\<company>\Alpha\telemetry\' -ForegroundColor Yellow
exit 0
