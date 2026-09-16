FROM node:24.13.0-bookworm-slim AS node
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
COPY --from=node /usr/local/ /usr/local/
WORKDIR /source
COPY . .
ARG SERVICE_VERSION=0.1.0
# ClientFramework is defined by the generated project. Node is available for Angular's publish target.
RUN dotnet publish src/Web/Web.csproj -c Release -o /app/publish /p:UseAppHost=false /p:InformationalVersion=${SERVICE_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080 DOTNET_EnableDiagnostics=0
EXPOSE 8080 8443
USER $APP_UID
ENTRYPOINT ["dotnet", "CleanArchitecture.Web.dll"]
