using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ShaFlyRest.Cloud.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ScanController : ControllerBase
    {
        private static readonly IncrementalHash Hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        private readonly ILogger<ScanController> _logger;

        public ScanController(ILogger<ScanController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            return Ok(new
            {
                status = "success",
                message = "hello world!"
            });
        }

        [HttpPost]
        public async Task<IActionResult> Scan()
        {
            var contextReader = Request.BodyReader;
            var ct = HttpContext.RequestAborted;

            while (!ct.IsCancellationRequested)
            {
                var contextReadResult = await contextReader.ReadAsync(ct);
                var contextBuffer = contextReadResult.Buffer;

                if (contextBuffer.IsSingleSegment)
                {
                    ProcessBlock(contextBuffer.FirstSpan, ct);
                }
                else
                {
                    foreach (var segment in contextBuffer)
                    {
                        ProcessBlock(segment.Span, ct);
                    }
                }
                
                contextReader.AdvanceTo(contextBuffer.End);

                if (contextReadResult.IsCompleted)
                {
                    _logger.LogInformation("Reader is completed!");
                    break;
                }
            }

            var hash = HashToString(Hasher.GetHashAndReset());
            _logger.LogInformation($"Calculated hash is: {hash}");
            return Ok(hash);
        }
        
        private void ProcessBlock(ReadOnlySpan<byte> block, CancellationToken ct)
        {
            _logger.LogInformation($"Processing {block.Length} byes");
            Hasher.AppendData(block);
        }

        private static string HashToString(byte[] hash)
        {
            var sb = new StringBuilder();

            foreach (var c in hash)
            {
                sb.Append(c.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}