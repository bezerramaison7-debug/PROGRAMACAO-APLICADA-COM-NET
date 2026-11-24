#!/usr/bin/env bash
set -euo pipefail
saida="$(realpath -m "${1:-artifacts/diagnostico}")"
mkdir -p "$saida"
produto="$(realpath "${API_DLL:-src/Api/bin/Release/net10.0/Api.dll}")"
pid=""
export HUB_TOKEN="$(openssl rand -hex 16)"
trap 'if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; fi' EXIT
for modo in normal compilada; do
  rm -f "$saida/$modo.db"
  compilada=false
  [[ "$modo" != compilada ]] || compilada=true
  env "ConnectionStrings__Pedidos=Data Source=$saida/$modo.db" \
    Diagnostico__ConsultaCompilada="$compilada" Auth__Operadores__carga="$HUB_TOKEN" \
    Logging__LogLevel__Default=Warning \
    dotnet "$produto" --urls http://127.0.0.1:5080 > "$saida/$modo-servidor.log" 2>&1 &
  pid=$!
  curl -fsS --retry 12 --retry-delay 1 --retry-connrefused http://127.0.0.1:5080/health > "$saida/$modo-health.json"
  if [[ "${PERFILAR:-0}" == 1 ]]; then
    dotnet-counters collect -p "$pid" --duration 00:00:08 --format csv \
      --counters System.Runtime,HubPedidos.Fila --output "$saida/$modo-counters" > "$saida/$modo-counters.log" 2>&1 &
    counters=$!
    dotnet-trace collect -p "$pid" --duration 00:00:07 --profile dotnet-sampled-thread-time \
      --output "$saida/$modo.nettrace" > "$saida/$modo-trace.log" 2>&1 &
    trace=$!
  fi
  python3 scripts/carga.py --saida "$saida/$modo.json"
  if [[ "${PERFILAR:-0}" == 1 ]]; then
    wait "$counters"
    wait "$trace"
    dotnet-trace report "$saida/$modo.nettrace" topN -n 25 > "$saida/$modo-top.txt"
  fi
  kill "$pid"
  wait "$pid"
  pid=""
done
