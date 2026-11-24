# Contratos de consulta

O módulo Application depende de Domain. Infrastructure implementa a persistência e as portas de Application. Api é a raiz de composição, depende de Application e Infrastructure e hospeda os módulos de pedidos, processamento e notificações.

| Rota | Contrato | Semântica |
|---|---|---|
| GET /pedidos/{id} | v1 padrão | id, totalCentavos (inteiro), categoria |
| GET /api/v1/pedidos/{id} | v1 explícita | mesmos campos e unidade monetária |
| GET /api/v2/pedidos/{id} | v2 explícita | id, total (decimal em reais), moeda BRL, categoria, estado |
| GET /api/v3/pedidos/{id} | versão não suportada | 400 com erro estável |

O DTO de v2 é um adaptador de representação. A conversão usa decimal e divide os centavos por 100.
A v1 fica disponível. Antes de depreciar, medir uso por rota, comunicar consumidores e validar migração. Remover somente após o prazo comunicado e ausência confirmada de clientes v1 no período acordado.

DbContext, repositório e processador SQL têm vida scoped. O worker singleton cria um escopo por item e descarta o contexto antes da notificação. Catálogo e fila são compartilhados. O host valida o grafo e os escopos na inicialização.
