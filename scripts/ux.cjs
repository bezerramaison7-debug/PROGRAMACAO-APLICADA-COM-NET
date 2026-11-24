const { chromium } = require("playwright");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");

const base = process.env.HUB_URL || "http://127.0.0.1:5080";
const saida = process.argv[2] || "artifacts/ux";
const registro = [];

async function texto(page, id, esperado) {
    await page.waitForFunction(({ id, esperado }) =>
        document.getElementById(id).textContent === esperado, { id, esperado });
}

async function conectar(page, token) {
    await page.goto(base);
    await page.getByLabel("Chave do operador").fill(token);
    await page.getByRole("button", { name: "Conectar", exact: true }).click();
    await texto(page, "conexao", "Conectado");
}

async function criar(page, sku) {
    await page.getByLabel("Região").selectOption("NE");
    await page.getByLabel("SKU").fill(sku);
    await page.getByLabel("Quantidade").fill("10");
    await page.getByLabel("Preço unitário em centavos").fill("100");
    await page.getByRole("button", { name: "Criar pedido" }).click();
    await texto(page, "estado", "recebido");
    return page.getByLabel("Identificador do pedido").inputValue();
}

async function screenshot(page, nome) {
    await page.screenshot({ path: path.join(saida, nome), fullPage: true });
}

async function main() {
    await fs.mkdir(saida, { recursive: true });
    const chaveA = process.env.HUB_TOKEN;
    const chaveB = process.env.HUB_TOKEN_OTHER;
    const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined,
        args: ["--no-sandbox", "--disable-dev-shm-usage"] });
    try {
        const operador = await browser.newPage({ viewport: { width: 1050, height: 770 } });
        await conectar(operador, chaveA);
        const id = await criar(operador, "LAB01");
        await operador.getByRole("button", { name: "Enviar para processamento" }).click();
        await texto(operador, "estado", "processado");
        await texto(operador, "atividade", "Atualização recebida em tempo real.");
        await screenshot(operador, "01-operador.png");
        registro.push("U1 · Operador: criação regional, envio e atualização SignalR observada.");

        const visitante = await browser.newPage({ viewport: { width: 390, height: 844 } });
        await conectar(visitante, chaveB);
        await visitante.getByLabel("Identificador do pedido").fill(id);
        await visitante.getByRole("button", { name: "Acompanhar" }).click();
        await visitante.waitForFunction(() => document.querySelector("#erro").textContent.includes("Acesso ao pedido negado"));
        assert.equal(await visitante.locator("#estado").textContent(), "—");
        assert.equal(await visitante.evaluate(() => document.documentElement.scrollWidth), 390);
        await screenshot(visitante, "02-acesso-negado.png");
        registro.push("U2 · Operador distinto: assinatura recusada no servidor e tela móvel sem dados do pedido.");

        const supervisor = await browser.newPage({ viewport: { width: 1050, height: 770 } });
        let socket;
        let bloquear = false;
        await supervisor.routeWebSocket(/\/hubs\/pedidos\?/, route => {
            if (bloquear) {
                route.close({ code: 1013, reason: "Interrupção controlada" });
                return;
            }
            socket = route;
            route.connectToServer();
        });
        await conectar(supervisor, chaveA);
        const outro = await criar(supervisor, "LAB02");
        assert.ok(socket);
        bloquear = true;
        await socket.close({ code: 1012, reason: "Interrupção controlada" });
        await texto(supervisor, "conexao", "Reconectando");
        assert.equal(await supervisor.locator("#processar").isDisabled(), true);
        const feito = await operador.request.post(base + "/pedidos/" + outro + "/processamento",
            { headers: { Authorization: "Bearer " + chaveA } });
        assert.equal(feito.status(), 202);
        await screenshot(supervisor, "03-reconectando.png");
        bloquear = false;
        await texto(supervisor, "conexao", "Conectado");
        await texto(supervisor, "atividade", "Estado reconciliado pela API.");
        await texto(supervisor, "estado", "processado");
        await screenshot(supervisor, "04-reconciliado.png");
        registro.push("U3 · Supervisor: ação indisponível durante reconexão. Processamento durante a interrupção recuperado via HTTP.");
    } finally {
        await browser.close();
        await fs.writeFile(path.join(saida, "resultados.txt"), registro.join("\n") + "\n");
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
