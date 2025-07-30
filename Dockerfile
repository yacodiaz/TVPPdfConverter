# Dockerfile para TVPPdfConverter
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Instalar herramientas necesarias y poppler-utils
RUN apt-get update && apt-get install -y \
    poppler-utils \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar archivo de proyecto y restaurar dependencias
COPY ["TVPPdfConverter.csproj", "."]
RUN dotnet restore "TVPPdfConverter.csproj"

# Copiar todo el código fuente
COPY . .

# Compilar la aplicación
RUN dotnet build "TVPPdfConverter.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TVPPdfConverter.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# Copiar la aplicación compilada
COPY --from=publish /app/publish .

# Crear directorios necesarios
RUN mkdir -p /app/wwwroot/tmp
RUN mkdir -p /app/tools

# Configurar variables de entorno
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Punto de entrada
ENTRYPOINT ["dotnet", "TVPPdfConverter.dll"] 