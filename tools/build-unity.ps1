# Собирает Unity-билд тестовой сборки «все механики сразу» (Поправка №7):
# ОДНА сцена Game.unity (титул → создание → хаб → финал), пересобранная
# кодом заново при каждом запуске — сцена не лежит в репозитории (ссылки на
# скрипты по GUID из .meta создаются локально), поэтому её всегда собирают
# перед билдом, а не полагаются на закомиченный файл (R19).
#
# Текстовый срез отдаётся через tools/publish-play.ps1 и ничего от Unity не
# требует. Этот скрипт — про играбельный билд.
#
# Использование:  powershell -File tools/build-unity.ps1
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\Unity.exe",
    [switch]$SkipScene
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
$out = Join-Path $proj "Build\Windows"
$exe = Join-Path $out "Alpha.exe"

if (-not (Test-Path $UnityPath)) {
    Write-Host "Unity не найден: $UnityPath" -ForegroundColor Red
    Write-Host "Передай путь: -UnityPath <...\Unity.exe>" -ForegroundColor Yellow
    exit 1
}

New-Item -ItemType Directory -Force -Path (Join-Path $proj "Logs") | Out-Null

function Invoke-Unity([string]$method, [string]$logName) {
    $log = Join-Path $proj "Logs\$logName"
    # Unity детачится при обычном запуске — нужен PassThru и ожидание.
    $p = Start-Process -FilePath $UnityPath -PassThru -ArgumentList @(
        '-quit', '-batchmode', '-projectPath', "`"$proj`"",
        '-executeMethod', $method, '-logFile', "`"$log`"")
    $p.WaitForExit()
    return $log
}

if (-not $SkipScene) {
    # Сцены в репозитории нет намеренно: она ссылается на скрипты по GUID из
    # .meta, а те создаются локально. Поэтому сначала собираем её.
    #
    # Импорт Kenney (KenneyImportSettings) здесь НЕ перезапускается — это
    # отдельный, медленный шаг, нужный только когда сами исходники набора
    # меняются, а не при каждой пересборке сцены/билда.
    Write-Host "Сборка сцены «Игра»..." -ForegroundColor Cyan
    $log = Invoke-Unity 'Game.Gameplay.EditorTools.GameSceneBuilder.Build' 'scene.log'
    if (-not (Test-Path (Join-Path $proj "Assets\Scenes\Game.unity"))) {
        Write-Host "Сцена не собралась. Лог: $log" -ForegroundColor Red
        Select-String -Path $log -Pattern 'error CS|Exception' | Select-Object -First 10 |
            ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
        exit 1
    }
}

if (Test-Path $exe) { Remove-Item $exe -Force }

Write-Host "Сборка билда..." -ForegroundColor Cyan
$log = Invoke-Unity 'Game.Gameplay.EditorTools.Builder.BuildWindows' 'build.log'

if (-not (Test-Path $exe)) {
    Write-Host "Билд не собрался. Лог: $log" -ForegroundColor Red
    Select-String -Path $log -Pattern 'error CS|\[Builder\]|BuildFailedException' |
        Select-Object -First 15 | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
    exit 1
}

$size = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 0)
Write-Host "ГОТОВО: $exe (каталог $size МБ)" -ForegroundColor Green
Write-Host "Отдавать ВЕСЬ каталог Build\Windows — exe без _Data не запустится." -ForegroundColor Yellow
exit 0
