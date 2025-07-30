# TVP PDF Converter

Una API web ASP.NET Core para extraer información de facturas desde archivos PDF y convertirla a formato Excel.

## 🚀 Funcionalidades

- **Extracción de PDFs**: Procesa archivos PDF de pre-liquidaciones/facturas
- **Detección automática**: Identifica y excluye documentos marcados como "DUPLICADO"
- **Datos completos**: Extrae información detallada incluyendo:
  - Datos de factura (número, fecha de emisión)
  - Información del artista
  - Detalles de servicios (concepto, instrumento, fechas, horarios)
  - Valores monetarios (unitario, subtotal por línea)
  - **Información fiscal**: Subtotal de factura, aportes de obra social, jubilación, recursos administrativos, tasa, transporte y total
- **Batch processing**: Procesa múltiples PDFs desde archivos ZIP
- **Exportación Excel**: Genera archivos Excel (.xls) con todos los datos extraídos
- **API REST**: Interfaz web fácil de usar con Swagger UI

## 🛠️ Tecnologías

- **Backend**: ASP.NET Core 8.0
- **PDF Processing**: Poppler tools (pdftotext.exe)
- **Excel Generation**: ClosedXML
- **PDF Parsing**: UglyToad.PdfPig
- **API Documentation**: Swagger/OpenAPI

## 📋 Requisitos Previos

- .NET 8.0 SDK
- Windows (debido a las herramientas de Poppler incluidas)

## 🏃‍♂️ Inicio Rápido

### Instalación

1. **Clonar el repositorio**:
   ```bash
   git clone https://github.com/tu-usuario/TVPPdfConverter.git
   cd TVPPdfConverter
   ```

2. **Restaurar dependencias**:
   ```bash
   dotnet restore
   ```

3. **Compilar**:
   ```bash
   dotnet build
   ```

4. **Ejecutar**:
   ```bash
   dotnet run
   ```

5. **Acceder a la aplicación**:
   - API: `http://localhost:5028`
   - Swagger UI: `http://localhost:5028/swagger`

### Uso

1. **Via Swagger UI**:
   - Visita `http://localhost:5028/swagger`
   - Usa el endpoint `POST /api/invoices/upload`
   - Sube un archivo ZIP con PDFs

2. **Via API directa**:
   ```bash
   curl -X POST "http://localhost:5028/api/invoices/upload" \
        -H "Content-Type: multipart/form-data" \
        -F "zip=@tu_archivo.zip" \
        -o resultado.xls
   ```

## 📁 Estructura del Proyecto

```
TVPPdfConverter/
├── Controllers/
│   └── InvoicesController.cs      # Controlador principal de la API
├── Models/
│   ├── InvoiceLine.cs            # Modelo de datos de líneas de factura
│   └── ProcesarZipRequest.cs     # Modelo de request
├── Services/
│   └── PdfInvoiceExtractor.cs    # Lógica de extracción de PDFs
├── Examples/                     # PDFs de ejemplo para testing
├── bin/Debug/net8.0/tools/       # Herramientas de Poppler
└── wwwroot/tmp/                  # Archivos temporales generados
```

## 🔧 Configuración

### Variables de Configuración

La aplicación se configura principalmente a través de `appsettings.json` y `appsettings.Development.json`.

### Herramientas PDF

La aplicación incluye las herramientas de Poppler en la carpeta `tools/`. Si necesitas usar una versión diferente:

1. Descarga Poppler tools para Windows
2. Actualiza la ruta en `InvoicesController.cs`:
   ```csharp
   private readonly PdfTextExtractor _extractor = new("ruta/a/pdftotext.exe");
   ```

## 📊 Formato de Datos Extraídos

El archivo Excel generado incluye las siguientes columnas:

| Campo | Descripción |
|-------|-------------|
| InvoiceNumber | Número de pre-liquidación |
| FechaEmision | Fecha de emisión de la factura |
| Artista | Nombre del artista/ejecutante |
| Concepto | Tipo de servicio realizado |
| Instrumento | Instrumento o rol desempeñado |
| FechaDesde/FechaHasta | Período de servicio |
| HoraDesde/HoraHasta | Horario de servicio |
| Dias | Cantidad de días trabajados |
| Horas | Cantidad de horas trabajadas |
| Unitario | Valor unitario por hora |
| Subtotal | Subtotal de la línea |
| **SubtotalFactura** | Subtotal total de la factura |
| **AporteContribucionOS** | Aporte a obra social |
| **Jubilacion** | Descuento jubilatorio |
| **RecursoAdministrativo** | Recurso administrativo |
| **Tasa** | Tasas aplicables |
| **Transporte** | Gastos de transporte |
| **TotalFactura** | Total final de la factura |

## 🐛 Solución de Problemas

### Errores Comunes

1. **"No se encontró pdftotext.exe"**: Verifica que las herramientas estén en la carpeta correcta
2. **"No se encontraron PDFs válidos"**: Asegúrate de que el ZIP contenga archivos PDF válidos
3. **PDFs marcados como DUPLICADO**: La aplicación automáticamente excluye estos documentos

### Logs

La aplicación registra información de debug en la consola durante el desarrollo.

## 🚢 Deployment

### Docker (Recomendado)

```dockerfile
# Dockerfile incluido en el proyecto
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["TVPPdfConverter.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TVPPdfConverter.dll"]
```

### Servicios de Hosting Gratuitos

- **Railway**: Ideal para aplicaciones .NET Core
- **Render**: Soporte completo para .NET
- **Azure App Service**: Tier gratuito disponible

## 🤝 Contribuciones

1. Fork del proyecto
2. Crea una branch para tu feature (`git checkout -b feature/nueva-funcionalidad`)
3. Commit tus cambios (`git commit -am 'Agrega nueva funcionalidad'`)
4. Push a la branch (`git push origin feature/nueva-funcionalidad`)
5. Abre un Pull Request

## 📝 Licencia

Este proyecto está bajo la Licencia MIT. Ver el archivo `LICENSE` para más detalles.

## 📞 Soporte

Si encuentras algún problema o tienes preguntas:

1. Revisa la sección de [Issues](https://github.com/tu-usuario/TVPPdfConverter/issues)
2. Crea un nuevo issue si es necesario
3. Incluye información detallada sobre el error y los archivos PDF que causaron el problema

---

⭐ **¡No olvides dar una estrella al proyecto si te resultó útil!** 