# Hub de Processamento de Pedidos

Requer SDK .NET 10. Compile com `dotnet build`, teste com `dotnet test` e execute com
`dotnet run --project src/Api --no-launch-profile --urls http://127.0.0.1:5080`.
O endpoint `GET /health` verifica a instância. Uma requisição de laboratório para `POST /pedidos`:

```json
{"regiao":"SE","prioritario":false,"itens":[{"sku":"A","quantidade":1,"precoCentavos":500}]}
```

## Executar a revisão atual

Use o SDK fixado em `global.json`. O banco de laboratório SQLite é criado por migrações.
Para gerar uma chave efêmera e rodar a interface localmente em Bash:

```bash
export Auth__Operadores__carga="$(openssl rand -hex 16)"
export HUB_TOKEN="$Auth__Operadores__carga"
dotnet run --project src/Api --no-launch-profile --urls http://127.0.0.1:5080
```

Abra `http://127.0.0.1:5080/` e use a chave da sessão no formulário. Para os pedidos da API, use
o cabeçalho `Authorization: Bearer <chave-da-sessao>`. Cada chave identifica um operador.
A autenticação do laboratório exige HTTPS fora do loopback e uma configuração própria do ambiente.
O código demonstra isolamento por operador em uma única instância. O endpoint público de indicadores
mostra somente agregados da base sintética. As rotas privadas usam validação condicional e estão fora do output cache.

| Tarefa | Comando |
|---|---|
| Revisão, testes e pacote Release | `bash scripts/revisao.sh` |
| Health Check e teste do pacote na mesma instância | `bash scripts/homologar.sh` |
| Validação do bloqueio por análise e testes | `bash scripts/provar-gates.sh` |
| Carga comparativa | `bash scripts/diagnostico.sh` |
| Counters e trace | Instale dotnet-counters e dotnet-trace 10.0.750501 e use `PERFILAR=1 bash scripts/diagnostico.sh` |
| Microbenchmark | `dotnet run -c Release --project benchmarks/Benchmarks -- --filter '*'` |
| Simulações no navegador | Instale Playwright 1.56.1 e Chromium conforme o workflow, depois execute `bash scripts/homologar.sh ux` |

As referências `roteiro-01` a `roteiro-08` identificam as práticas cumulativas.
Consulte [a política de versões e dependências](docs/contratos.md).
A carga compara 6 conexões, 1 escritora, 2 s de aquecimento e 5 s de medição.
Meta pré-definida para laboratório local: p95 ≤ 10 ms, p99 ≤ 25 ms, ≥ 500 req/s e erros < 1%.
A consulta normal permanece como padrão. A variante compilada é uma configuração experimental.

`bash scripts/fila-cenarios.sh` compara duas configurações de fila com pedidos sintéticos.
Como validação de protocolo complementar aos testes no navegador, instale `websockets==16.0`
em um ambiente Python isolado e execute `bash scripts/homologar.sh protocolo`.
