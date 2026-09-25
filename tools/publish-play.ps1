# Збирає текстовий зріз першої години в теку, яку можна віддати тестеру.
#
# Навіщо self-contained: у тестера немає .NET SDK, і ставити його він не мусить.
# Виходить тека Build/Play з exe і play.cmd — подвійний клік, і граєш.
#
# Використання:  powershell -File tools/publish-play.ps1
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
$out = Join-Path $proj "Build\Play"

# На цій машині в PATH лежить dotnet БЕЗ SDK (C:\Program Files\dotnet — лише
# рантайм), а SDK стоїть у профілі користувача. Перевіряти наявність команди
# недостатньо: вона є і все одно нічого не збере.
$local = Join-Path $env:USERPROFILE ".dotnet"
if (Test-Path (Join-Path $local "dotnet.exe")) { $env:PATH = "$local;$env:PATH" }

$sdks = & dotnet --list-sdks 2>$null
if (-not $sdks) {
    Write-Host "dotnet SDK не знайдено (рантайму замало — потрібен саме SDK)." -ForegroundColor Red
    Write-Host "Поставити: https://dot.net/v1/dotnet-install.ps1 -Channel 8.0" -ForegroundColor Yellow
    exit 1
}

Write-Host "Перевірка тестами перед збиранням..." -ForegroundColor Cyan
& dotnet build (Join-Path $proj "tools\Alpha.Headless.sln") -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Рішення не збирається." -ForegroundColor Red; exit 1 }
& dotnet test (Join-Path $proj "tools\Alpha.Headless.sln") --no-build -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Тести червоні — не віддаємо." -ForegroundColor Red; exit 1 }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

Write-Host "Публікація зрізу ($Runtime, без залежностей)..." -ForegroundColor Cyan
& dotnet publish (Join-Path $proj "tools\Alpha.Play\Alpha.Play.csproj") `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Публікація не вдалася." -ForegroundColor Red; exit 1 }

# Подвійний клік: вікно консолі має пережити кінець прогону, інакше підсумок
# п'ятої доби майне і зникне.
$cmd = @"
@echo off
chcp 65001 > nul
title Alpha - перша година
"%~dp0Alpha.Play.exe" %*
echo.
echo Натисни будь-яку клавішу, щоб закрити вікно.
pause > nul
"@
Set-Content -Path (Join-Path $out "play.cmd") -Value $cmd -Encoding UTF8

$readme = @"
Alpha — перша ігрова година (текстова збірка)

Запуск: подвійний клік по play.cmd

Що це. П'ять ігрових діб: сцена відкриття, вузол на перевалі, події в
місті, коротка вилазка, підсумок. Ти ухвалюєш рішення — бачиш обидва шляхи
і їхні пороги ДО вибору, вирішуєш, патрулювати вночі чи спати.

Картинки немає навмисно. Якщо петля не тримає в тексті, картинка її не врятує.
Дивимося саме на рішення: чи зрозумілі ставки, чи шкода людей, чи хочеться
грати далі.

Автопрогін без участі (подивитися, як воно йде саме):
    play.cmd --auto

Збереження і продовження:
    play.cmd --save save1.txt
    play.cmd --load save1.txt

Що надіслати назад: де застряг, що було незрозуміло, в який момент стало
нудно, і назви трьох людей з громади — чим вони одне від одного відрізняються.
"@
Set-Content -Path (Join-Path $out "ЧИТАЙ.txt") -Value $readme -Encoding UTF8

$size = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 0)
Write-Host "ГОТОВО: $out ($size МБ)" -ForegroundColor Green
Write-Host "Віддавати тестеру ВСЮ теку. Запуск — play.cmd" -ForegroundColor Yellow
exit 0
