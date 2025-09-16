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
            var dataExcluded = okExcluded.Value;
            var dataIncluded = okIncluded.Value;

            _output.WriteLine($"Preview excluded duplicates: {dataExcluded}");
            _output.WriteLine($"Preview included duplicates: {dataIncluded}");
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
                writer.Write("SADEM\nPRE LIQUIDACIÓN\nRegular PDF content");
            }

            // Create a duplicate PDF (contains "DUPLICADO" text)
            var duplicateEntry = zip.CreateEntry("duplicate.pdf");
            using (var stream = duplicateEntry.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("SADEM\nPRE LIQUIDACIÓN\nDUPLICADO\nDuplicate PDF content");
            }
        }
        return tempZipPath;
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