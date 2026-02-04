namespace SharpOps.Inference;

public class SharpOpsService : IDisposable
{


    private readonly SharpOpsInference _inference;


    public SharpOpsService()
        : this(
            Path.Combine(AppContext.BaseDirectory, "Models", "sharptinycoder.onnx"),
            Path.Combine(AppContext.BaseDirectory, "Models", "tokenizer", "tokenizer.json"))
    {
    }


    public SharpOpsService(string modelPath, string tokenizerPath)
    {
        _inference = new SharpOpsInference(modelPath, tokenizerPath);
    }


    public static string BuildPrompt(string methodSignature, Dictionary<string, string>? fields = null, string? description = null)
    {
        var sb = new System.Text.StringBuilder();

        // Optional description
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.Append(description);
            sb.Append('\n');
            sb.Append('\n');
        }

        // Method signature (use \n line endings to match training data)
        sb.Append(methodSignature);
        sb.Append('\n');

        // Optional fields section
        if (fields != null && fields.Count > 0)
        {
            sb.Append('\n');
            sb.Append("FIELDS:\n");
            foreach (var (name, type) in fields)
            {
                sb.Append($"{name}: {type}\n");
            }
        }

        sb.Append('\n');
        sb.Append("<|output|>");

        return sb.ToString();
    }


    public string GenerateSharpOps(
    string methodSignature,
    Dictionary<string, string>? fields = null,
    string? description = null,
    float temperature = 0f,
    float topP = 0.9f,
    int maxTokens = 512)
    {
        var prompt = BuildPrompt(methodSignature, fields, description);
        // Use GenerateNoBos - model was trained without BOS token
        return _inference.GenerateNoBos(prompt, temperature, topP, maxTokens);
    }


    public void Dispose()
    {
        _inference.Dispose();
    }


    public SharpOpsInference.GenerationStats GenerateWithStats(
        string methodSignature,
        Dictionary<string, string>? fields = null,
        string? description = null,
        float temperature = 0f,
        float topP = 0.9f,
        int maxTokens = 512)
    {
        var prompt = BuildPrompt(methodSignature, fields, description);
        return _inference.GenerateWithStats(prompt, temperature, topP, maxTokens);
    }
}