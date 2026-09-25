# Збирає Unity-білд тестової збірки «усі механіки одразу» (Поправка №7):
# ОДНА сцена Game.unity (титул → створення → хаб → фінал), перезібрана
# кодом наново під час кожного запуску — сцени немає в репозиторії (посилання на
# скрипти за GUID з .meta створюються локально), тому її завжди збирають
# перед білдом, а не покладаються на закомічений файл (R19).
#
# Текстовий зріз віддається через tools/publish-play.ps1 і нічого від Unity не
# потребує. Цей скрипт — про грабельний білд.
#
# Використання:  powershell -File tools/build-unity.ps1
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\Unity.exe",
    [switch]$SkipScene
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
$out = Join-Path $proj "Build\Windows"
$exe = Join-Path $out "Alpha.exe"

if (-not (Test-Path $UnityPath)) {
    Write-Host "Unity не знайдено: $UnityPath" -ForegroundColor Red
    Write-Host "Передай шлях: -UnityPath <...\Unity.exe>" -ForegroundColor Yellow
    exit 1
}

New-Item -ItemType Directory -Force -Path (Join-Path $proj "Logs") | Out-Null

function Invoke-Unity([string]$method, [string]$logName) {
    $log = Join-Path $proj "Logs\$logName"
    # Unity від'єднується під час звичайного запуску — потрібні PassThru і очікування.
    $p = Start-Process -FilePath $UnityPath -PassThru -ArgumentList @(
        '-quit', '-batchmode', '-projectPath', "`"$proj`"",
        '-executeMethod', $method, '-logFile', "`"$log`"")
    $p.WaitForExit()
    return $log
}

if (-not $SkipScene) {
    # Сцени в репозиторії немає навмисно: вона посилається на скрипти за GUID з
    # .meta, а ті створюються локально. Тому спершу збираємо її.
    #
    # Імпорт Kenney (KenneyImportSettings) тут НЕ перезапускається — це
    # окремий повільний крок, потрібний лише тоді, коли змінюються самі вихідні
    # файли набору, а не під час кожного перезбирання сцени/білда.
    Write-Host "Збирання сцени «Гра»..." -ForegroundColor Cyan
    $log = Invoke-Unity 'Game.Gameplay.EditorTools.GameSceneBuilder.Build' 'scene.log'
    if (-not (Test-Path (Join-Path $proj "Assets\Scenes\Game.unity"))) {
        Write-Host "Сцена не зібралася. Лог: $log" -ForegroundColor Red
        Select-String -Path $log -Pattern 'error CS|Exception' | Select-Object -First 10 |
            ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
        exit 1
    }
}

if (Test-Path $exe) { Remove-Item $exe -Force }

Write-Host "Збирання білда..." -ForegroundColor Cyan
$log = Invoke-Unity 'Game.Gameplay.EditorTools.Builder.BuildWindows' 'build.log'

if (-not (Test-Path $exe)) {
    Write-Host "Білд не зібрався. Лог: $log" -ForegroundColor Red
    Select-String -Path $log -Pattern 'error CS|\[Builder\]|BuildFailedException' |
        Select-Object -First 15 | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
    exit 1
}

$size = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 0)
Write-Host "ГОТОВО: $exe (тека $size МБ)" -ForegroundColor Green
Write-Host "Віддавати ВСЮ теку Build\Windows — exe без _Data не запуститься." -ForegroundColor Yellow
exit 0
