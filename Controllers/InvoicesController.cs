using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using TVPPdfConverter.Services;
using TVPPdfConverter.Services.Discovery;
using TVPPdfConverter.Models;
using ClosedXML.Excel;
using System.Runtime.InteropServices;
using UglyToad.PdfPig;
using System.Text.Json;

namespace TVPPdfConverter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    private readonly PdfTextExtractor _extractor;
    private readonly PdfDiscoveryService _discovery;
    private readonly ILogger<InvoicesController> _logger;

    public InvoicesController(PdfDiscoveryService discovery, ILogger<InvoicesController> logger)
    {
        _discovery = discovery;
        _logger = logger;
        // Detectar el sistema operativo y usar la ruta correcta de pdftotext
        string pdftotextPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Ruta para Windows (desarrollo local)
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "pdftotext.exe");
            var envPath = Environment.GetEnvironmentVariable("PDFTOTEXT_PATH");

            if (System.IO.File.Exists(toolsPath))
            {
                pdftotextPath = toolsPath;
                _logger.LogInformation("Usando pdftotext desde tools: {Path}", pdftotextPath);
            }
            else if (!string.IsNullOrWhiteSpace(envPath) && System.IO.File.Exists(envPath))
            {
                pdftotextPath = envPath!;
                _logger.LogInformation("Usando pdftotext desde PDFTOTEXT_PATH: {Path}", pdftotextPath);
            }
            else
            {
                // Último recurso: usar el PATH del sistema
                pdftotextPath = "pdftotext";
                _logger.LogWarning("pdftotext.exe no encontrado en tools/ ni PDFTOTEXT_PATH. Se intentará usar 'pdftotext' del PATH; si falla, se hará fallback a PdfPig.");
            }
        }
        else
        {
            // En Linux (Docker/contenedores), pdftotext está en el PATH
            pdftotextPath = "pdftotext";
            _logger.LogInformation("Entorno Linux/Container: se espera 'pdftotext' en PATH (poppler-utils). Si no está, se usará PdfPig como fallback.");
        }

        _extractor = new PdfTextExtractor(pdftotextPath);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileContentResult), Microsoft.AspNetCore.Http.StatusCodes.Status200OK, "application/vnd.ms-excel")] 
    [ProducesResponseType(Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile zip, [FromForm] string? columns = null)
    {
        if (zip == null || !zip.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Debe subir un archivo .zip" });

        var summary = await BuildSummaryAsync(zip.OpenReadStream(), HttpContext.RequestAborted);
        if (summary.Lines.Count == 0)
        {
            string message = summary.Duplicates == summary.TotalPdfs && summary.TotalPdfs > 0
                ? "Todos los PDFs están marcados como DUPLICADO."
                : (summary.TotalPdfs == 0
                    ? "El ZIP no contiene PDFs."
                    : "No se pudo extraer información de los PDFs.");
            return BadRequest(new { message, totalPdfs = summary.TotalPdfs, duplicates = summary.Duplicates, parsedPdfs = summary.ParsedPdfs, noDataPdfs = summary.NoDataPdfs, summary.Errors });
        }

        var selected = ParseColumns(columns);
        var bytes = ToExcel(summary.Lines, selected);
        return File(bytes, "application/vnd.ms-excel", "invoices.xls");
    }

    /* helper */
    private static readonly string[] DefaultColumns = new[]
    {
        nameof(InvoiceLine.InvoiceNumber),
        nameof(InvoiceLine.FechaEmision),
        nameof(InvoiceLine.Artista),
        nameof(InvoiceLine.Concepto),
        nameof(InvoiceLine.Instrumento),
        nameof(InvoiceLine.FechaDesde),
        nameof(InvoiceLine.FechaHasta),
        nameof(InvoiceLine.HoraDesde),
        nameof(InvoiceLine.HoraHasta),
        nameof(InvoiceLine.Dias),
        nameof(InvoiceLine.Horas),
        nameof(InvoiceLine.Unitario),
        nameof(InvoiceLine.Subtotal),
        nameof(InvoiceLine.SubtotalFactura),
        nameof(InvoiceLine.AporteContribucionOS),
        nameof(InvoiceLine.Jubilacion),
        nameof(InvoiceLine.RecursoAdministrativo),
        nameof(InvoiceLine.Tasa),
        nameof(InvoiceLine.Transporte),
        nameof(InvoiceLine.TotalFactura),
        nameof(InvoiceLine.Programa)
    };

    private static List<string> ParseColumns(string? columns)
    {
        if (string.IsNullOrWhiteSpace(columns)) return DefaultColumns.ToList();
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(columns);
            if (list == null || list.Count == 0) return DefaultColumns.ToList();
            // Filtrar solo válidos y preservar orden solicitado
            var set = new HashSet<string>(DefaultColumns, StringComparer.Ordinal);
            return list.Where(c => set.Contains(c)).ToList();
        }
        catch
        {
            return DefaultColumns.ToList();
        }
    }

    private static byte[] ToExcel(IEnumerable<InvoiceLine> lines, List<string>? columns = null)
    {
        var cols = (columns == null || columns.Count == 0) ? DefaultColumns.ToList() : columns;
        using var wb = new XLWorkbook();

        // 1) crear la hoja
        var ws = wb.Worksheets.Add("Datos");
        // 2) headers
        for (int i = 0; i < cols.Count; i++)
        {
            ws.Cell(1, i + 1).Value = cols[i];
        }
        // 3) rows
        int row = 2;
        foreach (var item in lines)
        {
            for (int i = 0; i < cols.Count; i++)
            {
                var col = cols[i];
                object? value = col switch
                {
                    nameof(InvoiceLine.InvoiceNumber) => item.InvoiceNumber,
                    nameof(InvoiceLine.FechaEmision) => item.FechaEmision,
                    nameof(InvoiceLine.Artista) => item.Artista,
                    nameof(InvoiceLine.Concepto) => item.Concepto,
                    nameof(InvoiceLine.Instrumento) => item.Instrumento,
                    nameof(InvoiceLine.FechaDesde) => item.FechaDesde,
                    nameof(InvoiceLine.FechaHasta) => item.FechaHasta,
                    nameof(InvoiceLine.HoraDesde) => item.HoraDesde,
                    nameof(InvoiceLine.HoraHasta) => item.HoraHasta,
                    nameof(InvoiceLine.Dias) => item.Dias,
                    nameof(InvoiceLine.Horas) => item.Horas,
                    nameof(InvoiceLine.Unitario) => item.Unitario,
                    nameof(InvoiceLine.Subtotal) => item.Subtotal,
                    nameof(InvoiceLine.SubtotalFactura) => item.SubtotalFactura,
                    nameof(InvoiceLine.AporteContribucionOS) => item.AporteContribucionOS,
                    nameof(InvoiceLine.Jubilacion) => item.Jubilacion,
                    nameof(InvoiceLine.RecursoAdministrativo) => item.RecursoAdministrativo,
                    nameof(InvoiceLine.Tasa) => item.Tasa,
                    nameof(InvoiceLine.Transporte) => item.Transporte,
                    nameof(InvoiceLine.TotalFactura) => item.TotalFactura,
                    nameof(InvoiceLine.Programa) => item.Programa,
                    _ => null
                };
                var cell = ws.Cell(row, i + 1);
                if (value is DateTime dt)
                {
                    cell.Value = dt;
                    cell.Style.DateFormat.Format = "dd/MM/yyyy";
                }
                else if (value is int vi) cell.Value = vi;
                else if (value is long vl) cell.Value = vl;
                else if (value is decimal vd) cell.Value = vd;
                else if (value is double vx) cell.Value = vx;
                else if (value is float vf) cell.Value = vf;
                else if (value is string vs) cell.Value = vs;
                else cell.Value = value?.ToString() ?? string.Empty;
            }
            row++;
        }

        // 4) auto-ajustar anchos (sobre la hoja, no sobre el libro)
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(object), 200, "application/json")]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Preview(IFormFile zip)
    {
        if (zip == null || !zip.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Debe subir un archivo .zip" });

        var summary = await BuildSummaryAsync(zip.OpenReadStream(), HttpContext.RequestAborted, previewLimit: 200);
        var message = summary.ParsedPdfs > 0
            ? $"Se extrajeron {summary.Lines.Count} filas desde {summary.ParsedPdfs} PDF(s). Duplicados: {summary.Duplicates}. Sin datos: {summary.NoDataPdfs}."
            : (summary.Duplicates == summary.TotalPdfs && summary.TotalPdfs > 0 ? "Todos los PDFs están marcados como DUPLICADO." : (summary.TotalPdfs == 0 ? "El ZIP no contiene PDFs." : "No se pudo extraer información de los PDFs."));

        return Ok(new { summary.TotalPdfs, summary.Duplicates, summary.ParsedPdfs, summary.NoDataPdfs, summary.Errors, rows = summary.Lines, message });
    }

    private sealed class UploadSummary
    {
        public int TotalPdfs { get; set; }
        public int Duplicates { get; set; }
        public int ParsedPdfs { get; set; }
        public int NoDataPdfs { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<InvoiceLine> Lines { get; set; } = new();
    }

    private static bool IsDuplicatePdf(string path)
    {
        try
        {
            using var doc = PdfDocument.Open(path);
            var text = doc.GetPage(1).Text;
            return text.IndexOf("DUPLICADO", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<UploadSummary> BuildSummaryAsync(Stream zipStream, CancellationToken ct, int? previewLimit = null)
    {
        var s = new UploadSummary();
        var temps = new List<(string path, bool isDup)>();
        try
        {
            await foreach (var tmp in _discovery.DiscoverTempPdfFilesFromZipStreamAsync(zipStream, options: null, cancellationToken: ct))
            {
                s.TotalPdfs++;
                bool dup = IsDuplicatePdf(tmp);
                if (dup) s.Duplicates++;
                temps.Add((tmp, dup));
            }

            // Selección: si hay originales, procesar solo originales; si no, procesar duplicados
            var hasOriginals = temps.Exists(t => !t.isDup);
            var selected = hasOriginals ? temps.FindAll(t => !t.isDup) : temps;
            _logger.LogInformation("Selección de PDFs: Totales={Total}, Originales={Originales}, Duplicados={Duplicados}. Se procesarán {Procesar} ({Tipo}).",
                s.TotalPdfs,
                temps.Count(t => !t.isDup),
                temps.Count(t => t.isDup),
                selected.Count,
                hasOriginals ? "originales" : "duplicados");

            foreach (var (path, isDup) in selected)
            {
                try
                {
                    var before = s.Lines.Count;
                    foreach (var ln in _extractor.Extract(path))
                    {
                        if (previewLimit.HasValue && s.Lines.Count >= previewLimit.Value)
                            break;
                        s.Lines.Add(ln);
                    }
                    var added = s.Lines.Count - before;
                    if (added > 0) s.ParsedPdfs++; else s.NoDataPdfs++;
                }
                catch (Exception ex)
                {
                    s.Errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
            return s;
        }
        finally
        {
            foreach (var (path, _) in temps)
            {
                try { System.IO.File.Delete(path); } catch { }
            }
        }
    }
}
