#!/usr/bin/env bash
set -euo pipefail
destino="$(realpath -m "${1:-artifacts/gates}")"
mkdir -p "$destino"
fonte="src/Domain/GateProbe.cs"
teste="tests/Tests/GateProbe.cs"
trap 'rm -f "$fonte" "$teste"' EXIT

cat > "$fonte" <<'CS'
internal static class GateProbe
{
    public static void Repassar()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException ex)
        {
            throw ex;
        }
    }
}
CS
if bash scripts/revisao.sh "$destino/analisador" > "$destino/analisador.log" 2>&1; then
    echo "A violação CA2200 deveria bloquear a revisão." >&2
    exit 1
fi
grep -m 1 'error CA2200' "$destino/analisador.log"
test ! -d "$destino/analisador/publicado"
rm "$fonte"

cat > "$teste" <<'CS'
namespace Tests;

public class GateProbe
{
    [Fact]
    public void FalhaIntencional() => Assert.Fail("Prova do gate de testes");
}
CS
if bash scripts/revisao.sh "$destino/teste" > "$destino/teste.log" 2>&1; then
    echo "O teste propositalmente vermelho deveria bloquear a revisão." >&2
    exit 1
fi
grep -m 1 'Prova do gate de testes' "$destino/teste.log"
test ! -d "$destino/teste/publicado"
rm "$teste"
