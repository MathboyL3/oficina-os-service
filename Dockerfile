# syntax=docker/dockerfile:1.7
# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore (camada cacheável)
COPY Directory.Build.props ./
COPY src/Os.Domain/*.csproj         src/Os.Domain/
COPY src/Os.Application/*.csproj    src/Os.Application/
COPY src/Os.Infrastructure/*.csproj src/Os.Infrastructure/
COPY src/Os.Api/*.csproj            src/Os.Api/
RUN dotnet restore src/Os.Api/Os.Api.csproj

# Build + publish
COPY src/ src/
RUN dotnet publish src/Os.Api/Os.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# --- New Relic .NET agent (APM) ---
# Instalado a partir do tarball oficial (evita a chave apt legada DSA1024).
# A ativação é por variáveis de ambiente (CORECLR_*) + NEW_RELIC_LICENSE_KEY,
# injetadas em runtime via K8s Secret — nenhum segredo de produção fica na imagem;
# o appsettings.Development.json contém só valores de desenvolvimento.
RUN apt-get update && apt-get install -y --no-install-recommends wget ca-certificates \
    && mkdir -p /usr/local/newrelic-dotnet-agent \
    && wget -qO /tmp/nr.tar.gz "https://download.newrelic.com/dot_net_agent/latest_release/newrelic-dotnet-agent_amd64.tar.gz" \
    && tar xzf /tmp/nr.tar.gz -C /usr/local/newrelic-dotnet-agent --strip-components=1 \
    && rm /tmp/nr.tar.gz \
    && apt-get purge -y wget && apt-get autoremove -y && apt-get clean && rm -rf /var/lib/apt/lists/*

# Variáveis do profiler CLR (ativam a instrumentação). LICENSE KEY vem do ambiente.
ENV CORECLR_ENABLE_PROFILING=1 \
    CORECLR_PROFILER={36032161-FFC0-4B61-B559-F6C5D41BAE5A} \
    CORECLR_PROFILER_PATH=/usr/local/newrelic-dotnet-agent/libNewRelicProfiler.so \
    CORECLR_NEWRELIC_HOME=/usr/local/newrelic-dotnet-agent \
    NEW_RELIC_APP_NAME="oficina-os-service"

# Usa o usuário não-root `app` (uid 1654) da imagem base aspnet
RUN mkdir -p /usr/local/newrelic-dotnet-agent/logs && chown -R app:app /usr/local/newrelic-dotnet-agent

COPY --from=build --chown=app:app /app/publish .

USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false

ENTRYPOINT ["dotnet", "Os.Api.dll"]
