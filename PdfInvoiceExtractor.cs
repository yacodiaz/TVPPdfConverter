using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TVPPdfConverter.Models;
using UglyToad.PdfPig;

namespace TVPPdfConverter.Services
{
    public sealed class PdfTextExtractor
    {
        private readonly string _pdftotextExe;
        private static readonly CompareInfo Ci = new CultureInfo("es-ES").CompareInfo;
        private static readonly Regex MoneyRegex = new Regex(@"\d+\.\d+", RegexOptions.Compiled);

        public PdfTextExtractor(string pdftotextExe)
        {
            // En Linux, pdftotext está en el PATH y no necesita verificación de archivo
            if (pdftotextExe != "pdftotext" && !File.Exists(pdftotextExe))
                throw new FileNotFoundException("No se encontró pdftotext.exe", pdftotextExe);

            _pdftotextExe = pdftotextExe;
        }

        public IEnumerable<InvoiceLine> Extract(string pdfPath)
        {
            // 0 – descartar si está marcado "DUPLICADO"
            using var dupDoc = PdfDocument.Open(pdfPath);
            if (dupDoc.GetPage(1).Text
                       .IndexOf("DUPLICADO", StringComparison.OrdinalIgnoreCase) >= 0)
                yield break;

            // 1 – extraer texto con pdftotext en UTF-8
            var psi = new ProcessStartInfo
            {
                FileName = _pdftotextExe,
                Arguments = $"-layout \"{pdfPath}\" -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            string text;
            using (var p = Process.Start(psi)!)
            {
                text = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
            }

            // 2 – metadatos de cabecera
            var invM = Regex.Match(text, @"PRE\s*LIQUIDACI[ÓO]N\s*Nº\s*(\d+)", RegexOptions.IgnoreCase);
            var invoice = invM.Success ? invM.Groups[1].Value : string.Empty;

            var feM = Regex.Match(text, @"Fecha de emisi[óo]n:\s*([0-9]{2}/[0-9]{2}/[0-9]{4})", RegexOptions.IgnoreCase);
            var feTxt = feM.Success ? feM.Groups[1].Value : string.Empty;
            DateTime.TryParseExact(feTxt, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var fechaEmi);

            // 3 – dividir líneas
            var lines = text
                .Split('\n')
                .Select(l => l.TrimEnd('\r').TrimEnd())
                .ToList();

            // 4 – localizar la fila de cabecera
            var headerIdx = lines.FindIndex(l =>
            {
                var norm = RemoveDiacritics(l).ToLowerInvariant();
                return norm.Contains("concepto")
                    && norm.Contains("instrumento")
                    && norm.Contains("fechas")
                    && norm.Contains("horario");
            });
            if (headerIdx < 0) yield break;

            var header = lines[headerIdx];
            var headerNorm = RemoveDiacritics(header).ToLowerInvariant();

            int posConcept = headerNorm.IndexOf("concepto", StringComparison.Ordinal);
            int posInstr = headerNorm.IndexOf("instrumento", StringComparison.Ordinal);
            int posFecha = headerNorm.IndexOf("fechas", StringComparison.Ordinal);
            int posHora = headerNorm.IndexOf("horario", StringComparison.Ordinal);
            int posDias = Ci.IndexOf(header, "días", CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

            // validamos offsets mínimos
            if (new[] { posConcept, posInstr, posFecha, posHora, posDias }.Any(p => p < 0))
                yield break;

            // 5 – recorrer detalle y manejar artistas con o sin asterisco
            var detailLines = new List<InvoiceLine>();
            string currentArtist = string.Empty;
            int i;
            
            for (i = headerIdx + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                // fin de tabla de detalle - aquí cambiaremos la lógica
                if (trimmed.StartsWith("SUBTOTAL", StringComparison.OrdinalIgnoreCase))
                    break;

                // artista con asterisco: "[*] Nombre"
                var starMatch = Regex.Match(trimmed, @"^\[\*\]\s*(.+)$");
                if (starMatch.Success)
                {
                    currentArtist = starMatch.Groups[1].Value.Trim();
                    continue;
                }

                // artista sin asterisco: solo letras y espacios
                if (Regex.IsMatch(trimmed, @"^[\p{L} ]+$"))
                {
                    currentArtist = trimmed;
                    continue;
                }

                // línea de detalle
                var conceptoTxt = Slice(line, posConcept, posInstr);
                var instrumento = Slice(line, posInstr, posFecha);

                // fechas
                var fechasTxt = Slice(line, posFecha, posHora);
                var fParts = fechasTxt.Split(" a ");
                var fDesde = fParts.ElementAtOrDefault(0) ?? string.Empty;
                var fHasta = fParts.ElementAtOrDefault(1) ?? string.Empty;

                // horas
                var horasTxt = Slice(line, posHora, posDias);
                var hParts = horasTxt.Split(" a ");
                var hDesde = hParts.ElementAtOrDefault(0) ?? string.Empty;
                var hHasta = hParts.ElementAtOrDefault(1) ?? string.Empty;

                // extraer unitario y subtotal con regex para mayor robustez
                var moneyMatches = MoneyRegex.Matches(line)
                                             .Cast<Match>()
                                             .Select(m => m.Value)
                                             .ToList();
                var unitTxt = moneyMatches.Count >= 2
                    ? moneyMatches[moneyMatches.Count - 2]
                    : "0";
                var subTxt = moneyMatches.Count >= 1
                    ? moneyMatches.Last()
                    : "0";

                // valor numérico mínimo: validamos que tengamos fechas y horas
                if (string.IsNullOrWhiteSpace(fDesde) || string.IsNullOrWhiteSpace(hDesde))
                    continue;

                // Crear línea temporal sin los valores adicionales (los agregaremos después)
                detailLines.Add(new InvoiceLine(
                    invoice, fechaEmi,
                    currentArtist,
                    conceptoTxt,
                    instrumento,
                    fDesde, fHasta,
                    hDesde, hHasta,
                    ParseInt(Slice(line, posDias, posHora)),  // días
                    ParseInt(Slice(line, posHora,                       // horas
                                   line.IndexOf(unitTxt, StringComparison.Ordinal))),
                    ParseDecimal(unitTxt),
                    ParseDecimal(subTxt),
                    // Valores temporales - se actualizarán después
                    0m, 0m, 0m, 0m, 0m, 0m, 0m
                ));
            }

            // 6 - Extraer información adicional de totales
            decimal subtotalFactura = 0m, aporteOS = 0m, jubilacion = 0m, recursoAdmin = 0m, tasa = 0m, transporte = 0m, totalFactura = 0m;

            // Continuar desde donde terminamos la tabla de detalle
            for (int j = i; j < lines.Count; j++)
            {
                var line = lines[j].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Extraer SUBTOTAL
                var subtotalMatch = Regex.Match(line, @"SUBTOTAL:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (subtotalMatch.Success)
                {
                    subtotalFactura = ParseDecimal(subtotalMatch.Groups[1].Value.Replace(",", ""));
                }

                // Extraer Aporte/Contribución O.S.
                var aporteMatch = Regex.Match(line, @"Aporte/Contribuci[óo]n\s+O\.S\.:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (aporteMatch.Success)
                {
                    aporteOS = ParseDecimal(aporteMatch.Groups[1].Value.Replace(",", ""));
                }

                // Extraer Jubilación
                var jubilacionMatch = Regex.Match(line, @"Jubilaci[óo]n:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (jubilacionMatch.Success)
                {
                    jubilacion = ParseDecimal(jubilacionMatch.Groups[1].Value.Replace(",", ""));
                }

                // Extraer Recurso Administrativo
                var recursoMatch = Regex.Match(line, @"Recurso\s+Administrativo:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (recursoMatch.Success)
                {
                    recursoAdmin = ParseDecimal(recursoMatch.Groups[1].Value.Replace(",", ""));
                }

                // Extraer Tasa
                var tasaMatch = Regex.Match(line, @"Tasa:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (tasaMatch.Success)
                {
                    tasa = ParseDecimal(tasaMatch.Groups[1].Value.Replace(",", ""));
                }

                // Extraer Transporte
                var transporteMatch = Regex.Match(line, @"Transporte:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (transporteMatch.Success)
                {
                    transporte = ParseDecimal(transporteMatch.Groups[1].Value.Replace(",", ""));
                }

                // Extraer TOTAL
                var totalMatch = Regex.Match(line, @"TOTAL:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (totalMatch.Success)
                {
                    totalFactura = ParseDecimal(totalMatch.Groups[1].Value.Replace(",", ""));
                }
            }

            // 7 - Devolver las líneas con los valores adicionales actualizados
            foreach (var detailLine in detailLines)
            {
                yield return detailLine with 
                { 
                    SubtotalFactura = subtotalFactura,
                    AporteContribucionOS = aporteOS,
                    Jubilacion = jubilacion,
                    RecursoAdministrativo = recursoAdmin,
                    Tasa = tasa,
                    Transporte = transporte,
                    TotalFactura = totalFactura
                };
            }
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string Slice(string text, int start, int end)
        {
            if (start < 0 || end <= start || start >= text.Length)
                return string.Empty;
            if (end > text.Length)
                end = text.Length;
            return text.Substring(start, end - start).Trim();
        }

        private static int ParseInt(string txt) =>
            int.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private static decimal ParseDecimal(string txt) =>
            decimal.TryParse(txt, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
}
