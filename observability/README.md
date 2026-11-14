# Observabilidade (Fase 4)

Dashboard New Relic: [`dashboard-fase4.json`](dashboard-fase4.json) (4 páginas: visão geral, APM, infraestrutura e **Saga (Fase 4)**), cobrindo os 3 microsserviços (`oficina-os-service`, `oficina-billing-service`, `oficina-execucao-service`).

## Importar o dashboard

- **UI:** *Dashboards > Import dashboard* e colar o conteúdo do JSON.
- **NerdGraph:** mutation `dashboardCreate(accountId: 8507486, dashboard: <json>)` em https://api.newrelic.com/graphiql.

## Pré-requisitos

- Agente APM do New Relic na imagem de cada serviço (alimenta `Transaction`/`TransactionError`).
- Cluster com o `nri-bundle` e o **`newrelic-logging` habilitado**, como na Fase 3 (ver `fiap-app/observability/README.md`). Os widgets de log e a página da Saga dependem desse forwarder; CPU/memória usam `K8sContainerSample` filtrado por `deploymentName`.

## Verificação no Log Explorer

```sql
SELECT message, SagaEstado, OsId FROM Log WHERE message LIKE 'saga.estado%' SINCE 30 minutes ago
SELECT * FROM Log WHERE alerta IS NOT NULL SINCE 1 day ago
```

## Alertas estruturados (atributo `alerta`)

| `alerta` | Significado | Ação esperada |
|---|---|---|
| `saga_em_execucao_atrasada` | OS em execução além do prazo esperado na saga | Verificar o `oficina-execucao-service` (fila/consumer RabbitMQ) e a OS; reprocessar ou intervir manualmente |
| `saga_acao_manual` | A saga falhou/compensou e exige decisão humana | Analisar o `OsId`, corrigir a causa e concluir ou cancelar a OS manualmente |
| `pagamento_apos_cancelamento` | Pagamento confirmado para uma OS já cancelada | Estornar/reembolsar o pagamento no Mercado Pago e registrar o caso |