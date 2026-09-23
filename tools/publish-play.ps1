# Собирает текстовый срез первого часа в папку, которую можно отдать тестеру.
#
# Зачем self-contained: у тестера нет .NET SDK и ставить его он не должен.
# Получается каталог Build/Play с exe и play.cmd — двойной клик, и играешь.
#
# Использование:  powershell -File tools/publish-play.ps1
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
$out = Join-Path $proj "Build\Play"

# На этой машине в PATH лежит dotnet БЕЗ SDK (C:\Program Files\dotnet — только
# рантайм), а SDK стоит в профиле пользователя. Проверять наличие команды
# недостаточно: она есть и всё равно ничего не соберёт.
$local = Join-Path $env:USERPROFILE ".dotnet"
if (Test-Path (Join-Path $local "dotnet.exe")) { $env:PATH = "$local;$env:PATH" }

$sdks = & dotnet --list-sdks 2>$null
if (-not $sdks) {
    Write-Host "dotnet SDK не найден (рантайма мало — нужен именно SDK)." -ForegroundColor Red
    Write-Host "Поставить: https://dot.net/v1/dotnet-install.ps1 -Channel 8.0" -ForegroundColor Yellow
    exit 1
}

Write-Host "Проверка тестами перед сборкой..." -ForegroundColor Cyan
& dotnet build (Join-Path $proj "tools\Alpha.Headless.sln") -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Решение не собирается." -ForegroundColor Red; exit 1 }
& dotnet test (Join-Path $proj "tools\Alpha.Headless.sln") --no-build -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Тесты красные — не отдаём." -ForegroundColor Red; exit 1 }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

Write-Host "Публикация среза ($Runtime, без зависимостей)..." -ForegroundColor Cyan
& dotnet publish (Join-Path $proj "tools\Alpha.Play\Alpha.Play.csproj") `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Публикация не удалась." -ForegroundColor Red; exit 1 }

# Двойной клик: окно консоли должно пережить конец прогона, иначе итог
# на пятые сутки мелькнёт и исчезнет.
$cmd = @"
@echo off
chcp 65001 > nul
title Alpha - первый час
"%~dp0Alpha.Play.exe" %*
echo.
echo Нажми любую клавишу, чтобы закрыть окно.
pause > nul
"@
Set-Content -Path (Join-Path $out "play.cmd") -Value $cmd -Encoding UTF8

$readme = @"
Alpha — первый игровой час (текстовая сборка)

Запуск: двойной клик по play.cmd

Что это. Пять игровых суток: сцена открытия, узел на перевале, события в
городе, короткая вылазка, итог. Ты принимаешь решения — видишь оба пути и
их пороги ДО выбора, решаешь, патрулировать ночью или спать.

Картинки нет намеренно. Если петля не держит в тексте, картинка её не спасёт.
Смотрим именно на решения: понятны ли ставки, жалко ли людей, хочется ли
играть дальше.

Автопрогон без участия (посмотреть, как оно идёт само):
    play.cmd --auto

Сохранение и продолжение:
    play.cmd --save save1.txt
    play.cmd --load save1.txt

Что прислать обратно: где застрял, что было непонятно, в какой момент стало
скучно, и назови трёх людей из общины — чем они друг от друга отличаются.
"@
Set-Content -Path (Join-Path $out "ЧИТАЙ.txt") -Value $readme -Encoding UTF8

$size = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 0)
Write-Host "ГОТОВО: $out ($size МБ)" -ForegroundColor Green
Write-Host "Отдавать тестеру ВЕСЬ каталог. Запуск — play.cmd" -ForegroundColor Yellow
exit 0
