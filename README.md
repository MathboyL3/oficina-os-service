# oficina-os-service

## Rodando localmente
Requer Docker. Os 3 serviços compartilham a rede externa `oficina-dev` (os containers se enxergam pelo nome):
1. Uma vez: `docker network create oficina-dev`
2. Suba o **os-service primeiro** (traz o RabbitMQ): `docker compose up -d --build` em `oficina-os-service`
3. Depois `oficina-execucao-service` e `oficina-billing-service` (mesmo comando).

APIs: os `:5001`, billing `:5002`, execucao `:5003` (`/health/ready`). RabbitMQ management: `:15672` (guest/guest).

Postgres do os: porta host **5435** (→ 5432 no container), pois a 5433 costuma estar ocupada. Fora do compose, o `src/Os.Api/appsettings.json` aponta para `localhost:5433`; ajuste para `5435` ao usar este Postgres do compose.
