#!/bin/bash
# SessionStart-хук: готовит облачную сессию к работе с проектом.
#
# Ставит .NET SDK и восстанавливает пакеты, чтобы ассистент мог сразу
# компилировать ядро и гонять тесты, а не просить владельца открыть Unity.
set -euo pipefail

# На машине владельца проекта не вмешиваемся — там всё уже настроено.
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

# Прогреваем NuGet-кэш, чтобы первый dotnet test не ждал восстановления.
dotnet restore "$PROJECT_DIR/tools/Game.Tests.Headless/Game.Tests.EditMode.csproj" >/dev/null 2>&1 || true

# Переменные переживают хук и достаются всей сессии.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    {
        echo "export PATH=\"$DOTNET_DIR:\$PATH\""
        echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
        echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
        echo "export DOTNET_NOLOGO=1"
    } >> "$CLAUDE_ENV_FILE"
fi

echo "Окружение готово: $(dotnet --version 2>/dev/null || "$DOTNET_DIR/dotnet" --version)"
