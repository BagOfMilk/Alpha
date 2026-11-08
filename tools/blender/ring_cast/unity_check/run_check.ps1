# Перевірка імпорту FBX персонажів як Humanoid у тимчасовому проєкті Unity. Вікон не відкриває, основного проєкту не чіпає.
#   powershell -File tools/blender/ring_cast/unity_check/run_check.ps1 -Work <тимчасова тека>
param([Parameter(Mandatory=$true)][string]$Work,
      [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\Unity.exe")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here "..\..\..\..")).Path
$proj = Join-Path $Work "UnityCheck"
if (-not (Test-Path (Join-Path $proj "Assets"))) {
    & $Unity -batchmode -quit -nographics -createProject $proj -logFile (Join-Path $Work "create.log") | Out-Null
}
$dstC = Join-Path $proj "Assets\Cast"
$dstE = Join-Path $proj "Assets\Editor"
New-Item -ItemType Directory -Force $dstC, $dstE | Out-Null
Copy-Item (Join-Path $here "CheckAvatar.cs") $dstE -Force
foreach ($c in "stalevyi","dzhura","baron","hammer","komet","vdova","novachka","lina") {
    New-Item -ItemType Directory -Force (Join-Path $dstC $c) | Out-Null
    Copy-Item (Join-Path $repo "Assets\Art\Ring\Cast\$c\$c.fbx") (Join-Path $dstC $c) -Force
}
New-Item -ItemType Directory -Force (Join-Path $dstC "Anim") | Out-Null
Copy-Item (Join-Path $repo "Assets\Art\Ring\Cast\Anim\RING_Cast_M.fbx"), (Join-Path $repo "Assets\Art\Ring\Cast\Anim\RING_Cast_F.fbx") (Join-Path $dstC "Anim") -Force
$env:RING_REPORT = Join-Path $Work "avatar_report.txt"
$p = Start-Process -FilePath $Unity -ArgumentList @("-batchmode","-quit","-nographics","-projectPath","`"$proj`"","-executeMethod","CheckAvatar.Run","-logFile","`"$(Join-Path $Work 'check.log')`"") -PassThru -Wait
"Unity exit code: $($p.ExitCode)"
if (Test-Path $env:RING_REPORT) { Get-Content $env:RING_REPORT -Encoding UTF8 }
