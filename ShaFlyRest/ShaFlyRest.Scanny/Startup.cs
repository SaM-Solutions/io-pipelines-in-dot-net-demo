using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace ShaFlyRest.Scanny
{
    public record FinalResponse(string LocalSha, string RemoteSha);
    
    public class Startup
    {
        private static readonly HttpClient HttpClient = new();
        private const string CloudEndpoint = "http://localhost:5100/scan";
        private readonly IncrementalHash Hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        private ILogger<Startup> _logger;
        
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging();
            
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<Startup>();
            
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo {Title = "ShaFlyRest.Scanny", Version = "v1"});
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShaFlyRest.Scanny v1"));
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapPost("/pipes", async context =>
                {
                    var contextReader = context.Request.BodyReader;
                    var contextWriter = context.Response.BodyWriter;
                    context.Response.ContentType = "text/plain";
                    context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
                    var ct = context.RequestAborted;
                    
                    var response = await HttpClient.PostAsync(CloudEndpoint, new StreamContent(contextReader.AsStream()), ct);
                    var proxyResult = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogInformation($"Proxy response: {proxyResult}");

                    var finalResponse = new FinalResponse(HashToString(Hasher.GetHashAndReset()), proxyResult);
                    await context.Response.StartAsync(ct);
                    await WriteContextResponse(contextWriter, finalResponse, ct);
                    await context.Response.CompleteAsync();
                });
                
                endpoints.MapPost("/stream", async (HttpContext context) =>
                {
                    var ct = context.RequestAborted;
                    var response = await HttpClient.PostAsync(CloudEndpoint, new StreamContent(context.Request.Body), ct);
                    var proxyResult = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogInformation($"Proxy response: {proxyResult}");
                    var finalResponse = new FinalResponse(HashToString(Hasher.GetHashAndReset()), proxyResult);
                    return Results.Ok(finalResponse);
                });
                
                
                endpoints.MapPost("/pipes-process", async context =>
                {
                    var contextReader = context.Request.BodyReader;
                    var contextWriter = context.Response.BodyWriter;
                    var ct = context.RequestAborted;
                    context.Response.ContentType = "text/plain";
                    context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
                    
                    // Create channel to Cloud endpoint
                    var proxyPipe = new Pipe();
                    var proxyPipeReader = proxyPipe.Reader;
                    var proxyPipeWriter = proxyPipe.Writer;

                    var proxyTask = HttpClient.PostAsync(CloudEndpoint, new StreamContent(proxyPipeReader.AsStream(false)), ct);
                    
                    while (!ct.IsCancellationRequested)
                    {
                        var contextReadResult = await contextReader.ReadAsync(ct);
                        var contextBuffer = contextReadResult.Buffer;

                        if (contextBuffer.IsSingleSegment)
                        {
                            await ProcessBlock(proxyPipeWriter, contextBuffer.First, ct);
                        }
                        else
                        {
                            foreach (var segment in contextBuffer)
                            {
                                await ProcessBlock(proxyPipeWriter, segment, ct);
                            }
                        }
                        contextReader.AdvanceTo(contextBuffer.End);

                        if (contextReadResult.IsCompleted)
                        {
                            _logger.LogInformation("Reader is completed!");
                            await proxyPipeWriter.FlushAsync(ct);
                            await proxyPipeWriter.CompleteAsync();
                            break;
                        }
                    }

                    var response = await proxyTask;
                    var proxyResult = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogInformation($"Proxy response: {proxyResult}");

                    var finalResponse = new FinalResponse(HashToString(Hasher.GetHashAndReset()), proxyResult);
                    await context.Response.StartAsync(ct);
                    await WriteContextResponse(contextWriter, finalResponse, ct);
                    await context.Response.CompleteAsync();
                });
                
                endpoints.MapPost("/stream-process", async (HttpContext context) =>
                {
                    var contextStream = context.Request.Body;
                    await using var proxyStream = new MemoryStream();
                    var ct = context.RequestAborted;
                    
                    var buffer = new byte[1024 * 8];
                    int read;
                    while ((read = await contextStream.ReadAsync(buffer, ct)) > 0)
                    {
                        Hasher.AppendData(buffer);
                        await proxyStream.WriteAsync(buffer, 0, read, ct);
                    }

                    await proxyStream.FlushAsync(ct);

                    var response = await HttpClient.PostAsync(CloudEndpoint, new StreamContent(proxyStream), ct);
                    var proxyResult = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogInformation($"Proxy response: {proxyResult}");
                    var finalResponse = new FinalResponse(HashToString(Hasher.GetHashAndReset()), proxyResult);
                    return Results.Ok(finalResponse);
                });
                
                endpoints.MapPost("/pipes-process-optimized", async context =>
                {
                    var contextReader = context.Request.BodyReader;
                    var contextWriter = context.Response.BodyWriter;
                    var ct = context.RequestAborted;
                    context.Response.ContentType = "text/plain";
                    context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
                    
                    // Create channel to Cloud endpoint
                    var proxyPipe = new Pipe();
                    var proxyPipeReader = proxyPipe.Reader;
                    var proxyPipeWriter = proxyPipe.Writer;

                    var proxyTask = HttpClient.PostAsync(CloudEndpoint, new StreamContent(proxyPipeReader.AsStream(false)), ct);

                    var bufferSize = 1024 * 8;
                    while (!ct.IsCancellationRequested)
                    {
                        var contextReadResult = await contextReader.ReadAsync(ct);
                        var contextBuffer = contextReadResult.Buffer;

                        if (contextBuffer.IsSingleSegment)
                        { 
                            ProcessBlockOptimized(proxyPipeWriter, contextBuffer.FirstSpan, ct);
                            await proxyPipeWriter.FlushAsync(ct);
                        }
                        else
                        {
                            foreach (var segment in contextBuffer)
                            {
                                ProcessBlockOptimized(proxyPipeWriter, segment.Span, ct);
                                await proxyPipeWriter.FlushAsync(ct);
                            }
                        }
                        
                        contextReader.AdvanceTo(contextBuffer.End);

                        if (contextReadResult.IsCompleted)
                        {
                            _logger.LogInformation("Reader is completed!");
                            await proxyPipeWriter.FlushAsync(ct);
                            await proxyPipeWriter.CompleteAsync();
                            break;
                        }
                    }

                    var response = await proxyTask;
                    var proxyResult = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogInformation($"Proxy response: {proxyResult}");

                    var finalResponse = new FinalResponse(HashToString(Hasher.GetHashAndReset()), proxyResult);
                    await context.Response.StartAsync(ct);
                    await WriteContextResponse(contextWriter, finalResponse, ct);
                    await context.Response.CompleteAsync();
                });
            });
        }

        private async Task ProcessBlock(PipeWriter pipeWriter, ReadOnlyMemory<byte> memory, CancellationToken ct)
        {
            _logger.LogInformation($"Processing {memory.Length} byes");
            Hasher.AppendData(memory.Span);
            await pipeWriter.WriteAsync(memory, ct);
        }

        private void ProcessBlockOptimized(PipeWriter pipeWriter, ReadOnlySpan<byte> span, CancellationToken ct)
        {
            _logger.LogInformation($"Processing {span.Length} byes");
            Hasher.AppendData(span);

            span.CopyTo(pipeWriter.GetSpan(span.Length));
            pipeWriter.Advance(span.Length);
        }

        private static async Task WriteContextResponse(PipeWriter contextWriter, FinalResponse finalResponse,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                throw new InvalidOperationException("Cannot write message after request is complete.");
            }

            var bytesWritten = BuildResponseMessage(contextWriter.GetMemory().Span, finalResponse);

            contextWriter.Advance(bytesWritten);

            await contextWriter.FlushAsync(ct);
        }

        private static int BuildResponseMessage(Span<byte> response, FinalResponse finalResponse)
        {
            var position = 0;
            var bytesWritten = Encoding.UTF8.GetBytes($"LocalSha1: {finalResponse.LocalSha}{Environment.NewLine}" +
                                                      $"RemoteSha1: {finalResponse.RemoteSha}", response[position..]);
            position += bytesWritten;

            return position;
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