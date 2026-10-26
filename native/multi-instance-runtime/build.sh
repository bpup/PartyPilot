#!/bin/sh
set -eu

if [ -z "${LLVM_MINGW_ROOT:-}" ]; then
  echo 'Set LLVM_MINGW_ROOT to an llvm-mingw macOS toolchain.' >&2
  exit 2
fi

compiler="$LLVM_MINGW_ROOT/bin/x86_64-w64-mingw32-clang"
root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
vendor="$root/../private-slot-runtime/vendor/minhook"
out="$root/../../release/native"
build="$root/.build"
mkdir -p "$out" "$build"

"$compiler" -std=c11 -O2 -Wall -Wextra -Werror -I"$vendor/include" \
  -c "$root/runtime.c" -o "$build/runtime.o"
for source in buffer hook trampoline; do
  "$compiler" -std=c11 -O2 -w -I"$vendor/include" -I"$vendor/src" -I"$vendor/src/hde" \
    -c "$vendor/src/$source.c" -o "$build/$source.o"
done
"$compiler" -std=c11 -O2 -w -I"$vendor/src/hde" \
  -c "$vendor/src/hde/hde64.c" -o "$build/hde64.o"
"$compiler" -shared -o "$out/PartyPilot.MultiInstance.dll" \
  "$build/runtime.o" "$build/buffer.o" "$build/hook.o" \
  "$build/trampoline.o" "$build/hde64.o"
"$compiler" -municode -std=c11 -O2 -Wall -Wextra -Werror \
  "$root/windows_smoke.c" -o "$out/PartyPilot.MultiInstance.Smoke.exe"
echo "Built $out/PartyPilot.MultiInstance.dll"
