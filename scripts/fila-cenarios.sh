#!/usr/bin/env bash
set -euo pipefail
saida="$(realpath -m "${1:-artifacts/fila}")"
mkdir -p "$saida"
produto="$(realpath "${API_DLL:-src/Api/bin/Release/net10.0/Api.dll}")"
pid=""
db="$(mktemp -d)"
trap 'if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; fi; rm -rf "$db"' EXIT
export HUB_TOKEN="$(openssl rand -hex 16)"
for config in 2:1 16:4; do
  capacidade="${config%:*}"
  consumidores="${config#*:}"
  env "ConnectionStrings__Pedidos=Data Source=$db/$capacidade.db" \
    Fila__Capacidade="$capacidade" Fila__Consumidores="$consumidores" Auth__Operadores__carga="$HUB_TOKEN" \
    Logging__LogLevel__Default=Warning dotnet "$produto" --urls http://127.0.0.1:5084 \
    > "$saida/$capacidade-servidor.log" 2>&1 &
  pid=$!
  curl -fsS --retry 12 --retry-delay 1 --retry-connrefused http://127.0.0.1:5084/health > /dev/null
  python3 scripts/fila_carga.py --port 5084 | tee "$saida/$capacidade.json"
  kill "$pid"
  wait "$pid"
  pid=""
done
