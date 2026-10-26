#!/bin/sh
set -eu

if [ -z "${LLVM_MINGW_ROOT:-}" ]; then
  echo 'Set LLVM_MINGW_ROOT to an official llvm-mingw macOS toolchain.' >&2
  exit 2
fi

compiler="$LLVM_MINGW_ROOT/bin/x86_64-w64-mingw32-clang"
root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
out="$root/../../release/native"
build="$root/.build"
mkdir -p "$out" "$build"

"$compiler" -std=c11 -O2 -Wall -Wextra -Werror -I"$root/vendor/minhook/include" \
  -c "$root/private_slots.c" -o "$build/private_slots.o"
"$compiler" -std=c11 -O2 -Wall -Wextra -Werror -I"$root/vendor/minhook/include" \
  -c "$root/runtime.c" -o "$build/runtime.o"
for source in buffer hook trampoline; do
  "$compiler" -std=c11 -O2 -w -I"$root/vendor/minhook/include" \
    -I"$root/vendor/minhook/src" -I"$root/vendor/minhook/src/hde" \
    -c "$root/vendor/minhook/src/$source.c" -o "$build/$source.o"
done
"$compiler" -std=c11 -O2 -w -I"$root/vendor/minhook/src/hde" \
  -c "$root/vendor/minhook/src/hde/hde64.c" -o "$build/hde64.o"
"$compiler" -shared -o "$out/PartyPilot.PrivateSlots.dll" \
  "$build/private_slots.o" "$build/runtime.o" \
  "$build/buffer.o" "$build/hook.o" "$build/trampoline.o" "$build/hde64.o"
"$compiler" -std=c11 -O2 -Wall -Wextra -Werror \
  "$root/windows_smoke.c" -o "$out/PartyPilot.PrivateSlots.Smoke.exe"
echo "Built $out/PartyPilot.PrivateSlots.dll"
