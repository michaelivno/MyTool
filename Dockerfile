# syntax=docker/dockerfile:1
# Build the API + Blazor WebAssembly client, run them as one container.
# Images are Microsoft's official .NET images from mcr.microsoft.com.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so dependency layers cache between code changes.
COPY src/Cockpit.Shared/Cockpit.Shared.csproj src/Cockpit.Shared/
COPY src/Cockpit.Client/Cockpit.Client.csproj src/Cockpit.Client/
COPY src/Cockpit.Server/Cockpit.Server.csproj src/Cockpit.Server/
RUN dotnet restore src/Cockpit.Server/Cockpit.Server.csproj

COPY src/ src/
RUN dotnet publish src/Cockpit.Server/Cockpit.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    COCKPIT_DB=/data/cockpit.db \
    DOTNET_EnableDiagnostics=0
VOLUME ["/data"]
EXPOSE 8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Cockpit.Server.dll"]
