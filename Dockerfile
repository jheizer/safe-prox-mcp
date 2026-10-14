# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SafeProxMcp/SafeProxMcp.csproj .
RUN dotnet restore

COPY src/SafeProxMcp/. .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV DOTNET_GCServerGarbageCollection=1
ENV DOTNET_GCConcurrentGC=1
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

# Cluster config is mounted at /app/clusters.json (see docker-compose.yml)

ENTRYPOINT ["dotnet", "SafeProxMcp.dll"]
