# 1. Imagen base con el runtime de ASP.NET Core
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# 2. Imagen con SDK para compilar el proyecto
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar solución y proyectos por separado para optimizar restore
COPY crmchapultepec.sln ./
COPY crmchapultepec/*.csproj ./crmchapultepec/
COPY crmchapultepec.data/*.csproj ./crmchapultepec.data/
COPY crmchapultepec.services/*.csproj ./crmchapultepec.services/

# Restaurar paquetes
RUN dotnet restore

# Copiar todo el código
COPY crmchapultepec/ ./crmchapultepec/
COPY crmchapultepec.data/ ./crmchapultepec.data/
COPY crmchapultepec.services/ ./crmchapultepec.services/

# Publicar en modo Release
RUN dotnet publish crmchapultepec/crmchapultepec.csproj -c Release -o /app/publish

# 3. Imagen final
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Configurar URL y puerto
ENV ASPNETCORE_URLS=http://+:8080

# Ejecutar app
ENTRYPOINT ["dotnet", "crmchapultepec.dll"]
