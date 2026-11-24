#!/usr/bin/env bash
set -euo pipefail
saida="$(realpath -m "${1:-artifacts/revisao}")"
mkdir -p "$saida"
dotnet restore -m:1
dotnet build -c Release --no-restore -m:1 -p:UseSharedCompilation=false
if [[ "${FORMATO_PASTA:-0}" == 1 ]]; then
  mapfile -t cs < <(find src tests benchmarks -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*')
  dotnet format whitespace --folder --verify-no-changes --include "${cs[@]}"
else
  dotnet format --verify-no-changes --no-restore
fi
dotnet test -c Release --no-build -m:1 --logger trx --results-directory "$saida"
dotnet publish src/Api -c Release --no-build -m:1 -p:UseSharedCompilation=false -o "$saida/publicado"
