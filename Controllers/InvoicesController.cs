using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using TVPPdfConverter.Services;
using TVPPdfConverter.Models;
using ClosedXML.Excel;
using System.Runtime.InteropServices;

namespace TVPPdfConverter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    private readonly PdfTextExtractor _extractor;

    public InvoicesController()
    {
        // Detectar el sistema operativo y usar la ruta correcta de pdftotext
        string pdftotextPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Ruta para Windows (desarrollo local)
            pdftotextPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "pdftotext.exe");
            
            // Fallback a ruta relativa si no existe
            if (!System.IO.File.Exists(pdftotextPath))
            {
                pdftotextPath = "C:\\Users\\yacod\\Downloads\\Release-24.08.0-0\\poppler-24.08.0\\Library\\bin\\pdftotext.exe";
            }
        }
        else
        {
            // En Linux (Docker/contenedores), pdftotext está en el PATH
            pdftotextPath = "pdftotext";
        }

        _extractor = new PdfTextExtractor(pdftotextPath);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [Produces("application/vnd.ms-excel")]
    public async Task<IActionResult> Upload(IFormFile zip)
    {
        if (zip == null || !zip.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Debe subir un archivo .zip");

        var rows = new List<InvoiceLine>();

        using var zipStream = zip.OpenReadStream();
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".pdf", true, null)))
        {
            var tmp = Path.GetTempFileName();
            await using (var s = entry.Open())
            await using (var fs = System.IO.File.Create(tmp))
                await s.CopyToAsync(fs);

            rows.AddRange(_extractor.Extract(tmp));
            System.IO.File.Delete(tmp);
        }

        if (rows.Count == 0) return BadRequest("No se encontraron PDFs válidos.");

        var bytes = ToExcel(rows);
        return File(bytes, "application/vnd.ms-excel", "invoices.xls");
    }

    /* helper */
    private static byte[] ToExcel(IEnumerable<InvoiceLine> lines)
    {
        using var wb = new XLWorkbook();

        // 1) crear la hoja
        var ws = wb.Worksheets.Add("Datos");

        // 2) insertar la tabla a partir de A1
        ws.Cell(1, 1).InsertTable(lines);

        // 3) auto-ajustar anchos (sobre la hoja, no sobre el libro)
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
