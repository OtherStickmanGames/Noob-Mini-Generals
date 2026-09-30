#!/bin/bash
# Проверочная компиляция скриптов проекта без открытия Unity (для ИИ-сессий и CI).
#
# Компилирует Roslyn'ом из любого установленного Unity 6 две сборки, как это делает Unity:
#   game   — все .cs вне папок Editor (Assembly-CSharp);
#   editor — все .cs в папках Editor (Assembly-CSharp-Editor), со ссылкой на game.
# Ссылки: DLL движка из этого Unity 6 + DLL пакетов из Library/ScriptAssemblies
# (их собирает Unity 2022.3 автора при открытии проекта — без них проверка не работает).
# Модули движка, которые в Unity 6 встроены, а в 2022.3 — пакеты (Mathematics, Burst,
# Collections), исключены, чтобы не было конфликта типов.
#
# Ловит синтаксис, опечатки в именах, неверные сигнатуры и типы. НЕ ловит отличия API
# Unity 6 от 2022.3 (редко, но бывает) и ничего не говорит о поведении в игре.
#
# Запуск из корня проекта (Git Bash):  bash Tools/compile-check.sh
# Путь к Unity можно задать: UNITY_DATA="C:/Program Files/Unity/Hub/Editor/<версия>/Editor/Data"

set -u
cd "$(dirname "$0")/.."
P="$(pwd -W 2>/dev/null || pwd)"

if [ -z "${UNITY_DATA:-}" ]; then
    for d in "C:/Program Files/Unity/Hub/Editor"/6000*/Editor/Data; do
        [ -d "$d" ] && UNITY_DATA="$d"
    done
fi
D="${UNITY_DATA:-}"
CSC=$(ls "$D"/DotNetSdk/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | head -1)
if [ -z "$D" ] || [ -z "$CSC" ]; then
    echo "Не найден Unity 6 с Roslyn (задайте UNITY_DATA)"; exit 2
fi
if [ ! -d Library/ScriptAssemblies ]; then
    echo "Нет Library/ScriptAssemblies — проект ещё не открывался в Unity"; exit 2
fi

OUT="${TMPDIR:-${TEMP:-/tmp}}/noob-generals-compile-check"
mkdir -p "$OUT"
NEWTONSOFT=$(ls Library/PackageCache/com.unity.nuget.newtonsoft-json@*/Runtime/Newtonsoft.Json.dll 2>/dev/null | head -1)

rsp() {
    local kind=$1
    echo "-nologo -preferreduilang:en -target:library -langversion:9 -unsafe -nowarn:0169,0414,0649,0618,0105"
    echo "-r:\"$D/NetStandard/ref/2.1.0/netstandard.dll\""
    for f in "$D"/Managed/UnityEngine/UnityEngine*.dll; do
        case "$f" in *MathematicsModule*|*BurstModule*|*CollectionsModule*) continue;; esac
        echo "-r:\"$f\""
    done
    [ -n "$NEWTONSOFT" ] && echo "-r:\"$P/$NEWTONSOFT\""
    for f in Library/ScriptAssemblies/*.dll; do
        case "$f" in
            *Assembly-CSharp*|*CodeGen*) ;;
            *Editor*) [ "$kind" = editor ] && echo "-r:\"$P/$f\"";;
            *) echo "-r:\"$P/$f\"";;
        esac
    done
    if [ "$kind" = game ]; then
        echo "-out:\"$OUT/Game.dll\""
        find Assets -name "*.cs" -not -path "*/Editor/*" | awk -v p="$P" '{print "\"" p "/" $0 "\""}'
    else
        for f in "$D"/Managed/UnityEngine/UnityEditor*.dll; do echo "-r:\"$f\""; done
        echo "-define:UNITY_EDITOR -out:\"$OUT/GameEditor.dll\" -r:\"$OUT/Game.dll\""
        find Assets -name "*.cs" -path "*/Editor/*" | awk -v p="$P" '{print "\"" p "/" $0 "\""}'
    fi
}

status=0
for kind in game editor; do
    rsp $kind > "$OUT/$kind.rsp"
    "$D/DotNetSdk/dotnet.exe" "$CSC" "@$OUT/$kind.rsp" > "$OUT/$kind.log" 2>&1
    errors=$(grep -c "error CS" "$OUT/$kind.log")
    echo "== $kind: ошибок $errors"
    grep "error CS" "$OUT/$kind.log" | head -40
    # Предупреждения — только в своём коде
    grep "warning CS" "$OUT/$kind.log" | grep -E "Assets.(Gameplay|VoxelArena)" | head -20
    [ "$errors" -gt 0 ] && status=1
    [ "$kind" = game ] && [ "$errors" -gt 0 ] && break
done
exit $status
