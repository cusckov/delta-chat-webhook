# ── Stage 1: Build .NET app ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/delta-chat-webhook-client.csproj .
RUN dotnet restore
COPY src/ .
RUN dotnet publish -c Release -o /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Устанавливаем Python + deltachat-rpc-server
RUN apt-get update && apt-get install -y --no-install-recommends \
        python3 python3-pip \
    && pip3 install --break-system-packages deltachat-rpc-server \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Папка для хранения аккаунтов Delta Chat
RUN mkdir -p /data/accounts
VOLUME ["/data/accounts"]

EXPOSE 8082

ENTRYPOINT ["dotnet", "delta-chat-webhook-client.dll"]
