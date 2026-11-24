const el = id => document.getElementById(id);
let chave = "";
let conexao;
let aberto = "";
let revisao = 0;
let pronto = false;
let ocupado = false;

function estadoConexao(texto, ativa) {
    el("conexao").textContent = texto;
    pronto = ativa;
    botoes();
}

function botoes() {
    el("criar").querySelector("button").disabled = !pronto || ocupado;
    el("acompanhar").querySelector("button").disabled = !pronto || ocupado;
    el("processar").disabled = !pronto || ocupado || !aberto || el("estado").textContent === "processado";
}

async function api(url, method = "GET", body) {
    const headers = { Authorization: "Bearer " + chave };
    if (body) headers["Content-Type"] = "application/json";
    const response = await fetch(url, { method, headers, body: body ? JSON.stringify(body) : undefined, cache: "no-store" });
    if (!response.ok) {
        const detalhe = await response.json().catch(() => ({}));
        const mensagens = { 401: "Credencial inválida.", 404: "Pedido indisponível para este operador.",
            409: "Pedido já enviado ou processado.", 503: "Fila cheia. Tente novamente." };
        throw new Error(detalhe.erro ?? mensagens[response.status] ?? "Falha HTTP " + response.status);
    }
    return response.status === 202 ? null : response.json();
}

async function reconciliar(id) {
    const observada = revisao;
    const pedido = await api("/api/v2/pedidos/" + encodeURIComponent(id));
    if (id !== aberto) return;
    el("categoria").textContent = pedido.categoria;
    if (revisao === observada) el("estado").textContent = pedido.estado;
    el("total").textContent = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" }).format(pedido.total);
    botoes();
}

async function assinar(id) {
    const anterior = aberto;
    if (anterior && anterior !== id) await conexao.invoke("Desassinar", anterior);
    aberto = id;
    revisao = 0;
    for (const campo of ["categoria", "estado", "total"]) el(campo).textContent = "—";
    try {
        await conexao.invoke("Assinar", id);
        await reconciliar(id);
    } catch (erro) {
        aberto = "";
        botoes();
        throw erro;
    }
}

function acao(form, trabalho) {
    form.addEventListener("submit", async event => {
        event.preventDefault();
        el("erro").textContent = "";
        ocupado = true;
        botoes();
        try { await trabalho(); }
        catch (erro) { el("erro").textContent = erro.message; }
        finally { ocupado = false; botoes(); }
    });
}

acao(el("conectar"), async () => {
    const conectar = el("conectar").querySelector("button");
    conectar.disabled = true;
    try {
        if (conexao) await conexao.stop();
        chave = el("token").value;
        aberto = "";
        estadoConexao("Conectando", false);
        el("atividade").textContent = "";
        const atual = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/pedidos", { accessTokenFactory: () => chave, transport: signalR.HttpTransportType.WebSockets })
            .withAutomaticReconnect([0, 1000, 3000, 7000]).build();
        conexao = atual;
        atual.on("PedidoAtualizado", evento => {
            if (evento.id !== aberto || evento.revisao <= revisao) return;
            revisao = evento.revisao;
            el("estado").textContent = evento.estado;
            el("atividade").textContent = "Atualização recebida em tempo real.";
            botoes();
        });
        atual.onreconnecting(() => estadoConexao("Reconectando", false));
        atual.onreconnected(async () => {
            try {
                if (aberto) {
                    await atual.invoke("Assinar", aberto);
                    await reconciliar(aberto);
                    el("atividade").textContent = "Estado reconciliado pela API.";
                }
            } catch {
                aberto = "";
                for (const campo of ["categoria", "estado", "total"]) el(campo).textContent = "—";
                el("erro").textContent = "O pedido precisa ser acompanhado novamente.";
            }
            estadoConexao("Conectado", true);
        });
        atual.onclose(() => {
            if (conexao === atual) estadoConexao("Desconectado", false);
        });
        await atual.start();
        el("token").value = "";
        estadoConexao("Conectado", true);
    } catch {
        estadoConexao("Desconectado", false);
        throw new Error("Conexão recusada. Confira a chave do operador.");
    } finally {
        conectar.disabled = false;
    }
});

acao(el("criar"), async () => {
    const pedido = await api("/pedidos", "POST", {
        regiao: el("regiao").value, prioritario: el("prioritario").checked,
        itens: [{ sku: el("sku").value, quantidade: Number(el("quantidade").value),
            precoCentavos: Number(el("preco").value) }]
    });
    el("pedido").value = pedido.id;
    await assinar(pedido.id);
    el("atividade").textContent = "Pedido criado.";
});

acao(el("acompanhar"), async () => {
    await assinar(el("pedido").value.trim());
    el("atividade").textContent = "Pedido acompanhado.";
});

el("processar").addEventListener("click", async () => {
    el("erro").textContent = "";
    ocupado = true;
    botoes();
    try {
        await api("/pedidos/" + encodeURIComponent(aberto) + "/processamento", "POST");
        if (el("estado").textContent !== "processado") el("atividade").textContent = "Aceito para processamento.";
    } catch (erro) { el("erro").textContent = erro.message; }
    finally { ocupado = false; botoes(); }
});
