using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<ShaFlyRestBenchmark>();

[MemoryDiagnoser]
public class ShaFlyRestBenchmark
{
    private const string ScannyUri = "http://localhost:5000";
    private const string FilePath = "/Users/mrfishchev/git/ShaFlyRest/testfile";
    
    /// <summary>
    /// Sends the file though a pipeline without a processing
    /// </summary>
    [Benchmark]
    public async Task SendUsingPipes()
    {
        await using var file = File.OpenRead(FilePath);
        using var client = new HttpClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ScannyUri}/pipes");
        request.Content = new StreamContent(file);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
    
    /// <summary>
    /// Sends the file though a stream without a processing
    /// </summary>
    [Benchmark]
    public async Task SendUsingStream()
    {
        await using var file = File.OpenRead(FilePath);
        using var client = new HttpClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ScannyUri}/stream");
        request.Content = new StreamContent(file);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
    
    /// <summary>
    /// Sends the file though a pipeline and calculate a hash
    /// </summary>
    [Benchmark]
    public async Task SendAndProcessUsingPipes()
    {
        await using var file = File.OpenRead(FilePath);
        using var client = new HttpClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ScannyUri}/pipes-process");
        request.Content = new StreamContent(file);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Sends the file though a stream and calculate a hash
    /// </summary>
    [Benchmark]
    public async Task SendAndProcessUsingStream()
    {
        await using var file = File.OpenRead(FilePath);
        using var client = new HttpClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ScannyUri}/stream-process");
        request.Content = new StreamContent(file);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
    
    [Benchmark]
    public async Task SendAndProcessUsingOptimizedPipes()
    {
        await using var file = File.OpenRead(FilePath);
        using var client = new HttpClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ScannyUri}/pipes-process-optimized");
        request.Content = new StreamContent(file);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}