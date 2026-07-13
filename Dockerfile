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

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Nexus.Web.dll"]
