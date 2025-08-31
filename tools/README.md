# Herramientas PDF (Windows)

Este proyecto puede usar `pdftotext.exe` (Poppler) para extraer texto con mejor preservación de layout. En Linux (Docker) se instala automáticamente `poppler-utils`. En Windows podés incluir los binarios localmente para que la app los detecte.

## Opciones de instalación

- Opción A – Winget (recomendada):
  1. `winget install -e --id oschwartz10612.Poppler`
  2. Copiá los binarios desde `C:\\Program Files\\poppler-<versión>\\Library\\bin` a esta carpeta `tools/` (ver "Qué copiar").

- Opción B – Chocolatey:
  1. `choco install poppler`
  2. Copiá los binarios de Poppler a `tools/`.

- Opción C – ZIP manual:
  1. Descargá Poppler para Windows (builds de oschwartz/poppler-windows).
  2. Extraé el ZIP y copiá el contenido de `bin` o `Library\\bin` a `tools/`.

## Qué copiar a `tools/`

- Mínimo: `pdftotext.exe` y sus DLLs dependientes que vienen en la misma carpeta (`libstdc++-6.dll`, `libgcc_s_seh-1.dll`, `libwinpthread-1.dll`, `libiconv-2.dll`, `libintl-8.dll`, etc.).
- Sugerido: copiar todo el contenido de `bin`/`Library\\bin` para evitar faltantes.

Esta carpeta `tools/` se publica automáticamente (ver `TVPPdfConverter.csproj`). En Windows, el controlador buscará `tools/pdftotext.exe` primero. Si no está, intentará usar la variable de entorno `PDFTOTEXT_PATH`, y si tampoco, caerá a un modo de extracción integrado (PdfPig).

## Alternativa: variable de entorno

En vez de copiar archivos, podés definir `PDFTOTEXT_PATH` apuntando a `pdftotext.exe`:

- PowerShell (sesión actual):

```
$env:PDFTOTEXT_PATH = "C:\\Program Files\\poppler-XX\\Library\\bin\\pdftotext.exe"
```

- Permanente para el usuario:

```
setx PDFTOTEXT_PATH "C:\\Program Files\\poppler-XX\\Library\\bin\\pdftotext.exe"
```

## Verificación rápida

1. Compilar: `dotnet build -c Release`
2. Ejecutar: `dotnet run`
3. Abrir la UI: `http://localhost:8080/`
4. Subir un `.zip` y verificar que no aparezcan errores de "pdftotext no encontrado".

## Notas

- Dentro de Docker (Linux) no necesitás hacer nada: el `Dockerfile` instala `poppler-utils`.
- Si usás sólo el fallback (PdfPig), funcionará pero el layout puede diferir levemente del de Poppler `-layout`.

