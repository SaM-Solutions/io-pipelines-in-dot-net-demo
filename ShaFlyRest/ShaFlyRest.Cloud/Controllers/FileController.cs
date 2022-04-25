using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MediaTypeHeaderValue = System.Net.Http.Headers.MediaTypeHeaderValue;

namespace ShaFlyRest.Cloud.Controllers;

[ApiController]
[Route("[controller]")]
public class FileController : ControllerBase
{
    private const string FilePath = "/Users/mrfishchev/git/ShaFlyRest/testfile";

    [HttpGet]
    public async Task<IActionResult> GetFile(CancellationToken ct)
    {
        var bytes = await System.IO.File.ReadAllBytesAsync(FilePath, ct);
        return File(bytes, MediaTypeNames.Application.Octet);
    }
}