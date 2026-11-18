# Imagem de produção da VPS. Diferente do Dockerfile da raiz, NÃO compila: empacota a pasta
# publish/ que o job verify já testou e carimbou com a versão. Assim o binário que vai pro ar é
# exatamente o que passou no quality gate, e não um segundo build que poderia divergir dele.
#
# Contexto do build = a pasta publish/ (docker build -f deploy/vps/Dockerfile publish/).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY . .

# Mesmo motivo do Dockerfile da raiz: o usuário não-root precisa escrever os anexos.
RUN mkdir -p /app/App_Data/uploads && chown -R $APP_UID:$APP_UID /app

# Login pelo Active Directory (#70): System.DirectoryServices.Protocols carrega a libldap do
# sistema no Linux. O nome do pacote muda entre as versões do Debian; tenta os dois.
RUN apt-get update     && (apt-get install -y --no-install-recommends libldap2 || apt-get install -y --no-install-recommends libldap-2.5-0)     && rm -rf /var/lib/apt/lists/*

USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "/app/Nexus.Web.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "Nexus.Web.dll"]
