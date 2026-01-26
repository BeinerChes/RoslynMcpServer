using System.Net;
using System.Text;
using System.Text.Json;

namespace RoslynMcpServer;

/// <summary>
/// Simple HTTP server for hook token validation.
/// Runs alongside the MCP server to provide token validation for Claude hooks.
/// </summary>
public class HookValidationServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts;
    private readonly int _port;
    private Task? _serverTask;

    public int Port => _port;

    public HookValidationServer(int port = 0)
    {
        // If port is 0, find an available port
        _port = port == 0 ? FindAvailablePort() : port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _cts = new CancellationTokenSource();
    }

    private static int FindAvailablePort()
    {
        // Try ports in the 5300-5399 range
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Start()
    {
        _listener.Start();
        Console.Error.WriteLine($"Hook validation server listening on http://localhost:{_port}/");

        // Write port to a known file so hooks can discover it
        WritePortFile();

        _serverTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Hook server error: {ex.Message}");
                }
            }
        });
    }

    private void WritePortFile()
    {
        try
        {
            var claudeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude");
            Directory.CreateDirectory(claudeDir);

            var portFile = Path.Combine(claudeDir, "roslyn-hook-port");
            File.WriteAllText(portFile, _port.ToString());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not write port file: {ex.Message}");
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // CORS headers for local development
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                return;
            }

            var path = request.Url?.AbsolutePath ?? "";

            object result = path switch
            {
                "/validate" => await HandleValidateAsync(request),
                "/health" => new { status = "ok", port = _port },
                _ => new { error = "Not found" }
            };

            var json = JsonSerializer.Serialize(result);
            var buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = "application/json";
            response.StatusCode = path == "/" || result is { } r && r.GetType().GetProperty("error") != null ? 404 : 200;
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer);
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            var error = JsonSerializer.Serialize(new { error = ex.Message });
            var buffer = Encoding.UTF8.GetBytes(error);
            await response.OutputStream.WriteAsync(buffer);
        }
        finally
        {
            response.Close();
        }
    }

    private static async Task<object> HandleValidateAsync(HttpListenerRequest request)
    {
        string? token = null;
        string? topic = null;

        if (request.HttpMethod == "GET")
        {
            token = request.QueryString["token"];
            topic = request.QueryString["topic"] ?? "git";
        }
        else if (request.HttpMethod == "POST")
        {
            using var reader = new StreamReader(request.InputStream);
            var body = await reader.ReadToEndAsync();

            try
            {
                var json = JsonDocument.Parse(body);
                token = json.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
                topic = json.RootElement.TryGetProperty("topic", out var tp) ? tp.GetString() : "git";
            }
            catch
            {
                return new { valid = false, error = "Invalid JSON body" };
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            return new { valid = false, error = "Token is required" };
        }

        var result = HookTokenService.Instance.ValidateToken(token, topic ?? "git");

        return new
        {
            valid = result.IsValid,
            message = result.Message,
            topic = result.Topic,
            ageSeconds = result.Age?.TotalSeconds
        };
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _serverTask?.Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();

        // Clean up port file
        try
        {
            var portFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "roslyn-hook-port");
            if (File.Exists(portFile))
                File.Delete(portFile);
        }
        catch { }
    }
}
