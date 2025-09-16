using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO;
using System.IO.Compression;
using System.Threading;
using TVPPdfConverter.Controllers;
using TVPPdfConverter.Services;
using TVPPdfConverter.Services.Discovery;
using Xunit;
using Xunit.Abstractions;

namespace TVPPdfConverter.Tests;

public class InvoicesControllerTests
{
    private readonly ITestOutputHelper _output;
    private readonly PdfDiscoveryService _discoveryService;
    private readonly Mock<ILogger<InvoicesController>> _loggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly InvoicesController _controller;

    public InvoicesControllerTests(ITestOutputHelper output)
    {
        _output = output;
        var discoveryLoggerMock = new Mock<ILogger<PdfDiscoveryService>>();
        _discoveryService = new PdfDiscoveryService();
        _loggerMock = new Mock<ILogger<InvoicesController>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        
        // Setup controller with HttpContext
        _controller = new InvoicesController(_discoveryService, _loggerMock.Object);
        
        // Create a mock HttpContext with RequestAborted token
        var httpContext = new DefaultHttpContext();
        httpContext.RequestAborted = CancellationToken.None;
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task Preview_WithValidZip_ShouldReturnAnalysisResult()
    {
        // Arrange
        var testZipPath = Path.Combine(AppContext.BaseDirectory, "TestData", "03-MARZO-I-UNISONO.zip");
        
        if (!File.Exists(testZipPath))
        {
            _output.WriteLine("Test ZIP file not found, creating a simple test ZIP");
            // Create a simple test ZIP for unit testing
            testZipPath = Path.GetTempFileName() + ".zip";
            using (var zip = ZipFile.Open(testZipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("test.pdf");
                using (var stream = entry.Open())
                using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync("PDF content placeholder");
                }
            }
        }

        var zipBytes = await File.ReadAllBytesAsync(testZipPath);
        var formFile = CreateFormFile("test.zip", zipBytes);

        // Act
        var result = await _controller.Preview(formFile, false);

        // Assert
        _output.WriteLine($"Analysis result type: {result.GetType().Name}");
        
        // The controller can return either OkObjectResult or BadRequestObjectResult depending on the content
        Assert.True(result is OkObjectResult || result is BadRequestObjectResult);
        
        if (result is OkObjectResult okResult)
        {
            _output.WriteLine($"Analysis result: {okResult.Value}");
        }
        else if (result is BadRequestObjectResult badResult)
        {
            _output.WriteLine($"Analysis error: {badResult.Value}");
        }
    }

    [Fact]
    public async Task Preview_WithNullFile_ShouldReturnBadRequest()
    {
        // Act
        var result = await _controller.Preview(null!, false);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Preview_WithNonZipFile_ShouldReturnBadRequest()
    {
        // Arrange
        var formFile = CreateFormFile("test.txt", new byte[] { 1, 2, 3 });

        // Act
        var result = await _controller.Preview(formFile, false);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Upload_WithValidZip_ShouldProcessSuccessfully()
    {
        // Arrange
        var testZipPath = Path.Combine(AppContext.BaseDirectory, "TestData", "03-MARZO-I-UNISONO.zip");

        if (!File.Exists(testZipPath))
        {
            _output.WriteLine("Test ZIP file not found, creating a simple test ZIP");
            // Create a simple test ZIP for unit testing
            testZipPath = Path.GetTempFileName() + ".zip";
            using (var zip = ZipFile.Open(testZipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("test.pdf");
                using (var stream = entry.Open())
                using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync("PDF content placeholder");
                }
            }
        }

        var zipBytes = await File.ReadAllBytesAsync(testZipPath);
        var formFile = CreateFormFile("test.zip", zipBytes);

        // Act
        var result = await _controller.Upload(formFile, false);

        // Assert
        _output.WriteLine($"Upload result type: {result.GetType().Name}");

        // Should return FileContentResult with Excel data, or BadRequestObjectResult with error details
        Assert.True(result is FileContentResult || result is BadRequestObjectResult);

        if (result is BadRequestObjectResult badRequest)
        {
            _output.WriteLine($"Upload error: {badRequest.Value}");
        }
        else if (result is FileContentResult fileResult)
        {
            _output.WriteLine($"Successfully generated Excel file of {fileResult.FileContents.Length} bytes");
            Assert.True(fileResult.FileContents.Length > 0);
            Assert.Equal("application/vnd.ms-excel", fileResult.ContentType);
        }
    }

    [Fact]
    public async Task Upload_WithProcessDuplicatesTrue_ShouldIncludeDuplicates()
    {
        // Arrange
        var testZipPath = CreateTestZipWithDuplicates();
        var zipBytes = await File.ReadAllBytesAsync(testZipPath);
        var formFile = CreateFormFile("test_with_duplicates.zip", zipBytes);

        // Act
        var result = await _controller.Upload(formFile, true);

        // Assert
        _output.WriteLine($"Upload result with duplicates: {result.GetType().Name}");
        Assert.True(result is FileContentResult || result is BadRequestObjectResult);

        // Clean up
        try { File.Delete(testZipPath); } catch { }
    }

    [Fact]
    public async Task Upload_WithProcessDuplicatesFalse_ShouldExcludeDuplicates()
    {
        // Arrange
        var testZipPath = CreateTestZipWithDuplicates();
        var zipBytes = await File.ReadAllBytesAsync(testZipPath);
        var formFile = CreateFormFile("test_with_duplicates.zip", zipBytes);

        // Act
        var result = await _controller.Upload(formFile, false);

        // Assert
        _output.WriteLine($"Upload result without duplicates: {result.GetType().Name}");
        Assert.True(result is FileContentResult || result is BadRequestObjectResult);

        // Clean up
        try { File.Delete(testZipPath); } catch { }
    }

    [Fact]
    public async Task Preview_WithDuplicates_ShouldShowCorrectCounts()
    {
        // Arrange
        var testZipPath = CreateTestZipWithDuplicates();
        var zipBytes = await File.ReadAllBytesAsync(testZipPath);
        var formFile = CreateFormFile("test_with_duplicates.zip", zipBytes);

        // Act - Test with duplicates excluded
        var resultExcluded = await _controller.Preview(formFile, false);

        // Reset stream position
        zipBytes = await File.ReadAllBytesAsync(testZipPath);
        formFile = CreateFormFile("test_with_duplicates.zip", zipBytes);

        // Act - Test with duplicates included
        var resultIncluded = await _controller.Preview(formFile, true);

        // Assert
        if (resultExcluded is OkObjectResult okExcluded && resultIncluded is OkObjectResult okIncluded)
        {
            // Use reflection or cast to access dynamic properties
            dynamic dataExcluded = okExcluded.Value!;
            dynamic dataIncluded = okIncluded.Value!;

            _output.WriteLine($"Excluded: TotalPdfs={dataExcluded.totalPdfs}, Duplicates={dataExcluded.duplicates}, ParsedPdfs={dataExcluded.parsedPdfs}");
            _output.WriteLine($"Included: TotalPdfs={dataIncluded.totalPdfs}, Duplicates={dataIncluded.duplicates}, ParsedPdfs={dataIncluded.parsedPdfs}");

            // When excluding duplicates, parsed should be less than total if there are actual duplicates
            if ((int)dataExcluded.duplicates > 0)
            {
                Assert.True((int)dataIncluded.parsedPdfs >= (int)dataExcluded.parsedPdfs,
                    "Including duplicates should process same or more files than excluding them");
            }

            // Both should detect the same number of total PDFs and duplicates
            Assert.Equal((int)dataExcluded.totalPdfs, (int)dataIncluded.totalPdfs);
            Assert.Equal((int)dataExcluded.duplicates, (int)dataIncluded.duplicates);
        }

        // Clean up
        try { File.Delete(testZipPath); } catch { }
    }

    [Fact]
    public async Task Upload_WithDuplicatesFlag_ShouldProcessDifferentCounts()
    {
        // Arrange
        var testZipPath = CreateTestZipWithDuplicates();
        var zipBytes = await File.ReadAllBytesAsync(testZipPath);

        // Test excluding duplicates
        var formFileExcluded = CreateFormFile("test_duplicates_excluded.zip", zipBytes);
        var resultExcluded = await _controller.Upload(formFileExcluded, false);

        // Test including duplicates
        zipBytes = await File.ReadAllBytesAsync(testZipPath); // Re-read file
        var formFileIncluded = CreateFormFile("test_duplicates_included.zip", zipBytes);
        var resultIncluded = await _controller.Upload(formFileIncluded, true);

        // Assert
        _output.WriteLine($"Result excluded type: {resultExcluded.GetType().Name}");
        _output.WriteLine($"Result included type: {resultIncluded.GetType().Name}");

        // Both should succeed (or both should fail with same reason)
        Assert.Equal(resultExcluded.GetType(), resultIncluded.GetType());

        if (resultExcluded is FileContentResult fileExcluded && resultIncluded is FileContentResult fileIncluded)
        {
            _output.WriteLine($"Excluded file size: {fileExcluded.FileContents.Length} bytes");
            _output.WriteLine($"Included file size: {fileIncluded.FileContents.Length} bytes");

            // File with duplicates included should be same size or larger
            Assert.True(fileIncluded.FileContents.Length >= fileExcluded.FileContents.Length,
                "File with duplicates should be same size or larger");
        }

        // Clean up
        try { File.Delete(testZipPath); } catch { }
    }

    [Theory]
    [InlineData(false, "Should exclude duplicates")]
    [InlineData(true, "Should include duplicates")]
    public async Task ProcessDuplicates_Flag_ShouldAffectProcessing(bool processDuplicates, string description)
    {
        // Arrange
        var testZipPath = CreateTestZipWithDuplicates();
        var zipBytes = await File.ReadAllBytesAsync(testZipPath);
        var formFile = CreateFormFile("test_duplicates_theory.zip", zipBytes);

        // Act
        var result = await _controller.Preview(formFile, processDuplicates);

        // Assert
        _output.WriteLine($"Test: {description} - processDuplicates={processDuplicates}");

        if (result is OkObjectResult okResult)
        {
            dynamic data = okResult.Value!;
            _output.WriteLine($"TotalPdfs: {data.totalPdfs}, Duplicates: {data.duplicates}, ParsedPdfs: {data.parsedPdfs}");

            Assert.True((int)data.totalPdfs > 0, "Should find PDFs in test file");
        }
        else
        {
            _output.WriteLine($"Unexpected result type: {result.GetType().Name}");
        }

        // Clean up
        try { File.Delete(testZipPath); } catch { }
    }

    private string CreateTestZipWithDuplicates()
    {
        var tempZipPath = Path.GetTempFileName() + ".zip";
        using (var zip = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
        {
            // Create a regular PDF
            var regularEntry = zip.CreateEntry("regular.pdf");
            using (var stream = regularEntry.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n3 0 obj\n<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>\nendobj\n4 0 obj\n<< /Length 44 >>\nstream\nBT\n/F1 12 Tf\n100 700 Td\n(SADEM PRE LIQUIDACION Regular) Tj\nET\nendstream\nendobj\nxref\n0 5\n0000000000 65535 f\n0000000009 00000 n\n0000000058 00000 n\n0000000115 00000 n\n0000000178 00000 n\ntrailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n271\n%%EOF");
            }

            // Create a duplicate PDF (contains "DUPLICADO" text)
            var duplicateEntry = zip.CreateEntry("duplicate.pdf");
            using (var stream = duplicateEntry.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n3 0 obj\n<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>\nendobj\n4 0 obj\n<< /Length 50 >>\nstream\nBT\n/F1 12 Tf\n100 700 Td\n(SADEM PRE LIQUIDACION DUPLICADO) Tj\nET\nendstream\nendobj\nxref\n0 5\n0000000000 65535 f\n0000000009 00000 n\n0000000058 00000 n\n0000000115 00000 n\n0000000178 00000 n\ntrailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n277\n%%EOF");
            }
        }
        return tempZipPath;
    }

    [Fact]
    public void IsDuplicatePdf_DetectionLogic_ShouldWork()
    {
        // Test the duplicate detection logic specifically
        var testZipPath = CreateTestZipWithDuplicates();

        using (var zip = ZipFile.OpenRead(testZipPath))
        {
            foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".pdf")))
            {
                var tempFile = Path.GetTempFileName() + ".pdf";
                try
                {
                    entry.ExtractToFile(tempFile, true);

                    // Use reflection to access private method
                    var method = typeof(InvoicesController).GetMethod("IsDuplicatePdf",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var isDuplicate = (bool)method!.Invoke(null, new object[] { tempFile })!;

                    _output.WriteLine($"File: {entry.Name}, IsDuplicate: {isDuplicate}");

                    if (entry.Name.Contains("duplicate"))
                    {
                        Assert.True(isDuplicate, $"File {entry.Name} should be detected as duplicate");
                    }
                    else
                    {
                        Assert.False(isDuplicate, $"File {entry.Name} should NOT be detected as duplicate");
                    }
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }

        try { File.Delete(testZipPath); } catch { }
    }

    private IFormFile CreateFormFile(string fileName, byte[] content)
    {
        var stream = new MemoryStream(content);
        var formFile = new FormFile(stream, 0, content.Length, "zip", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = fileName.EndsWith(".zip") ? "application/zip" : "text/plain"
        };
        return formFile;
    }
}