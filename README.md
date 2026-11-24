# Hub de Processamento de Pedidos

Requer SDK .NET 10. Compile com `dotnet build`, teste com `dotnet test` e execute com
`dotnet run --project src/Api --no-launch-profile --urls http://127.0.0.1:5080`.
O endpoint `GET /health` verifica a instância. Uma requisição de laboratório para `POST /pedidos`:

```json
{"regiao":"SE","prioritario":false,"itens":[{"sku":"A","quantidade":1,"precoCentavos":500}]}
```
