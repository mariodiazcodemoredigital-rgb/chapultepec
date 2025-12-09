# 1. Imagen base con el runtime de ASP.NET Core
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# 2. Imagen con el SDK para compilar el proyecto
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiamos todo el código al contenedor
COPY . .

# Restauramos dependencias
RUN dotnet restore

# Publicamos en modo Release
RUN dotnet publish -c Release -o /app/publish

# 3. Imagen final, solo con lo necesario para correr la app
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# La app Blazor Server escuchará en el puerto 8080 dentro del contenedor
ENV ASPNETCORE_URLS=http://+:8080

# 👇 IMPORTANTE: aquí va el nombre de tu dll principal
ENTRYPOINT ["dotnet", "crmchapultepec.dll"]