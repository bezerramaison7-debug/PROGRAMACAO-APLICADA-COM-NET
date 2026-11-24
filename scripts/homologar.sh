#!/usr/bin/env bash
set -euo pipefail
raiz="$PWD"
saida="$raiz/artifacts/homologacao"
mkdir -p "$saida"
revisao="$(git rev-parse HEAD)"
release="$raiz/artifacts/revisao/publicado"
db="$(mktemp -d)"
export HUB_TOKEN="$(openssl rand -hex 24)" HUB_TOKEN_OTHER="$(openssl rand -hex 24)"
# As chaves são efêmeras e não são gravadas nos artefatos.
export Auth__Operadores__carga="$HUB_TOKEN" Auth__Operadores__visitante="$HUB_TOKEN_OTHER"
env "ConnectionStrings__Pedidos=Data Source=$db/pedidos.db" Revisao="$revisao" \
  bash -c 'cd "$1"; exec dotnet Api.dll --urls http://127.0.0.1:5080' _ "$release" \
  > "$saida/servidor-${1:-smoke}.log" 2>&1 &
app=$!
trap 'kill "$app" 2>/dev/null || true; wait "$app" || true; rm -rf "$db"' EXIT
curl -fsS --retry 12 --retry-connrefused --retry-delay 1 http://127.0.0.1:5080/health > "$saida/health-${1:-smoke}.json"
python3 - "$saida/health-${1:-smoke}.json" "$revisao" <<'PY'
import json, sys
from pathlib import Path
assert json.loads(Path(sys.argv[1]).read_text()) == {"status": "Healthy", "revisao": sys.argv[2]}
print("Health Check validou a mesma revisão publicada: " + sys.argv[2])
PY
if [[ "${1:-smoke}" == ux ]]; then
  node scripts/ux.cjs "$saida/ux"
elif [[ "${1:-smoke}" == protocolo ]]; then
  python3 scripts/signalr_protocolo.py | tee "$saida/signalr-protocolo.log"
else
  python3 scripts/contrato.py | tee "$saida/contrato.log"
fi
