#!/bin/bash
# Ставить .NET SDK, якщо його немає. Ідемпотентний: повторний запуск нічого не ламає.
#
# Навіщо: ядро Game.Core — чистий C# без залежностей від рушія, тому його можна
# компілювати й тестувати без Unity. Це дає цикл правки за секунди замість
# «відкрий редактор і подивись».
set -euo pipefail

DOTNET_DIR="${DOTNET_INSTALL_DIR:-/usr/local/dotnet}"
CHANNEL="8.0"

if command -v dotnet >/dev/null 2>&1; then
    echo "dotnet уже встановлено: $(dotnet --version)"
    exit 0
fi

if [ -x "$DOTNET_DIR/dotnet" ]; then
    echo "dotnet знайдено в $DOTNET_DIR: $("$DOTNET_DIR/dotnet" --version)"
    exit 0
fi

echo "Ставлю .NET SDK $CHANNEL у $DOTNET_DIR ..."

# Офіційний скрипт — основний шлях: не чіпає системні пакети й не залежить
# від свіжості метаданих apt (у хмарних образах вони часто застарілі).
if curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh; then
    bash /tmp/dotnet-install.sh --channel "$CHANNEL" --install-dir "$DOTNET_DIR" --no-path
else
    echo "Скрипт встановлення недоступний, пробую apt ..."
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y dotnet-sdk-8.0
fi

"$DOTNET_DIR/dotnet" --version
echo "Готово."
