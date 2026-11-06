# syntax=docker/dockerfile:1
# Build context: repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until a csproj changes.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj src/BuildingBlocks.Messaging/
COPY src/Orders.Contracts/Orders.Contracts.csproj src/Orders.Contracts/
COPY src/Inventory.Worker/Inventory.Worker.csproj src/Inventory.Worker/
RUN dotnet restore src/Inventory.Worker/Inventory.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Inventory.Worker/Inventory.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Non-root user shipped with the official .NET images.
USER $APP_UID
ENTRYPOINT ["dotnet", "Inventory.Worker.dll"]
