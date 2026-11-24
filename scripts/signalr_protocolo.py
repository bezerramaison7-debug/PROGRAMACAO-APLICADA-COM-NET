"""Três perfis simulados usando um cliente independente do protocolo SignalR."""
import asyncio
import http.client
from contextlib import closing
import json
import os
from urllib.parse import urlencode

from websockets.asyncio.client import connect
from carga import PEDIDO, HEADERS

PORT = int(os.environ.get("HUB_PORT", 5080))
A = os.environ["HUB_TOKEN"]
B = os.environ["HUB_TOKEN_OTHER"]
SEP = chr(30)


def requisitar(chave, metodo, caminho, corpo=None):
    headers = {**HEADERS, "Authorization": "Bearer " + chave}
    with closing(http.client.HTTPConnection("127.0.0.1", PORT, timeout=3)) as conexao:
        conexao.request(metodo, caminho, corpo, headers)
        resposta = conexao.getresponse()
        return resposta.status, resposta.read()


async def receber(ws, filtro):
    async with asyncio.timeout(5):
        while True:
            frame = await ws.recv()
            for trecho in frame.split(SEP):
                if trecho:
                    mensagem = json.loads(trecho)
                    if filtro(mensagem):
                        return mensagem


def url(chave):
    status, dados = requisitar(chave, "POST", "/hubs/pedidos/negotiate?negotiateVersion=1", "")
    assert status == 200
    conn = json.loads(dados)
    return f"ws://127.0.0.1:{PORT}/hubs/pedidos?" + urlencode(
        {"id": conn["connectionToken"], "access_token": chave}), conn["connectionId"]


async def handshake(ws):
    await ws.send('{"protocol":"json","version":1}' + SEP)
    assert await receber(ws, lambda m: "type" not in m) == {}


async def assinar(ws, id_pedido):
    await ws.send(json.dumps({"type": 1, "invocationId": "1", "target": "Assinar",
                              "arguments": [id_pedido]}) + SEP)
    return await receber(ws, lambda m: m.get("invocationId") == "1")


def novo_pedido():
    status, body = requisitar(A, "POST", "/pedidos", PEDIDO)
    assert status == 201
    return json.loads(body)["id"]


async def main():
    id_pedido = novo_pedido()
    a_url, _ = url(A)
    b_url, _ = url(B)
    async with connect(a_url) as a, connect(b_url) as b:
        await handshake(a)
        await handshake(b)
        assert "error" not in await assinar(a, id_pedido)
        assert "Acesso ao pedido negado" in (await assinar(b, id_pedido))["error"]
        assert requisitar(B, "GET", "/pedidos/" + id_pedido)[0] == 404
        assert requisitar(A, "POST", f"/pedidos/{id_pedido}/processamento")[0] == 202
        evento = await receber(a, lambda m: m.get("target") == "PedidoAtualizado")
        assert evento["arguments"][0]["estado"] == "processado"
        print("U1: operador inscrito recebeu evento processado após HTTP 202.")
        try:
            async with asyncio.timeout(.3):
                await receber(b, lambda m: m.get("target") == "PedidoAtualizado")
                raise AssertionError("Operador sem permissão recebeu um evento.")
        except TimeoutError:
            pass
        print("U2: assinatura negada, HTTP 404 e ausência de evento na janela observada.")

    outro = novo_pedido()
    antigo, conn_antiga = url(A)
    async with connect(antigo) as a:
        await handshake(a)
        assert "error" not in await assinar(a, outro)
    assert requisitar(A, "POST", f"/pedidos/{outro}/processamento")[0] == 202
    async with asyncio.timeout(5):
        while json.loads(requisitar(A, "GET", "/api/v2/pedidos/" + outro)[1])["estado"] != "processado":
            await asyncio.sleep(.01)
    novo, conn_nova = url(A)
    assert conn_nova != conn_antiga
    async with connect(novo) as a:
        await handshake(a)
        assert "error" not in await assinar(a, outro)
        status, body = requisitar(A, "GET", "/api/v2/pedidos/" + outro)
        assert status == 200 and json.loads(body)["estado"] == "processado"
    print("U3: nova conexão reautorizada e estado perdido recuperado pelo GET v2.")


if __name__ == "__main__":
    asyncio.run(main())
