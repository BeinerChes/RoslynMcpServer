using SharpOps.Model;
using TorchSharp;
using TorchSharp.PyBridge;

namespace SharpOps.Inference;

public class SharpOpsInference : IDisposable
{
    public record GenerationStats(
        string Output,
        int InputTokens,
        int OutputTokens,
        double ElapsedMs,
        double TokensPerSecond,
        double MsPerToken);

    private readonly SharpTinyCoder _model;
    private readonly SharpOpsTokenizer _tokenizer;
    private readonly Random _random = new();

    public SharpOpsInference(string modelPath, string tokenizerPath)
    {
        _tokenizer = new SharpOpsTokenizer(tokenizerPath);

        var config = new ModelConfig
        {
            VocabSize = _tokenizer.VocabSize,
            ContextLength = ModelConfig.ConfigTiny.ContextLength,
            EmbeddingDim = ModelConfig.ConfigTiny.EmbeddingDim,
            NumLayers = ModelConfig.ConfigTiny.NumLayers,
            NumHeads = ModelConfig.ConfigTiny.NumHeads,
            NumKvHeads = ModelConfig.ConfigTiny.NumKvHeads,
            FfHiddenDim = ModelConfig.ConfigTiny.FfHiddenDim,
            Dropout = 0.0f,
            TieEmbeddings = ModelConfig.ConfigTiny.TieEmbeddings,
        };

        _model = new SharpTinyCoder(config);
        _model.load_py(modelPath, strict: false);
        _model.eval();
    }

    public string Generate(string input, float temperature = 0f, float topP = 0.9f, int maxTokens = 512)
    {
        var inputIds = _tokenizer.EncodeWithBos(input);
        return GenerateFromIds(inputIds, temperature, topP, maxTokens);
    }

    public string GenerateNoBos(string input, float temperature = 0f, float topP = 0.9f, int maxTokens = 512)
    {
        var inputIds = _tokenizer.Encode(input);
        return GenerateFromIds(inputIds, temperature, topP, maxTokens);
    }

    public GenerationStats GenerateWithStats(string input, float temperature = 0f, float topP = 0.9f, int maxTokens = 512)
    {
        var inputIds = _tokenizer.Encode(input);
        var inputTokenCount = inputIds.Length;
        var currentIds = new List<int>(inputIds);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int generatedTokens = 0;

        using (torch.no_grad())
        {
            for (int i = 0; i < maxTokens; i++)
            {
                var logits = RunForward(currentIds);
                var nextToken = SampleWithTemperatureAndTopP(logits, temperature, topP);
                currentIds.Add(nextToken);
                generatedTokens++;

                if (nextToken == _tokenizer.EosId)
                    break;
            }
        }

        sw.Stop();
        var elapsedMs = sw.Elapsed.TotalMilliseconds;
        var tokensPerSec = generatedTokens / (elapsedMs / 1000.0);
        var msPerToken = elapsedMs / generatedTokens;

        var output = _tokenizer.DecodeOutput(currentIds.ToArray());

        return new GenerationStats(
            Output: output,
            InputTokens: inputTokenCount,
            OutputTokens: generatedTokens,
            ElapsedMs: elapsedMs,
            TokensPerSecond: tokensPerSec,
            MsPerToken: msPerToken);
    }

    public string GenerateDebug(string input, float temperature = 0f, float topP = 0.9f, int maxTokens = 512)
    {
        var inputIds = _tokenizer.EncodeWithBos(input);
        var currentIds = new List<int>(inputIds);

        Console.Error.WriteLine($"Input token IDs ({inputIds.Length}): {string.Join(", ", inputIds)}");

        using (torch.no_grad())
        {
            for (int i = 0; i < maxTokens; i++)
            {
                var logits = RunForward(currentIds);

                // Show top 5 logits
                var topIndices = Enumerable.Range(0, logits.Length)
                    .OrderByDescending(idx => logits[idx])
                    .Take(5)
                    .ToArray();
                Console.Error.WriteLine($"Step {i}: Top logits: {string.Join(", ", topIndices.Select(idx => $"{idx}({logits[idx]:F2})"))}");

                var nextToken = SampleWithTemperatureAndTopP(logits, temperature, topP);
                currentIds.Add(nextToken);
                Console.Error.WriteLine($"  Sampled: {nextToken} -> '{_tokenizer.GetToken(nextToken)}'");

                if (nextToken == _tokenizer.EosId)
                    break;
            }
        }

        Console.Error.WriteLine($"Output token IDs: {string.Join(", ", currentIds.Skip(inputIds.Length))}");
        return _tokenizer.DecodeOutput(currentIds.ToArray());
    }

    private string GenerateFromIds(int[] inputIds, float temperature, float topP, int maxTokens)
    {
        var currentIds = new List<int>(inputIds);

        using (torch.no_grad())
        {
            for (int i = 0; i < maxTokens; i++)
            {
                var logits = RunForward(currentIds);
                var nextToken = SampleWithTemperatureAndTopP(logits, temperature, topP);
                currentIds.Add(nextToken);

                if (nextToken == _tokenizer.EosId)
                    break;
            }
        }

        return _tokenizer.DecodeOutput(currentIds.ToArray());
    }

    private float[] RunForward(List<int> currentIds)
    {
        var seqLen = currentIds.Count;
        var inputTensor = torch.tensor(currentIds.Select(id => (long)id).ToArray(), dtype: torch.ScalarType.Int64)
            .reshape(1, seqLen);

        var (logits, _) = _model.forward(inputTensor, null, null);

        // Get logits for last position [vocab_size]
        var lastLogits = logits[0, seqLen - 1];
        var result = new float[lastLogits.shape[0]];
        var data = lastLogits.data<float>();
        for (int v = 0; v < result.Length; v++)
        {
            result[v] = data[v];
        }

        inputTensor.Dispose();
        logits.Dispose();
        lastLogits.Dispose();

        return result;
    }

    private int SampleWithTemperatureAndTopP(float[] logits, float temperature, float topP)
    {
        // Greedy decoding when temperature is 0 or negative
        if (temperature <= 0)
        {
            int bestIdx = 0;
            float bestVal = logits[0];
            for (int i = 1; i < logits.Length; i++)
            {
                if (logits[i] > bestVal)
                {
                    bestVal = logits[i];
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        // Apply temperature
        for (int i = 0; i < logits.Length; i++)
        {
            logits[i] /= temperature;
        }

        // Convert to probabilities with softmax
        var maxLogit = logits.Max();
        var expLogits = logits.Select(l => MathF.Exp(l - maxLogit)).ToArray();
        var sumExp = expLogits.Sum();
        var probs = expLogits.Select(e => e / sumExp).ToArray();

        // Apply top-p (nucleus) sampling
        var sortedIndices = Enumerable.Range(0, probs.Length)
            .OrderByDescending(i => probs[i])
            .ToArray();

        var cumSum = 0f;
        var cutoffIndex = sortedIndices.Length;
        for (int i = 0; i < sortedIndices.Length; i++)
        {
            cumSum += probs[sortedIndices[i]];
            if (cumSum >= topP)
            {
                cutoffIndex = i + 1;
                break;
            }
        }

        // Zero out probabilities below cutoff
        var validIndices = new HashSet<int>(sortedIndices.Take(cutoffIndex));
        for (int i = 0; i < probs.Length; i++)
        {
            if (!validIndices.Contains(i))
                probs[i] = 0;
        }

        // Renormalize
        var newSum = probs.Sum();
        for (int i = 0; i < probs.Length; i++)
        {
            probs[i] /= newSum;
        }

        // Sample from distribution
        var r = (float)_random.NextDouble();
        var cumProb = 0f;
        for (int i = 0; i < probs.Length; i++)
        {
            cumProb += probs[i];
            if (r < cumProb)
                return i;
        }

        return sortedIndices[0];
    }

    public void Dispose()
    {
        _model.Dispose();
    }
}
