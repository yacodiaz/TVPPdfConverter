# 🚀 Guía de Deployment para TVP PDF Converter

## 📋 Pasos para Publicar en GitHub

### 1. Inicializar Git y Subir a GitHub

```bash
# 1. Inicializar repositorio Git
git init

# 2. Agregar todos los archivos
git add .

# 3. Hacer el primer commit
git commit -m "Initial commit: TVP PDF Converter con extracción de datos fiscales"

# 4. Crear repositorio en GitHub (ve a github.com y crea un nuevo repositorio)
# Luego conectar el repositorio local con el remoto:
git remote add origin https://github.com/TU-USUARIO/TVPPdfConverter.git

# 5. Subir el código
git branch -M main
git push -u origin main
```

### 2. Habilitar GitHub Actions

1. Ve a tu repositorio en GitHub
2. Click en la pestaña **Actions**
3. GitHub detectará automáticamente el workflow `.github/workflows/deploy.yml`
4. Click **"I understand my workflows, go ahead and enable them"**

## 🌐 Opciones de Hosting Gratuito

### Opción 1: Railway (Recomendado) ⭐

**Pros**: Fácil de usar, buena performance, build automático desde GitHub
**Límites**: 500 horas/mes gratis, $5/mes después

1. **Registrarse**: Ve a [railway.app](https://railway.app)
2. **Conectar GitHub**: Autoriza Railway a acceder a tu GitHub
3. **Crear proyecto**:
   - Click "Deploy from GitHub repo"
   - Selecciona tu repositorio `TVPPdfConverter`
   - Railway detectará automáticamente el `Dockerfile`
4. **Variables de entorno** (opcional):
   ```
   ASPNETCORE_ENVIRONMENT=Production
   ```
5. **Deploy**: Railway automáticamente construirá y desplegará tu app
6. **URL**: Railway te dará una URL como `https://tvppdfconverter-production.up.railway.app`

### Opción 2: Render.com

**Pros**: Buena documentación, SSL automático
**Límites**: 750 horas/mes gratis, apps duermen después de 15 min de inactividad

1. **Registrarse**: Ve a [render.com](https://render.com)
2. **Conectar GitHub**: Vincula tu cuenta de GitHub
3. **Crear Web Service**:
   - Click "New +" → "Web Service"
   - Conecta tu repositorio
   - Render detectará el `render.yaml`
4. **Configuración**:
   - **Name**: `tvp-pdf-converter`
   - **Branch**: `main`
   - **Runtime**: `Docker`
5. **Deploy**: Click "Create Web Service"

### Opción 3: Azure App Service (Free Tier)

**Pros**: Integración excelente con .NET, Microsoft
**Límites**: 60 minutos CPU/día, apps duermen

1. **Azure CLI**:
   ```bash
   # Instalar Azure CLI
   # Windows: https://aka.ms/installazurecliwindows
   
   # Login
   az login
   
   # Crear grupo de recursos
   az group create --name tvp-pdf-converter-rg --location "East US"
   
   # Crear app service plan (gratis)
   az appservice plan create --name tvp-pdf-converter-plan --resource-group tvp-pdf-converter-rg --sku F1 --is-linux
   
   # Crear web app
   az webapp create --resource-group tvp-pdf-converter-rg --plan tvp-pdf-converter-plan --name tvp-pdf-converter-app --deployment-container-image-name ghcr.io/TU-USUARIO/tvppdfconverter:latest
   ```

2. **Configurar deployment desde GitHub**:
   ```bash
   az webapp deployment github-actions add --resource-group tvp-pdf-converter-rg --name tvp-pdf-converter-app --repo "https://github.com/TU-USUARIO/TVPPdfConverter" --branch main --token TU_GITHUB_TOKEN
   ```

## 🔧 Testing Local antes del Deploy

```bash
# 1. Construir imagen Docker local
docker build -t tvp-pdf-converter .

# 2. Ejecutar contenedor local
docker run -p 8080:8080 tvp-pdf-converter

# 3. Probar en navegador
# http://localhost:8080/swagger
```

## 📊 Monitoreo y Logs

### Railway
```bash
# Instalar Railway CLI
npm install -g @railway/cli

# Login
railway login

# Ver logs
railway logs
```

### Render
- Ve a tu dashboard en Render.com
- Click en tu servicio
- Pestaña "Logs" para ver logs en tiempo real

### Azure
```bash
# Ver logs
az webapp log tail --name tvp-pdf-converter-app --resource-group tvp-pdf-converter-rg
```

## 🛠️ Troubleshooting Común

### Error: "pdftotext not found"
**Solución**: El Dockerfile ya incluye `poppler-utils`. Si persiste, verificar que el deployment use Docker.

### Error: "No such file or directory"
**Solución**: Verificar que las rutas en `InvoicesController.cs` sean correctas para Linux.

### App muy lenta o timeouts
**Solución**: 
- Aumentar límites de memoria si es posible
- Optimizar procesamiento de PDFs grandes
- Considerar plan pago para mejor performance

### SSL/HTTPS issues
**Solución**: La mayoría de servicios proveen HTTPS automáticamente. Verificar configuración de `ASPNETCORE_URLS`.

## 🔒 Configuración de Producción

### Variables de Entorno Recomendadas
```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
```

### Configuración de Logs
Agregar en `appsettings.Production.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## 📈 Próximos Pasos

1. **Dominio Personalizado**: Configurar un dominio propio
2. **Monitoreo**: Implementar Application Insights o similar
3. **Caching**: Agregar Redis para mejor performance
4. **Base de Datos**: Persistir datos de facturas procesadas
5. **Autenticación**: Agregar login/seguridad si es necesario

---

💡 **Tip**: Empieza con Railway por su simplicidad, luego migra a Azure si necesitas más control. 