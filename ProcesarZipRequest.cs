// Models/ProcesarZipRequest.cs
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

public class ProcesarZipRequest
{
    [Required]
    [Display(Name = "zipFile")]
    public IFormFile ZipFile { get; set; } = default!;
}
