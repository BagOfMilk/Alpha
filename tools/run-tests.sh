#!/bin/bash
# Прогін тестів ядра без Unity. Основний спосіб перевірки перед пушем.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
[ -x /usr/local/dotnet/dotnet ] && export PATH="/usr/local/dotnet:$PATH" DOTNET_ROOT="/usr/local/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Збирання ВСЬОГО рішення окремим кроком: dotnet test збирає лише тестовий
# проєкт і його залежності, тому Alpha.Sim і Alpha.Play могли зламатися
# непомітно (так і сталося 20.09.2026 під час перебудови моделі персонажа —
# харнес не компілювався, а тести були зелені).
dotnet build tools/Alpha.Headless.sln

exec dotnet test tools/Alpha.Headless.sln --no-build "$@"
