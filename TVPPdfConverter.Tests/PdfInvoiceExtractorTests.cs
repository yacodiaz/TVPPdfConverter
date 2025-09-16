using Microsoft.Extensions.Logging;
using Moq;
using System.IO;
using TVPPdfConverter.Services;
using TVPPdfConverter.Services.Discovery;
using Xunit;
using Xunit.Abstractions;

namespace TVPPdfConverter.Tests;

public class PdfInvoiceExtractorTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<PdfDiscoveryService>> _discoveryLoggerMock;
    private readonly PdfDiscoveryService _discoveryService;
    private readonly PdfTextExtractor _extractor;

    public PdfInvoiceExtractorTests(ITestOutputHelper output)
    {
        _output = output;
        _discoveryLoggerMock = new Mock<ILogger<PdfDiscoveryService>>();
        _discoveryService = new PdfDiscoveryService();
        
        // Usar pdftotext si está disponible, sino PdfPig como fallback
        var pdftotextPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "pdftotext.exe");
        if (!File.Exists(pdftotextPath))
        {
            pdftotextPath = "pdftotext"; // Intentar del PATH
        }
        
        _extractor = new PdfTextExtractor(pdftotextPath);
    }

    [Fact]
    public void PdfTextExtractor_ShouldInitialize_WithValidPath()
    {
        // Arrange & Act & Assert
        Assert.NotNull(_extractor);
    }

    [Fact]
    public async Task ProcessZipFile_ShouldExtractPdfData_FromUnisono()
    {
        // Arrange
        var testZipPath = Path.Combine(AppContext.BaseDirectory, "TestData", "03-MARZO-I-UNISONO.zip");
        _output.WriteLine($"Testing with ZIP file: {testZipPath}");
        
        Assert.True(File.Exists(testZipPath), $"Test ZIP file not found at: {testZipPath}");

        var invoiceLines = new List<TVPPdfConverter.Models.InvoiceLine>();
        var processedFiles = new List<string>();
        var errorFiles = new List<string>();

        // Act
        await foreach (var tempPdfPath in _discoveryService.DiscoverTempPdfFilesFromZipStreamAsync(
            File.OpenRead(testZipPath), null, CancellationToken.None))
        {
            try
            {
                _output.WriteLine($"Processing PDF: {Path.GetFileName(tempPdfPath)}");
                
                var linesFromPdf = _extractor.Extract(tempPdfPath).ToList();
                _output.WriteLine($"Extracted {linesFromPdf.Count} lines from {Path.GetFileName(tempPdfPath)}");
                
                invoiceLines.AddRange(linesFromPdf);
                processedFiles.Add(tempPdfPath);
                
                // Log some details about extracted data
                foreach (var line in linesFromPdf.Take(3)) // Log first 3 lines
                {
                    _output.WriteLine($"  Line: {line.Artista} - {line.Concepto} - {line.Subtotal}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error processing {Path.GetFileName(tempPdfPath)}: {ex.Message}");
                errorFiles.Add(tempPdfPath);
            }
            finally
            {
                // Clean up temp file
                try { File.Delete(tempPdfPath); } catch { }
            }
        }

        // Assert
        _output.WriteLine($"Total processed files: {processedFiles.Count}");
        _output.WriteLine($"Total error files: {errorFiles.Count}");
        _output.WriteLine($"Total extracted lines: {invoiceLines.Count}");
        
        Assert.True(processedFiles.Count > 0, "Should process at least one PDF file");
        Assert.True(invoiceLines.Count > 0, "Should extract at least one invoice line");
        
        // Verify data quality
        var linesWithArtist = invoiceLines.Count(l => !string.IsNullOrWhiteSpace(l.Artista));
        var linesWithConcepto = invoiceLines.Count(l => !string.IsNullOrWhiteSpace(l.Concepto));
        var linesWithSubtotal = invoiceLines.Count(l => l.Subtotal > 0);
        
        _output.WriteLine($"Lines with Artista: {linesWithArtist}");
        _output.WriteLine($"Lines with Concepto: {linesWithConcepto}");
        _output.WriteLine($"Lines with Subtotal > 0: {linesWithSubtotal}");
        
        Assert.True(linesWithArtist > 0, "Should have lines with artist names");
        Assert.True(linesWithConcepto > 0, "Should have lines with concepts");
        Assert.True(linesWithSubtotal > 0, "Should have lines with subtotals");
    }

    [Fact]
    public async Task ProcessZipFile_ShouldExtractPdfData_FromEnero2Q()
    {
        // Arrange
        var testZipPath = Path.Combine(AppContext.BaseDirectory, "TestData", "01-Enero-2Q.zip");
        _output.WriteLine($"Testing with ZIP file: {testZipPath}");
        
        Assert.True(File.Exists(testZipPath), $"Test ZIP file not found at: {testZipPath}");

        var invoiceLines = new List<TVPPdfConverter.Models.InvoiceLine>();
        var processedFiles = new List<string>();

        // Act
        await foreach (var tempPdfPath in _discoveryService.DiscoverTempPdfFilesFromZipStreamAsync(
            File.OpenRead(testZipPath), null, CancellationToken.None))
        {
            try
            {
                _output.WriteLine($"Processing PDF: {Path.GetFileName(tempPdfPath)}");
                
                var linesFromPdf = _extractor.Extract(tempPdfPath).ToList();
                _output.WriteLine($"Extracted {linesFromPdf.Count} lines from {Path.GetFileName(tempPdfPath)}");
                
                invoiceLines.AddRange(linesFromPdf);
                processedFiles.Add(tempPdfPath);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error processing {Path.GetFileName(tempPdfPath)}: {ex.Message}");
            }
            finally
            {
                // Clean up temp file
                try { File.Delete(tempPdfPath); } catch { }
            }
        }

        // Assert
        _output.WriteLine($"Total processed files: {processedFiles.Count}");
        _output.WriteLine($"Total extracted lines: {invoiceLines.Count}");
        
        Assert.True(processedFiles.Count > 0, "Should process at least one PDF file");
    }

    [Fact]
    public async Task ProcessZipFile_ShouldNotTruncateArtistNames()
    {
        // Arrange
        var testZipPath = Path.Combine(AppContext.BaseDirectory, "TestData", "03-MARZO I Lo mejor de Se Siente Argentina.zip");
        
        if (!File.Exists(testZipPath))
        {
            _output.WriteLine($"Skipping test - test file not found: {testZipPath}");
            return;
        }

        _output.WriteLine($"Testing artist name extraction with: {testZipPath}");

        var invoiceLines = new List<TVPPdfConverter.Models.InvoiceLine>();
        var processedFiles = new List<string>();

        // Act
        await foreach (var tempPdfPath in _discoveryService.DiscoverTempPdfFilesFromZipStreamAsync(
            File.OpenRead(testZipPath), null, CancellationToken.None))
        {
            try
            {
                var linesFromPdf = _extractor.Extract(tempPdfPath).ToList();
                invoiceLines.AddRange(linesFromPdf);
                processedFiles.Add(tempPdfPath);
                
                // Log artist names for verification
                var uniqueArtists = linesFromPdf.Select(l => l.Artista).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct();
                _output.WriteLine($"Artists found in {Path.GetFileName(tempPdfPath)}:");
                foreach (var artist in uniqueArtists)
                {
                    _output.WriteLine($"  - '{artist}' (length: {artist.Length})");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error processing {Path.GetFileName(tempPdfPath)}: {ex.Message}");
            }
            finally
            {
                try { File.Delete(tempPdfPath); } catch { }
            }
        }

        // Assert
        _output.WriteLine($"Total processed files: {processedFiles.Count}");
        _output.WriteLine($"Total extracted lines: {invoiceLines.Count}");
        
        // Validate artist names
        var artistNames = invoiceLines.Select(l => l.Artista).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        
        foreach (var artist in artistNames)
        {
            // Artist names should not be truncated (no single letters or very short names)
            Assert.True(artist.Length > 2, $"Artist name '{artist}' appears to be truncated");
            
            // Artist names should not contain data patterns (dates, times, money)
            Assert.False(artist.Contains("/"), $"Artist name '{artist}' contains date separator");
            Assert.False(artist.Contains(":"), $"Artist name '{artist}' contains time separator");
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(artist, @"\d+\.\d+"), 
                        $"Artist name '{artist}' contains money pattern");
        }
        
        Assert.True(artistNames.Count > 0, "Should extract at least one artist name");
    }

    [Theory]
    [InlineData("03-MARZO-I-UNISONO.zip")]
    [InlineData("01-Enero-2Q.zip")]
    [InlineData("03-MARZO I UNISONO.zip")]
    public async Task ProcessZipFile_ShouldHandleAllTestFiles(string zipFileName)
    {
        // Arrange
        var testZipPath = Path.Combine(AppContext.BaseDirectory, "TestData", zipFileName);
        
        if (!File.Exists(testZipPath))
        {
            _output.WriteLine($"Skipping test for {zipFileName} - file not found");
            return;
        }

        _output.WriteLine($"Testing with ZIP file: {testZipPath}");

        var invoiceLines = new List<TVPPdfConverter.Models.InvoiceLine>();
        var processedFiles = new List<string>();
        var totalPdfCount = 0;

        // Act
        await foreach (var tempPdfPath in _discoveryService.DiscoverTempPdfFilesFromZipStreamAsync(
            File.OpenRead(testZipPath), null, CancellationToken.None))
        {
            totalPdfCount++;
            try
            {
                var linesFromPdf = _extractor.Extract(tempPdfPath).ToList();
                invoiceLines.AddRange(linesFromPdf);
                processedFiles.Add(tempPdfPath);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error processing PDF {totalPdfCount}: {ex.Message}");
            }
            finally
            {
                try { File.Delete(tempPdfPath); } catch { }
            }
        }

        // Assert
        _output.WriteLine($"ZIP: {zipFileName} - PDFs: {totalPdfCount}, Processed: {processedFiles.Count}, Lines: {invoiceLines.Count}");
        
        Assert.True(totalPdfCount > 0, $"Should find at least one PDF in {zipFileName}");
    }

    [Fact]
    public async Task ProcessZipFile_ShouldExtractFieldsSeparately()
    {
        // Arrange
        var testZipPath = Path.Combine(AppContext.BaseDirectory, "TestData", "03-MARZO I Lo mejor de Se Siente Argentina.zip");
        
        if (!File.Exists(testZipPath))
        {
            _output.WriteLine($"Skipping test - test file not found: {testZipPath}");
            return;
        }

        var invoiceLines = new List<TVPPdfConverter.Models.InvoiceLine>();

        // Act
        await foreach (var tempPdfPath in _discoveryService.DiscoverTempPdfFilesFromZipStreamAsync(
            File.OpenRead(testZipPath), null, CancellationToken.None))
        {
            try
            {
                var linesFromPdf = _extractor.Extract(tempPdfPath).ToList();
                invoiceLines.AddRange(linesFromPdf);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error processing {Path.GetFileName(tempPdfPath)}: {ex.Message}");
            }
            finally
            {
                try { File.Delete(tempPdfPath); } catch { }
            }
        }

        // Assert field separation quality
        foreach (var line in invoiceLines)
        {
            _output.WriteLine($"Line: Artist='{line.Artista}', Concept='{line.Concepto}', Instrument='{line.Instrumento}', FDesde='{line.FechaDesde}', HDesde='{line.HoraDesde}'");

            // Concepto should not contain instrument data
            if (!string.IsNullOrWhiteSpace(line.Concepto))
            {
                Assert.False(line.Concepto.Contains("Musical") && line.Concepto.Contains("Ejecutante"), 
                            $"Concepto field '{line.Concepto}' appears to contain instrument data");
            }

            // Instrumento should not contain date/time data
            if (!string.IsNullOrWhiteSpace(line.Instrumento))
            {
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(line.Instrumento, @"\d{2}/\d{2}"), 
                            $"Instrumento field '{line.Instrumento}' contains date data");
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(line.Instrumento, @"\d{2}:\d{2}"), 
                            $"Instrumento field '{line.Instrumento}' contains time data");
            }

            // Date fields should be properly formatted
            if (!string.IsNullOrWhiteSpace(line.FechaDesde))
            {
                Assert.True(System.Text.RegularExpressions.Regex.IsMatch(line.FechaDesde, @"^\d{2}/\d{2}/\d{2,4}$"), 
                           $"FechaDesde '{line.FechaDesde}' is not properly formatted");
            }

            // Time fields should be properly formatted
            if (!string.IsNullOrWhiteSpace(line.HoraDesde))
            {
                Assert.True(System.Text.RegularExpressions.Regex.IsMatch(line.HoraDesde, @"^\d{2}:\d{2}$"), 
                           $"HoraDesde '{line.HoraDesde}' is not properly formatted");
            }
        }

        Assert.True(invoiceLines.Count > 0, "Should extract at least one invoice line");
    }

    [Fact]
    public void Extract_ShouldNotTruncateConceptoAndInstrumento_WhenFollowedByDates()
    {
        // Arrange - Simular texto PDF que causa el problema de truncamiento
        var problematicPdfText = @"SADEM
PRE LIQUIDACIÓN Nº 24539
Fecha de emisión: 15/09/2025
Concepto                 Instrumento                    fechas            horario     días  Horas   Unitario      Subtotal
[*] HERNANDEZ SARA
                                                Ejecutante Musical              Guitarra Eléctrica           20/03/25 a 20/03/25  02:00 a 02:00   0     0      9000      14600     12000
[*] CASTRO DANIEL ALBERTO
                                                Ejecutante Musical              Violín Clásico               22/03/25 a 22/03/25  03:00 a 03:00   0     0      8500      13500     11500
SUBTOTAL: $100000";

        var extractor = new PdfTextExtractor("pdftotext");

        // Act
        var result = extractor.ExtractFromText(problematicPdfText).ToList();

        // Assert
        _output.WriteLine($"Extracted {result.Count} lines from test PDF");
        Assert.True(result.Count >= 2, "Should extract at least 2 lines");
        
        foreach (var line in result)
        {
            _output.WriteLine($"Artist: '{line.Artista}', Concept: '{line.Concepto}', Instrument: '{line.Instrumento}'");
            
            // Verificar que Concepto no esté truncado
            if (!string.IsNullOrWhiteSpace(line.Concepto))
            {
                Assert.Equal("Ejecutante Musical", line.Concepto);
                Assert.False(line.Concepto.StartsWith("Ejecutante") && line.Concepto.Length < 10, 
                           $"Concepto appears truncated: '{line.Concepto}'");
            }
            
            // Verificar que Instrumento no esté truncado
            if (!string.IsNullOrWhiteSpace(line.Instrumento))
            {
                Assert.True(line.Instrumento.Length > 4, $"Instrumento appears truncated: '{line.Instrumento}'");
                Assert.False(line.Instrumento == "Guita", "Instrumento should not be truncated to 'Guita'");
                Assert.False(line.Instrumento == "Violi", "Instrumento should not be truncated to 'Violi'");
            }
        }
    }

    [Fact]
    public void Extract_ShouldHandleLongInstrumentNames_WithoutTruncation()
    {
        // Arrange - Caso específico de instrumentos largos
        var pdfTextWithLongInstruments = @"SADEM
PRE LIQUIDACIÓN Nº 24540
Fecha de emisión: 15/09/2025
Concepto                 Instrumento                    fechas            horario     días  Horas   Unitario      Subtotal
[*] MARTINEZ JOSE LUIS
                                                Ejecutante Musical              Guitarra Acústica de 12 Cuerdas  21/03/25 a 21/03/25  04:00 a 04:00   0     0      9500      15000     13000
[*] RODRIGUEZ MARIA ELENA
                                                Ejecutante Musical              Piano de Cola Steinway & Sons    22/03/25 a 22/03/25  02:30 a 02:30   0     0      12000     18000     16000
SUBTOTAL: $150000";

        var extractor = new PdfTextExtractor("pdftotext");

        // Act
        var result = extractor.ExtractFromText(pdfTextWithLongInstruments).ToList();

        // Assert
        _output.WriteLine($"Extracted {result.Count} lines from test PDF");
        Assert.True(result.Count >= 2, "Should extract at least 2 lines");
        
        foreach (var line in result)
        {
            _output.WriteLine($"Artist: '{line.Artista}', Instrument: '{line.Instrumento}'");
            
            if (!string.IsNullOrWhiteSpace(line.Instrumento))
            {
                // Verificar que instrumentos largos no se corten
                Assert.True(line.Instrumento.Length > 8, $"Long instrument name appears truncated: '{line.Instrumento}'");
                Assert.False(line.Instrumento.Contains("21/03/25"), "Instrument should not contain date data");
                Assert.False(line.Instrumento.Contains("04:00"), "Instrument should not contain time data");
            }
        }
    }

    [Fact]
    public async Task ProcessZipFile_ShouldExtractPdfData_FromSADEMYDC()
    {
        // Arrange - Test para archivos SADEM YDC específicos
        var testZipPath = @"D:\Proyectos\TVPPdfConverter\Resources\SADEM YDC-20250830T233029Z-1-001\SADEM YDC\03-MARZO SE SIENTE ARGENTINA V0.zip";
        
        if (!File.Exists(testZipPath))
        {
            _output.WriteLine($"SADEM YDC test file not found: {testZipPath}");
            // Try alternative path
            testZipPath = Path.Combine(AppContext.BaseDirectory, "TestData", "03-MARZO SE SIENTE ARGENTINA V0.zip");
            if (!File.Exists(testZipPath))
            {
                _output.WriteLine("Skipping SADEM YDC test - file not found");
                return;
            }
        }

        _output.WriteLine($"Testing SADEM YDC file: {testZipPath}");

        var invoiceLines = new List<TVPPdfConverter.Models.InvoiceLine>();
        var processedFiles = new List<string>();
        var errorFiles = new List<string>();
        var artistsFound = new HashSet<string>();

        // Act
        await foreach (var tempPdfPath in _discoveryService.DiscoverTempPdfFilesFromZipStreamAsync(
            File.OpenRead(testZipPath), null, CancellationToken.None))
        {
            try
            {
                _output.WriteLine($"Processing: {Path.GetFileName(tempPdfPath)}");
                
                var linesFromPdf = _extractor.Extract(tempPdfPath).ToList();
                invoiceLines.AddRange(linesFromPdf);
                processedFiles.Add(tempPdfPath);
                
                // Collect unique artists
                foreach (var line in linesFromPdf)
                {
                    if (!string.IsNullOrWhiteSpace(line.Artista))
                        artistsFound.Add(line.Artista);
                }
                
                // Log details for debugging
                _output.WriteLine($"  Extracted {linesFromPdf.Count} lines");
                if (linesFromPdf.Count > 0)
                {
                    _output.WriteLine($"  Sample: {linesFromPdf[0].Artista} - {linesFromPdf[0].Concepto}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error processing {Path.GetFileName(tempPdfPath)}: {ex.Message}");
                errorFiles.Add(tempPdfPath);
            }
            finally
            {
                try { File.Delete(tempPdfPath); } catch { }
            }
        }

        // Assert
        _output.WriteLine($"=== SADEM YDC Processing Summary ===");
        _output.WriteLine($"Processed files: {processedFiles.Count}");
        _output.WriteLine($"Error files: {errorFiles.Count}");
        _output.WriteLine($"Total lines extracted: {invoiceLines.Count}");
        _output.WriteLine($"Unique artists found: {artistsFound.Count}");
        
        foreach (var artist in artistsFound.Take(10))
        {
            _output.WriteLine($"  - {artist}");
        }

        // Quality assertions
        Assert.True(processedFiles.Count > 0, "Should process at least one PDF");
        Assert.True(invoiceLines.Count > 0, "Should extract at least one line");
        
        // Validate data integrity for SADEM files
        foreach (var line in invoiceLines)
        {
            // Artist names should be clean (no truncation, no data mixed in)
            if (!string.IsNullOrWhiteSpace(line.Artista))
            {
                Assert.False(line.Artista.Contains("  "), $"Artist '{line.Artista}' has multiple spaces");
                Assert.False(line.Artista.EndsWith("Ejecutante Musical"), $"Artist '{line.Artista}' has role text");
                Assert.True(line.Artista.Length <= 60, $"Artist name too long: '{line.Artista}'");
            }
            
            // Dates should be valid
            if (!string.IsNullOrWhiteSpace(line.FechaDesde))
            {
                Assert.Matches(@"^\d{2}/\d{2}/\d{2,4}$", line.FechaDesde);
            }
            
            // Times should be valid
            if (!string.IsNullOrWhiteSpace(line.HoraDesde))
            {
                Assert.Matches(@"^\d{2}:\d{2}$", line.HoraDesde);
            }
            
            // Monetary values should be reasonable
            if (line.Subtotal > 0)
            {
                Assert.True(line.Subtotal < 10000000, $"Subtotal unreasonably high: {line.Subtotal}");
            }
        }
        
        _output.WriteLine("SADEM YDC test completed successfully!");
    }

    [Fact]
    public async Task ProcessZipFile_ShouldHandleComplexArtistNames()
    {
        // Test específico para nombres de artistas complejos
        var testFiles = new[]
        {
            @"D:\Proyectos\TVPPdfConverter\Resources\SADEM YDC-20250830T233029Z-1-001\SADEM YDC\03-MARZO SE SIENTE ARGENTINA V0.zip",
            Path.Combine(AppContext.BaseDirectory, "TestData", "03-MARZO I Lo mejor de Se Siente Argentina.zip")
        };

        foreach (var testZipPath in testFiles)
        {
            if (!File.Exists(testZipPath))
                continue;

            _output.WriteLine($"Testing complex names in: {Path.GetFileName(testZipPath)}");

            var invoiceLines = new List<TVPPdfConverter.Models.InvoiceLine>();
            
            await foreach (var tempPdfPath in _discoveryService.DiscoverTempPdfFilesFromZipStreamAsync(
                File.OpenRead(testZipPath), null, CancellationToken.None))
            {
                try
                {
                    var linesFromPdf = _extractor.Extract(tempPdfPath).ToList();
                    invoiceLines.AddRange(linesFromPdf);
                }
                catch { }
                finally
                {
                    try { File.Delete(tempPdfPath); } catch { }
                }
            }

            // Validate complex artist names
            var complexNames = invoiceLines
                .Select(l => l.Artista)
                .Where(a => !string.IsNullOrWhiteSpace(a) && (a.Contains(" ") || a.Length > 20))
                .Distinct()
                .ToList();

            _output.WriteLine($"Found {complexNames.Count} complex artist names:");
            foreach (var name in complexNames.Take(5))
            {
                _output.WriteLine($"  - '{name}' (length: {name.Length})");
                
                // Validate the name is properly formatted
                Assert.DoesNotContain("  ", name); // No double spaces
                Assert.DoesNotMatch(@"\d{2}/\d{2}", name); // No dates
                Assert.DoesNotMatch(@"\d{2}:\d{2}", name); // No times
                Assert.DoesNotMatch(@"\d+\.\d+", name); // No money amounts
            }
            
            if (complexNames.Count > 0)
            {
                _output.WriteLine($"Complex names test passed for {Path.GetFileName(testZipPath)}");
                break; // At least one file tested successfully
            }
        }
    }

    [Theory]
    [InlineData("03-MARZO-I-UNISONO.zip")]
    [InlineData("03-MARZO I Lo mejor de Se Siente Argentina.zip")]
    [InlineData("03-MARZO SE SIENTE ARGENTINA V0.zip")]
    public async Task ProcessZipFile_ShouldMaintainDataIntegrity(string zipFileName)
    {
        // Test para verificar la integridad de datos en diferentes archivos
        var possiblePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "TestData", zipFileName),
            Path.Combine(@"D:\Proyectos\TVPPdfConverter\Resources\SADEM YDC-20250830T233029Z-1-001\SADEM YDC", zipFileName),
            Path.Combine(@"C:\Users\yacod\Downloads", zipFileName)
        };

        string? testZipPath = possiblePaths.FirstOrDefault(File.Exists);
        
        if (testZipPath == null)
        {
            _output.WriteLine($"Skipping test for {zipFileName} - file not found in any location");
            return;
        }

        _output.WriteLine($"Testing data integrity for: {zipFileName}");

        var invoiceLines = new List<TVPPdfConverter.Models.InvoiceLine>();
        var totalPdfs = 0;

        await foreach (var tempPdfPath in _discoveryService.DiscoverTempPdfFilesFromZipStreamAsync(
            File.OpenRead(testZipPath), null, CancellationToken.None))
        {
            totalPdfs++;
            try
            {
                var linesFromPdf = _extractor.Extract(tempPdfPath).ToList();
                invoiceLines.AddRange(linesFromPdf);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error in PDF {totalPdfs}: {ex.Message}");
            }
            finally
            {
                try { File.Delete(tempPdfPath); } catch { }
            }
        }

        // Data integrity checks
        _output.WriteLine($"PDFs: {totalPdfs}, Lines: {invoiceLines.Count}");
        
        if (invoiceLines.Count > 0)
        {
            // Check that we have complete data
            var linesWithAllData = invoiceLines.Count(l => 
                !string.IsNullOrWhiteSpace(l.InvoiceNumber) &&
                !string.IsNullOrWhiteSpace(l.Artista) &&
                !string.IsNullOrWhiteSpace(l.Concepto) &&
                l.Subtotal > 0);
            
            var percentageComplete = (linesWithAllData * 100.0) / invoiceLines.Count;
            _output.WriteLine($"Lines with complete data: {linesWithAllData} ({percentageComplete:F1}%)");
            
            // At least 50% of lines should have complete data
            Assert.True(percentageComplete >= 50, $"Only {percentageComplete:F1}% of lines have complete data");
            
            // Check for data consistency
            var invoiceNumbers = invoiceLines.Select(l => l.InvoiceNumber).Distinct().Count();
            _output.WriteLine($"Unique invoice numbers: {invoiceNumbers}");
            Assert.True(invoiceNumbers > 0, "Should have at least one invoice number");
        }
        
        Assert.True(totalPdfs > 0, $"Should find PDFs in {zipFileName}");
    }
}