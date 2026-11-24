"""Carga fechada, com aquecimento e latência medida por requisição."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
import http.client
import json
import math
import os
from pathlib import Path
import socket
from time import perf_counter

PEDIDO = json.dumps({"regiao": "SE", "prioritario": False, "itens": [
    {"sku": "CARGA", "quantidade": 2, "precoCentavos": 150}
]})
HEADERS = {"Content-Type": "application/json", "Authorization": "Bearer " + os.environ.get("HUB_TOKEN", "")}


def request(conn, verb, path, body=None):
    conn.request(verb, path, body, HEADERS)
    resposta = conn.getresponse()
    return resposta.status, resposta.read()


def executar(args):
    with closing(http.client.HTTPConnection(args.host, args.port, timeout=5)) as conn:
        status, dados = request(conn, "POST", "/pedidos", PEDIDO)
        if status != 201:
            raise RuntimeError(f"Falha no preparo: {status} {dados}")
        rota = "/pedidos/" + json.loads(dados)["id"]

    def janela(segundos):
        inicio = perf_counter()
        fim = inicio + segundos

        def worker(indice):
            amostra = []
            conn = http.client.HTTPConnection(args.host, args.port, timeout=2)
            try:
                while perf_counter() < fim:
                    escrita = indice < args.escritores
                    verbo, caminho, body = ("POST", "/pedidos", PEDIDO) if escrita else ("GET", rota, None)
                    t0 = perf_counter()
                    try:
                        status, _ = request(conn, verbo, caminho, body)
                    except (TimeoutError, socket.timeout):
                        status = "timeout"
                        conn.close()
                    except (OSError, http.client.HTTPException):
                        status = "transporte"
                        conn.close()
                    amostra.append([verbo, status, round((perf_counter() - t0) * 1000, 4)])
            finally:
                conn.close()
            return amostra

        with ThreadPoolExecutor(max_workers=args.concorrencia) as pool:
            dados = [linha for lote in pool.map(worker, range(args.concorrencia)) for linha in lote]
        return dados, perf_counter() - inicio

    aquecimento, _ = janela(args.aquecimento)
    dados, duracao = janela(args.duracao)
    Path(args.saida).with_suffix(".samples.json").write_text(json.dumps(dados))

    def resumo(rows):
        ms = sorted(r[2] for r in rows)
        if not ms:
            return {"n": 0}
        return {
            "n": len(ms),
            "req_s": round(len(ms) / duracao, 2),
            **{f"p{p}_ms": ms[max(0, math.ceil(p * len(ms) / 100) - 1)] for p in (50, 95, 99)},
            "erros": sum(r[1] not in (200, 201) for r in rows),
            "timeouts": sum(r[1] == "timeout" for r in rows),
        }

    return {"concorrencia": args.concorrencia, "escritores": args.escritores,
            "aquecimento_n": len(aquecimento), "duracao_s": round(duracao, 3),
            "geral": resumo(dados), **{v: resumo([r for r in dados if r[0] == v]) for v in ("GET", "POST")}}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5080)
    parser.add_argument("--concorrencia", type=int, default=6)
    parser.add_argument("--escritores", type=int, default=1)
    parser.add_argument("--aquecimento", type=int, default=2)
    parser.add_argument("--duracao", type=int, default=5)
    parser.add_argument("--saida", required=True)
    params = parser.parse_args()
    result = json.dumps(executar(params), indent=2)
    Path(params.saida).write_text(result)
    print(result)
