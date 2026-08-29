#!/bin/bash
# Прогон тестов ядра без Unity. Основной способ проверки перед пушем.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
[ -x /usr/local/dotnet/dotnet ] && export PATH="/usr/local/dotnet:$PATH" DOTNET_ROOT="/usr/local/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
exec dotnet test tools/Alpha.Headless.sln "$@"
