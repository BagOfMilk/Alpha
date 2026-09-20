#!/bin/bash
# Прогон тестов ядра без Unity. Основной способ проверки перед пушем.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
[ -x /usr/local/dotnet/dotnet ] && export PATH="/usr/local/dotnet:$PATH" DOTNET_ROOT="/usr/local/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Сборка ВСЕГО решения отдельным шагом: dotnet test собирает только тестовый
# проект и его зависимости, поэтому Alpha.Sim и Alpha.Play могли сломаться
# незамеченными (так и вышло 20.09.2026 при перестройке модели персонажа —
# харнес не компилировался, а тесты были зелёные).
dotnet build tools/Alpha.Headless.sln

exec dotnet test tools/Alpha.Headless.sln --no-build "$@"
