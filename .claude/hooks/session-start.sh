#!/bin/bash
# SessionStart-хук: готує хмарну сесію до роботи з проєктом.
#
# Ставить .NET SDK і відновлює пакети, щоб асистент міг одразу
# компілювати ядро й ганяти тести, а не просити власника відкрити Unity.
set -euo pipefail

# На машині власника проєкту не втручаємося — там усе вже налаштовано.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
    exit 0
fi

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
DOTNET_DIR="/usr/local/dotnet"

bash "$PROJECT_DIR/tools/setup-dotnet.sh"

if ! command -v dotnet >/dev/null 2>&1; then
    export PATH="$DOTNET_DIR:$PATH"
    export DOTNET_ROOT="$DOTNET_DIR"
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# Прогріваємо кеш NuGet, щоб перший dotnet test не чекав відновлення.
dotnet restore "$PROJECT_DIR/tools/Game.Tests.Headless/Game.Tests.EditMode.csproj" >/dev/null 2>&1 || true

# Змінні переживають хук і дістаються всій сесії.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    {
        echo "export PATH=\"$DOTNET_DIR:\$PATH\""
        echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
        echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
        echo "export DOTNET_NOLOGO=1"
    } >> "$CLAUDE_ENV_FILE"
fi

echo "Оточення готове:$(dotnet --version 2>/dev/null || "$DOTNET_DIR/dotnet" --version)"
