"""Consumidor de v1 executado contra a aplicação que oferece v2."""
from contextlib import closing
import http.client
import json
import sys

from carga import PEDIDO, request

with closing(http.client.HTTPConnection("127.0.0.1", int(sys.argv[1]) if len(sys.argv) > 1 else 5080, timeout=5)) as conn:
    status, body = request(conn, "POST", "/pedidos", PEDIDO)
    assert status == 201, (status, body)
    pedido = json.loads(body)
    for prefixo in ("", "/api/v1"):
        status, body = request(conn, "GET", f"{prefixo}/pedidos/{pedido['id']}")
        atual = json.loads(body)
        assert status == 200 and atual == pedido and type(atual["totalCentavos"]) is int
        print(f"GET {prefixo}/pedidos/{pedido['id']} -> {status} {body.decode()}")
    status, body = request(conn, "GET", f"/api/v2/pedidos/{pedido['id']}")
    v2 = json.loads(body)
    assert status == 200 and v2["total"] * 100 == pedido["totalCentavos"] and v2["moeda"] == "BRL"
    assert "totalCentavos" not in v2 and v2["estado"] == "recebido"
    print(f"v2 -> {status} {body.decode()}")
    status, body = request(conn, "GET", f"/api/v3/pedidos/{pedido['id']}")
    assert status == 400
    print(f"v3 -> {status} {body.decode()}")
