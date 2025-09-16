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
        private static readonly Regex DetailRegex = new Regex(
            @"^(?<concepto>.+?)\s+(?<instrumento>[\p{L}. ]+?)\s+(?<fdesde>\d{2}/\d{2}/\d{2,4})\s*a\s*(?<fhasta>\d{2}/\d{2}/\d{2,4})\s+(?<hdesde>\d{2}:\d{2})\s*a\s*(?<hhasta>\d{2}:\d{2})\s+(?<dias>\d+)\s+(?<horas>\d+)\s+(?<unit>[\d.,]+)\s+(?<sub>[\d.,]+)\s*$",
            RegexOptions.Compiled);

        public PdfTextExtractor(string pdftotextExe)
        {
            // En Linux, pdftotext está en el PATH y no necesita verificación de archivo
            if (pdftotextExe != "pdftotext" && !File.Exists(pdftotextExe))
                throw new FileNotFoundException("No se encontró pdftotext.exe", pdftotextExe);

            _pdftotextExe = pdftotextExe;
        }

        public IEnumerable<InvoiceLine> Extract(string pdfPath)
        {
            // Nota: ya no descartamos PDFs marcados como "DUPLICADO" aquí.
            // La decisión de procesar originales vs duplicados se toma a nivel de controlador por ZIP.
            
            // Debug: guardar sample de este PDF
            var fileName = Path.GetFileName(pdfPath);
            Console.WriteLine($"[DEBUG] Extrayendo desde: {fileName}");
        
            // 1 - extraer texto, con fallback a PdfPig si pdftotext no está disponible
            string text = string.Empty;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _pdftotextExe,
                    Arguments = $"-layout -enc UTF-8 \"{pdfPath}\" -",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi)!)
                {
                    text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                }
            }
            catch
            {
                // Fallback: extraer texto usando PdfPig
                Console.Error.WriteLine("pdftotext no disponible o falló. Usando PdfPig para extracción de texto.");
                using var docForText = PdfDocument.Open(pdfPath);
                var sbAll = new StringBuilder();
                foreach (var page in docForText.GetPages())
                {
                    sbAll.AppendLine(page.Text);
                }
                text = sbAll.ToString();
            }
            
            return ExtractFromText(text);
        }
        
        public IEnumerable<InvoiceLine> ExtractFromText(string text)
        {
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
                bool hasConcepto = norm.Contains("concepto");
                bool hasInstrumento = norm.Contains("instrumento");
                bool hasFechas = norm.Contains("fechas");
                bool hasHorario = norm.Contains("horario");
                // Also try "dias" without accent in case of encoding issues
                bool hasDias = norm.Contains("días") || norm.Contains("dias") || l.Contains("D�as");
                return hasConcepto && hasInstrumento && hasFechas && hasHorario && hasDias;
            });
            if (headerIdx < 0) yield break;

            var header = lines[headerIdx];
            var headerNorm = RemoveDiacritics(header).ToLowerInvariant();

            // Programa: intentar extraer "Programa: <nombre>" en la cabecera
            string programa = string.Empty;
            var progMatchText = Regex.Match(text, @"Programa:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            if (progMatchText.Success)
            {
                programa = progMatchText.Groups[1].Value.Trim();
            }
            else
            {
                for (int k = 0; k < Math.Min(headerIdx, 100); k++)
                {
                    var m = Regex.Match(lines[k], @"Programa:\s*(.+)", RegexOptions.IgnoreCase);
                    if (m.Success) { programa = m.Groups[1].Value.Trim(); break; }
                }
            }

            int posConcept = headerNorm.IndexOf("concepto", StringComparison.Ordinal);
            int posInstr = headerNorm.IndexOf("instrumento", StringComparison.Ordinal);
            int posFecha = headerNorm.IndexOf("fechas", StringComparison.Ordinal);
            int posHora = headerNorm.IndexOf("horario", StringComparison.Ordinal);
            int posDias = Ci.IndexOf(header, "días", CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            if (posDias < 0) posDias = Ci.IndexOf(header, "dias", CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            if (posDias < 0) posDias = header.IndexOf("D�as", StringComparison.Ordinal);


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

                // artista con asterisco: "[*] Nombre" - puede incluir datos en la misma línea
                var starMatch = Regex.Match(trimmed, @"^\[\*\]\s*([^\d\[\]]+?)(?:\s+\d{2}/\d{2}/\d{2,4}|$)");
                if (starMatch.Success)
                {
                    currentArtist = CleanArtistName(starMatch.Groups[1].Value.Trim());
                    // Si la línea también contiene datos (fechas), no hacer continue
                    if (!Regex.IsMatch(trimmed, @"\d{2}/\d{2}/\d{2,4}"))
                    {
                        continue; // Solo el nombre del artista
                    }
                    // Si contiene fechas, continuar procesando la línea como datos
                }

                // artista sin asterisco: solo letras, espacios y algunos caracteres especiales
                // pero NO debe contener fechas, números que parezcan precios o patrones de datos
                var artistOnlyPattern = @"^[A-ZÁÉÍÓÚÜÑ][A-ZÁÉÍÓÚÜÑ\s,\.''-]+$";
                var containsDataPattern = @"\d{2}/\d{2}/\d{2,4}|:\d{2}|\d+\.\d+";
                
                if (Regex.IsMatch(trimmed, artistOnlyPattern) && 
                    !Regex.IsMatch(trimmed, containsDataPattern) &&
                    trimmed.Length <= 50) // Limitar longitud para evitar capturar líneas completas de datos
                {
                    currentArtist = CleanArtistName(trimmed);
                    continue;
                }

                // línea de detalle - usar análisis inteligente para evitar spillover
                var (conceptoTxt, instrumento) = ExtractConceptAndInstrument(line, posConcept, posInstr, posFecha);

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

                // Heurística mejorada: detectar múltiples problemas de formato
                bool suspect = instrumento.Contains("/") || instrumento.Contains(":") || instrumento.Contains(" a ") ||
                              conceptoTxt.Contains("/") || conceptoTxt.Contains(":") ||
                              string.IsNullOrWhiteSpace(fDesde) || string.IsNullOrWhiteSpace(hDesde) ||
                              !Regex.IsMatch(fDesde, @"^\d{2}/\d{2}/\d{2,4}$") ||
                              !Regex.IsMatch(hDesde, @"^\d{2}:\d{2}$") ||
                              conceptoTxt.Length > 30 || instrumento.Length > 25; // Campos demasiado largos
                              
                if (suspect)
                {
                    var m = DetailRegex.Match(line);
                    if (m.Success)
                    {
                        conceptoTxt = m.Groups["concepto"].Value.Trim();
                        instrumento = m.Groups["instrumento"].Value.Trim();
                        fDesde = m.Groups["fdesde"].Value.Trim();
                        fHasta = m.Groups["fhasta"].Value.Trim();
                        hDesde = m.Groups["hdesde"].Value.Trim();
                        hHasta = m.Groups["hhasta"].Value.Trim();
                        unitTxt = m.Groups["unit"].Value.Trim();
                        subTxt = m.Groups["sub"].Value.Trim();
                    }
                }

                // valor numérico mínimo: validamos que tengamos fechas y horas válidas
                // Si fDesde no es una fecha válida, intentar skipear esta línea
                if (string.IsNullOrWhiteSpace(fDesde) || string.IsNullOrWhiteSpace(hDesde) ||
                    !Regex.IsMatch(fDesde, @"^\d{2}/\d{2}/\d{2,4}$"))
                    continue;

                // Debug: mostrar datos extraídos
                Console.WriteLine($"[DEBUG] Line data - Artist: '{currentArtist}', Concept: '{conceptoTxt}', FDesde: '{fDesde}', FHasta: '{fHasta}', Invoice: '{invoice}', FechaEmi: {fechaEmi:yyyy-MM-dd}");
                
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
                    0m, 0m, 0m, 0m, 0m, 0m, 0m,
                    programa
                ));
            }

            // 6 - Extraer información adicional de totales
            decimal subtotalFactura = 0m, aporteOS = 0m, jubilacion = 0m, recursoAdmin = 0m, tasa = 0m, transporte = 0m, totalFactura = 0m;

            // Continuar desde donde terminamos la tabla de detalle
            Console.WriteLine($"[DEBUG] Buscando totales desde línea {i}...");
            for (int j = i; j < lines.Count; j++)
            {
                var line = lines[j].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                if (line.Contains("SUBTOTAL") || line.Contains("Aporte") || line.Contains("Jubilac") || line.Contains("TOTAL"))
                {
                    Console.WriteLine($"[DEBUG] Line {j}: '{line}'");
                }

                // Extraer SUBTOTAL
                var subtotalMatch = Regex.Match(line, @"SUBTOTAL:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (subtotalMatch.Success)
                {
                    subtotalFactura = ParseDecimal(subtotalMatch.Groups[1].Value.Replace(",", ""));
                }

                // Extraer Aporte/Contribución O.S. - más flexible con encoding
                var aporteMatch = Regex.Match(line, @"Aporte.*?O\.?S\.?\s*:?\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (aporteMatch.Success)
                {
                    aporteOS = ParseDecimal(aporteMatch.Groups[1].Value.Replace(",", ""));
                    Console.WriteLine($"[DEBUG] Found Aporte: {aporteOS}");
                }

                // Extraer Jubilación - más flexible con encoding  
                var jubilacionMatch = Regex.Match(line, @"Jubilac.*?:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (jubilacionMatch.Success)
                {
                    jubilacion = ParseDecimal(jubilacionMatch.Groups[1].Value.Replace(",", ""));
                    Console.WriteLine($"[DEBUG] Found Jubilacion: {jubilacion}");
                }

                // Extraer Recurso Administrativo - más flexible
                var recursoMatch = Regex.Match(line, @"Recurso.*?Administrativo.*?:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (recursoMatch.Success)
                {
                    recursoAdmin = ParseDecimal(recursoMatch.Groups[1].Value.Replace(",", ""));
                    Console.WriteLine($"[DEBUG] Found RecursoAdmin: {recursoAdmin}");
                }

                // Extraer Tasa - más flexible
                var tasaMatch = Regex.Match(line, @"Tasa.*?:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (tasaMatch.Success)
                {
                    tasa = ParseDecimal(tasaMatch.Groups[1].Value.Replace(",", ""));
                    Console.WriteLine($"[DEBUG] Found Tasa: {tasa}");
                }

                // Extraer Transporte - más flexible
                var transporteMatch = Regex.Match(line, @"Transporte.*?:\s*\$?\s*([\d,]+\.?\d*)", RegexOptions.IgnoreCase);
                if (transporteMatch.Success)
                {
                    transporte = ParseDecimal(transporteMatch.Groups[1].Value.Replace(",", ""));
                    Console.WriteLine($"[DEBUG] Found Transporte: {transporte}");
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

        private static string SliceWithContext(string text, int start, int end, string fieldType = "")
        {
            if (start < 0 || end <= start || start >= text.Length)
                return string.Empty;
            if (end > text.Length)
                end = text.Length;
            
            var result = text.Substring(start, end - start).Trim();
            
            // Limpiar caracteres especiales y múltiples espacios
            result = Regex.Replace(result, @"\s+", " ");
            
            // Estrategia más inteligente para detectar desbordamiento
            var words = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 1) return result;
            
            var cleanWords = new List<string>();
            var probableOverflow = false;
            
            // Patrones conocidos que pueden ser válidos en campos de concepto/instrumento
            var validConceptPatterns = new[] { "horas", "permanencia", "ejecutante", "musical", "de" };
            var validInstrumentPatterns = new[] { "guitarra", "piano", "violin", "bajo", "bateria", "voz", "canto", "eléctrica", "acústica", "clásico" };
            
            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i].ToLowerInvariant();
                var originalWord = words[i];
                
                // Detectar patrones que claramente indican desbordamiento a otras columnas
                bool isDatePattern = Regex.IsMatch(originalWord, @"^\d{2}/\d{2}(/\d{2,4})?$");
                bool isTimePattern = Regex.IsMatch(originalWord, @"^\d{2}:\d{2}$");
                bool isNumericValue = Regex.IsMatch(originalWord, @"^\d+(\.\d+)?$") && originalWord.Length > 1;
                bool isDateConnector = originalWord.Equals("a", StringComparison.OrdinalIgnoreCase);
                
                // Si encontramos patrones de fecha/hora/números, verificar el contexto
                if (isDatePattern || isTimePattern || isNumericValue)
                {
                    // Si ya tenemos contenido válido y encontramos estos patrones, probablemente es desbordamiento
                    if (cleanWords.Count > 0)
                    {
                        // Verificar si los siguientes elementos también son fechas/números (confirma desbordamiento)
                        bool hasMoreNumericAfter = false;
                        for (int j = i + 1; j < Math.Min(i + 3, words.Length); j++)
                        {
                            if (Regex.IsMatch(words[j], @"^\d+|^\d{2}[:/]\d{2}") || words[j] == "a")
                            {
                                hasMoreNumericAfter = true;
                                break;
                            }
                        }
                        
                        if (hasMoreNumericAfter)
                        {
                            probableOverflow = true;
                            break;
                        }
                    }
                }
                
                // Incluir conectores válidos
                if (isDateConnector && cleanWords.Count > 0 && i < words.Length - 1)
                {
                    // Solo incluir "a" si está entre palabras válidas (no entre números)
                    var prevWordValid = cleanWords.Count > 0 && !Regex.IsMatch(cleanWords.Last(), @"^\d+$");
                    var nextWordValid = i + 1 < words.Length && !Regex.IsMatch(words[i + 1], @"^\d{2}/\d{2}");
                    if (prevWordValid && nextWordValid)
                    {
                        cleanWords.Add(originalWord);
                    }
                    continue;
                }
                
                // Para campos de concepto, mantener frases comunes
                if (fieldType.Contains("concept", StringComparison.OrdinalIgnoreCase) || fieldType == "")
                {
                    if (validConceptPatterns.Any(p => word.Contains(p)) || 
                        (cleanWords.Count > 0 && validConceptPatterns.Any(p => cleanWords.Last().ToLowerInvariant().Contains(p))))
                    {
                        cleanWords.Add(originalWord);
                        continue;
                    }
                }
                
                // Para campos de instrumento, mantener instrumentos conocidos
                if (fieldType.Contains("instrument", StringComparison.OrdinalIgnoreCase) || fieldType == "")
                {
                    if (validInstrumentPatterns.Any(p => word.Contains(p)))
                    {
                        cleanWords.Add(originalWord);
                        continue;
                    }
                }
                
                // Incluir palabras que no son claramente datos numéricos/fechas
                if (!isDatePattern && !isTimePattern && !isNumericValue)
                {
                    cleanWords.Add(originalWord);
                }
            }
            
            // Si no obtuvimos nada después del filtrado, mantener al menos las primeras palabras válidas
            if (cleanWords.Count == 0 && words.Length > 0)
            {
                // Tomar las primeras palabras que no sean claramente numéricas
                for (int i = 0; i < Math.Min(3, words.Length); i++)
                {
                    if (!Regex.IsMatch(words[i], @"^\d{2}[:/]\d{2}|^\d+$"))
                    {
                        cleanWords.Add(words[i]);
                        if (cleanWords.Count >= 2) break; // Limitar para evitar desbordamiento
                    }
                }
            }
            
            return string.Join(" ", cleanWords).Trim();
        }
        
        private static string Slice(string text, int start, int end)
        {
            return SliceWithContext(text, start, end, "");
        }
        
        private static (string concepto, string instrumento) ExtractConceptAndInstrument(string line, int posConcept, int posInstr, int posFecha)
        {
            // Validar límites antes de extraer
            if (posConcept < 0 || posFecha <= posConcept || posConcept >= line.Length)
                return ("", "");
            
            var endPos = Math.Min(posFecha, line.Length);
            var length = endPos - posConcept;
            if (length <= 0) return ("", "");
            
            // Extraer todo el texto desde concepto hasta fechas
            var fullText = line.Substring(posConcept, length).Trim();
            fullText = Regex.Replace(fullText, @"\s+", " ");
            
            var words = fullText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return ("", "");
            
            // Patrones conocidos para determinar dónde termina concepto y empieza instrumento
            var conceptTerminators = new[] { "musical", "permanencia", "permanencia." };
            var instrumentStartWords = new[] { "ejecutante", "guitarra", "piano", "violin", "voz", "bajo", "bateria", "canto" };
            
            var conceptWords = new List<string>();
            var instrumentWords = new List<string>();
            bool switchedToInstrument = false;
            
            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i].ToLowerInvariant().TrimEnd('.', ':');
                
                // Si encontramos una palabra que claramente indica inicio de instrumento
                if (instrumentStartWords.Any(starter => word.Contains(starter)))
                {
                    switchedToInstrument = true;
                    instrumentWords.Add(words[i]);
                    continue;
                }
                
                // Si ya cambiamos a instrumento, todo va a instrumento
                if (switchedToInstrument)
                {
                    instrumentWords.Add(words[i]);
                    continue;
                }
                
                // Si encontramos terminadores de concepto
                if (conceptTerminators.Any(terminator => word.Contains(terminator)))
                {
                    conceptWords.Add(words[i]);
                    // Los siguientes van a instrumento
                    switchedToInstrument = true;
                    continue;
                }
                
                // Heurística: si tenemos conceptos multi-palabra comunes
                bool isConceptWord = false;
                if (conceptWords.Count > 0)
                {
                    var lastConceptWord = conceptWords.Last().ToLowerInvariant();
                    if ((lastConceptWord.Contains("horas") && word == "de") ||
                        (lastConceptWord == "de" && word.Contains("permanencia")))
                    {
                        isConceptWord = true;
                    }
                }
                else if (word.Contains("horas") || word == "[*]")
                {
                    isConceptWord = true;
                }
                
                if (isConceptWord)
                {
                    conceptWords.Add(words[i]);
                }
                else
                {
                    // Si no es claramente concepto y tenemos algo ya, probablemente es instrumento
                    if (conceptWords.Count > 0)
                    {
                        switchedToInstrument = true;
                        instrumentWords.Add(words[i]);
                    }
                    else
                    {
                        // Primera palabra por defecto va a concepto
                        conceptWords.Add(words[i]);
                    }
                }
            }
            
            // Limpiar resultados
            var concepto = string.Join(" ", conceptWords).Trim().TrimEnd('.', ':');
            var instrumento = string.Join(" ", instrumentWords).Trim();
            
            // Si instrumento está vacío pero concepto tiene "Ejecutante Musical", redistribuir
            if (string.IsNullOrWhiteSpace(instrumento) && concepto.Contains("Ejecutante Musical"))
            {
                var conceptParts = concepto.Split(new[] { "Ejecutante Musical" }, StringSplitOptions.None);
                if (conceptParts.Length > 1)
                {
                    concepto = conceptParts[0].Trim().TrimEnd('.', ':');
                    instrumento = "Ejecutante Musical" + conceptParts[1].Trim();
                }
            }
            
            return (concepto, instrumento);
        }

        private static int ParseInt(string txt) =>
            int.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private static decimal ParseDecimal(string txt) =>
            decimal.TryParse(txt, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

        private static string CleanArtistName(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
                return artistName;

            // Limpiar múltiples espacios
            artistName = Regex.Replace(artistName, @"\s+", " ");
            
            // Remover texto de instrumentos/roles que puede haberse colado
            artistName = Regex.Replace(artistName, @"\s+(Ejecutante\s+Musical|Guitarra|Piano|Violin|Bateria|Bajo).*$", "", RegexOptions.IgnoreCase);
            
            return artistName.Trim();
        }
    }
}
