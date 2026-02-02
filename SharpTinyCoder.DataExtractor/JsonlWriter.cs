using System.Text.Json;

namespace SharpTinyCoder.DataExtractor;

/// <summary>
/// Streaming writer for JSONL (JSON Lines) format.
/// Each line is a complete JSON object.
/// </summary>
public sealed class JsonlWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _options;

    public JsonlWriter(string outputPath)
    {
        _writer = new StreamWriter(outputPath, append: false, encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    /// <summary>
    /// Writes a training sample as a single JSON line.
    /// </summary>
    public async Task WriteAsync(TrainingSample sample)
    {
        // Normalize line endings to LF only
        var normalized = new TrainingSample
        {
            Input = sample.Input.Replace("\r\n", "\n"),
            Output = sample.Output.Replace("\r\n", "\n"),
            SourceFile = sample.SourceFile,
            Line = sample.Line,
            MethodName = sample.MethodName
        };
        var json = JsonSerializer.Serialize(normalized, _options);
        await _writer.WriteLineAsync(json);
    }

    /// <summary>
    /// Flushes the underlying stream.
    /// </summary>
    public async Task FlushAsync()
    {
        await _writer.FlushAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }
}
