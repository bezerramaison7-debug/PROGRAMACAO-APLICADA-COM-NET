"""Cria pedidos e dispara uma rajada controlada de trabalho concorrente."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
import http.client
import json
import math
from time import perf_counter, sleep

from carga import PEDIDO, request


def enviar(porta, metodo, rota, corpo=None):
    with closing(http.client.HTTPConnection("127.0.0.1", porta, timeout=5)) as conn:
        return request(conn, metodo, rota, corpo)


def executar(porta):
    ids = []
    for _ in range(96):
        status, body = enviar(porta, "POST", "/pedidos", PEDIDO)
        assert status == 201, (status, body)
        ids.append(json.loads(body)["id"])
    inicio = perf_counter()

    def processar(id_pedido):
        t0 = perf_counter()
        status, _ = enviar(porta, "POST", f"/pedidos/{id_pedido}/processamento")
        return status, (perf_counter() - t0) * 1000

    pico = 0
    with ThreadPoolExecutor(max_workers=24) as pool:
        futures = [pool.submit(processar, p) for p in ids]
        while True:
            _, body = enviar(porta, "GET", "/metricas/fila")
            estado = json.loads(body)
            pico = max(pico, estado["profundidade"])
            if all(f.done() for f in futures) and estado["processados"] + estado["falhas"] == estado["aceitos"]:
                break
            if perf_counter() - inicio > 15:
                raise TimeoutError(estado)
            sleep(.002)
    respostas = [f.result() for f in futures]
    latencias = sorted(duracao for _, duracao in respostas)
    assert all(status in (202, 503) for status, _ in respostas)
    assert estado["falhas"] == 0 and estado["aceitos"] + estado["rejeitados"] == len(ids)
    assert sum(status == 503 for status, _ in respostas) == estado["rejeitados"]
    print(json.dumps({**estado, "profundidade_pico_amostrada": pico,
                      "processados_s": round(estado["processados"] / (perf_counter() - inicio), 2),
                      "post_p95_ms": round(latencias[math.ceil(.95 * len(latencias)) - 1], 3)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=5080)
    executar(parser.parse_args().port)
