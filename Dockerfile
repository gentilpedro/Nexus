FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Nexus.slnx .
COPY src/Nexus.Domain/Nexus.Domain.csproj src/Nexus.Domain/
COPY src/Nexus.Infrastructure/Nexus.Infrastructure.csproj src/Nexus.Infrastructure/
COPY src/Nexus.Web/Nexus.Web.csproj src/Nexus.Web/
COPY tests/Nexus.Domain.Tests/Nexus.Domain.Tests.csproj tests/Nexus.Domain.Tests/
COPY tests/Nexus.Infrastructure.Tests/Nexus.Infrastructure.Tests.csproj tests/Nexus.Infrastructure.Tests/
RUN dotnet restore src/Nexus.Web/Nexus.Web.csproj

COPY src/ src/
RUN dotnet publish src/Nexus.Web/Nexus.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Attachments are written at runtime under App_Data/uploads (see AttachmentStorageService).
# Created and chowned here so the non-root user below can write to it; otherwise the first
# upload fails with an access error that only appears in the container, never in local dev.
RUN mkdir -p /app/App_Data/uploads && chown -R $APP_UID:$APP_UID /app

# Drop root. The .NET base images ship a non-root user (APP_UID=1654) but still default to
# root, so without this line the whole application — including anything that manages to execute
# through it — runs with full container privileges for no reason.
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Uses the liveness endpoint added for observability. Without a HEALTHCHECK, an orchestrator
# (compose, Swarm, ECS) treats "the process is still running" as healthy, which stays true even
# when the app is failing every request.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "/app/Nexus.Web.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "Nexus.Web.dll"]
