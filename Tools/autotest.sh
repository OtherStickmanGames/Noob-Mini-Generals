#!/usr/bin/env bash
# Автотест боя: копия проекта рядом (проект открыт в редакторе — его заблокирован), сборка
# Windows-билда из копии, прогон боя ИИ против ИИ. Отчёт и кадры — в Builds/AutoTest/Report.
#
#   bash Tools/autotest.sh [секунд игрового времени=600] [ускорение=3] [--no-build]
#   BONUS=5 bash Tools/autotest.sh … — каждой стороне +5 базового ресурса в секунду (дойти до техники)
#   PEACE=400 bash Tools/autotest.sh … — первые 400 с без волн атаки (стороны только строятся)
#
# Первый запуск копирует Library (~2 ГБ), дальше — только изменения.
set -euo pipefail

SECONDS_GAME="${1:-600}"
SCALE="${2:-3}"
BONUS="${BONUS:-0}"
PEACE="${PEACE:-0}"
NO_BUILD="${3:-}"

ROOT="$(cd "$(dirname "$0")/.." && pwd -W)"
COPY="$(cd "$ROOT/.." && pwd -W)/NMG-AutoTest"
UNITY="${UNITY_EDITOR_2022:-E:/W10/UNITY/2022.3.62f3/Editor/Unity.exe}"
BUILD="$ROOT/Builds/AutoTest"
REPORT="$BUILD/Report"

win() { echo "$1" | sed 's#/#\\#g'; }

if [ "$NO_BUILD" != "--no-build" ]; then
    echo "== копия проекта: $COPY"
    # Library — только в первый раз: дальше копия ведёт свою (Library открытого проекта меняется
    # на ходу, и с ней база ассетов копии оказывается в чужом, полуобновлённом состоянии)
    DIRS="Assets Packages ProjectSettings"
    [ -d "$COPY/Library" ] || DIRS="$DIRS Library"
    for dir in $DIRS; do
        # robocopy: код < 8 — успех
        robocopy "$(win "$ROOT/$dir")" "$(win "$COPY/$dir")" //MIR //NFL //NDL //NJH //NJS //NP //R:1 //W:1 \
            //XD "$(win "$ROOT/Library/Bee")" "$(win "$ROOT/Library/BuildPlayerData")" >/dev/null || [ $? -lt 8 ]
    done

    echo "== импорт и компиляция в копии"
    # Отдельный запуск: новые скрипты импортируются и компилируются до вызова метода сборки
    # (иначе -executeMethod может не найти только что добавленный класс)
    "$UNITY" -batchmode -quit -projectPath "$COPY" -logFile "$BUILD/import.log" || true

    echo "== сборка билда"
    rm -f "$BUILD/build.log"
    STAMP=$(date +%s)
    "$UNITY" -batchmode -quit -projectPath "$COPY" -executeMethod Generals.AutoTestBuild.Build \
        -autotestBuildPath "$BUILD/NMG.exe" -logFile "$BUILD/build.log" || {
        echo "сборка не удалась, см. $BUILD/build.log"
        grep -E "error CS|Error|Exception" "$BUILD/build.log" | head -30
        exit 1
    }
    # Unity может вернуть 0 и при неудаче — проверяем сам билд: свежий и с отметкой об успехе
    if ! grep -qF "[AutoTestBuild] Succeeded" "$BUILD/build.log" || [ "$(stat -c %Y "$BUILD/NMG_Data/Managed/Assembly-CSharp.dll")" -lt "$STAMP" ]; then
        echo "сборка не удалась (старый билд не запускаю), см. $BUILD/build.log"
        grep -E "error CS|executeMethod|Exception" "$BUILD/build.log" | head -30
        exit 1
    fi
fi

echo "== бой: $SECONDS_GAME с игрового времени, ×$SCALE"
rm -rf "$REPORT"
mkdir -p "$REPORT"
"$BUILD/NMG.exe" -autotest -autotestSeconds "$SECONDS_GAME" -autotestScale "$SCALE" -autotestBonus "$BONUS" -autotestPeace "$PEACE" -autotestOut "$REPORT" \
    -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$REPORT/player.log"

echo "== отчёт: $REPORT/report.txt"
head -60 "$REPORT/report.txt"
