# Roteiro do vídeo — Fase 4 (até ~15 min)

Pré-requisito: ambiente no ar (docker compose dos 3 serviços ou cluster Kubernetes), coleções Postman importadas (`docs/*.postman_collection.json`), billing em `MercadoPago__Modo=Fake`. Mantenha abertos: Swagger, RabbitMQ UI e New Relic.

| Tempo | Cena | O que mostrar / dizer |
|---|---|---|
| 0:00–1:30 | Introdução | Problema, divisão do monólito em 3 microsserviços, 3 repositórios, SQL + NoSQL. Diagrama de [`ARQUITETURA-FASE4.md`](ARQUITETURA-FASE4.md) |
| 1:30–3:00 | Por que Saga orquestrada | Ordem estrita e compensações; estado consultável em `GET /api/ordens-servico/{id}`; diagrama de estados do README |
| 3:00–4:30 | Infra | `kubectl get pods -n oficina` (3 serviços, RabbitMQ, bancos), HPA, Kong (`/os`, `/billing`, `/execucao`), RabbitMQ UI (31672 ou 15672) |
| 4:30–7:30 | **Fluxo feliz** | Postman os-service: Bootstrap → Login → (execucao) cadastrar serviço e peça → cliente → veículo → abrir OS → itens → enviar orçamento. Billing: aprovar orçamento (Fake paga na hora). Acompanhar `saga.estado`: GerandoOrcamento → AguardandoAprovacao → AguardandoPagamento → ReservandoPecas → EmExecucao. Execucao: fila → iniciar → diagnóstico → finalizar → OS `Finalizada` → `entregar` |
| 7:30–9:30 | **Compensação** | Nova OS: após o pagamento, `POST .../cancelar` (ou execucao `falhar`): mostrar `Compensando` → peças liberadas (estoque volta), pagamento estornado, `Cancelada` com motivo. Opcional: `FakeComportamento=FalharEstorno` → `AguardandoAcaoManual` → `GET /api/admin/sagas` → reprocessar |
| 9:30–10:30 | Mercado Pago | Gateway real vs Fake, webhook com `x-signature` (request "Webhook MP (exemplo)" da coleção do billing), idempotência, estorno |
| 10:30–12:00 | Qualidade | `dotnet test` com a tabela de cobertura (≥ 80%), BDD `SagaOrdemServico.feature` (5 cenários), SonarCloud |
| 12:00–13:30 | CI/CD | Workflow: `build-test-sonar` → `image` (GHCR) → `deploy` (self-hosted); proteção da `main` (check `build-test-sonar`) |
| 13:30–15:00 | Observabilidade | New Relic: dashboard Fase 4, painel Saga (estados), trace distribuído com o mesmo `trace.id` atravessando os 3 serviços, alertas `saga_acao_manual` / `pagamento_apos_cancelamento` |

Encerramento: recapitular entregáveis (3 repos com README, Postman, Sonar, pipelines, vídeo) e riscos conhecidos (webhook real exige túnel público; runner self-hosted depende do Docker Desktop).
