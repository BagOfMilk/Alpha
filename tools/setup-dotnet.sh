#!/bin/bash
# Ставит .NET SDK, если его нет. Идемпотентен: повторный запуск ничего не ломает.
#
# Зачем: ядро Game.Core — чистый C# без зависимостей от движка, поэтому его можно
# компилировать и тестировать без Unity. Это даёт цикл правки в секунды вместо
# «открой редактор и посмотри».
set -euo pipefail

DOTNET_DIR="${DOTNET_INSTALL_DIR:-/usr/local/dotnet}"
CHANNEL="8.0"

if command -v dotnet >/dev/null 2>&1; then
    echo "dotnet уже установлен: $(dotnet --version)"
    exit 0
fi

if [ -x "$DOTNET_DIR/dotnet" ]; then
    echo "dotnet найден в $DOTNET_DIR: $("$DOTNET_DIR/dotnet" --version)"
    exit 0
fi

echo "Ставлю .NET SDK $CHANNEL в $DOTNET_DIR ..."

# Официальный скрипт — основной путь: не трогает системные пакеты и не зависит
# от свежести метаданных apt (они в облачных образах часто протухшие).
if curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh; then
    bash /tmp/dotnet-install.sh --channel "$CHANNEL" --install-dir "$DOTNET_DIR" --no-path
else
    echo "Скрипт установки недоступен, пробую apt ..."
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y dotnet-sdk-8.0
fi

"$DOTNET_DIR/dotnet" --version
echo "Готово."
