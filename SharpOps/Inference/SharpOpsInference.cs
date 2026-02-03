using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SharpOps.Inference;

public class SharpOpsInference : IDisposable
{


    private readonly InferenceSession _session;


    private readonly SharpOpsTokenizer _tokenizer;


    private readonly Random _random = new();


    public SharpOpsInference(string modelPath, string tokenizerPath)
    {
        _session = new InferenceSession(modelPath);
        _tokenizer = new SharpOpsTokenizer(tokenizerPath);
    }


    public string Generate(string input, float temperature = 0.7f, float topP = 0.9f, int maxTokens = 512)
    {
        // Encode input with BOS token
        var inputIds = _tokenizer.EncodeWithBos(input);
        var currentIds = new List<int>(inputIds);

        // Generation loop
        for (int i = 0; i < maxTokens; i++)
        {
            // Create input tensor [1, seq_len]
            var inputTensor = new DenseTensor<long>(new[] { 1, currentIds.Count });
            for (int j = 0; j < currentIds.Count; j++)
            {
                inputTensor[0, j] = currentIds[j];
            }

            // Run inference
            var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor)
        };

            using var results = _session.Run(inputs);
            var logitsOutput = results.First().AsTensor<float>();

            // Get logits for last position [vocab_size]
            var seqLen = currentIds.Count;
            var vocabSize = logitsOutput.Dimensions[2];
            var logits = new float[vocabSize];
            for (int v = 0; v < vocabSize; v++)
            {
                logits[v] = logitsOutput[0, seqLen - 1, v];
            }

            // Sample next token
            var nextToken = SampleWithTemperatureAndTopP(logits, temperature, topP);
            currentIds.Add(nextToken);

            // Stop if EOS
            if (nextToken == _tokenizer.EosId)
                break;
        }

        // Decode output (everything after <|output|>)
        return _tokenizer.DecodeOutput(currentIds.ToArray());
    }


    private int SampleWithTemperatureAndTopP(float[] logits, float temperature, float topP)
    {
        // Apply temperature
        if (temperature > 0)
        {
            for (int i = 0; i < logits.Length; i++)
            {
                logits[i] /= temperature;
            }
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

        // Fallback: return highest probability token
        return sortedIndices[0];
    }


    public void Dispose()
    {
        _session.Dispose();
    }


    public string GenerateDebug(string input, float temperature = 0.7f, float topP = 0.9f, int maxTokens = 512)
    {
        // Encode input with BOS token
        var inputIds = _tokenizer.EncodeWithBos(input);
        var currentIds = new List<int>(inputIds);

        Console.Error.WriteLine($"Input token IDs ({inputIds.Length}): {string.Join(", ", inputIds)}");

        // Generation loop
        for (int i = 0; i < maxTokens; i++)
        {
            // Create input tensor [1, seq_len]
            var inputTensor = new DenseTensor<long>(new[] { 1, currentIds.Count });
            for (int j = 0; j < currentIds.Count; j++)
            {
                inputTensor[0, j] = currentIds[j];
            }

            // Run inference
            var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor)
        };

            using var results = _session.Run(inputs);
            var logitsOutput = results.First().AsTensor<float>();

            // Get logits for last position [vocab_size]
            var seqLen = currentIds.Count;
            var vocabSize = logitsOutput.Dimensions[2];
            var logits = new float[vocabSize];
            for (int v = 0; v < vocabSize; v++)
            {
                logits[v] = logitsOutput[0, seqLen - 1, v];
            }

            // Show top 5 logits
            var topIndices = Enumerable.Range(0, vocabSize)
                .OrderByDescending(idx => logits[idx])
                .Take(5)
                .ToArray();
            Console.Error.WriteLine($"Step {i}: Top logits: {string.Join(", ", topIndices.Select(idx => $"{idx}({logits[idx]:F2})"))}");

            // Sample next token
            var nextToken = SampleWithTemperatureAndTopP(logits, temperature, topP);
            currentIds.Add(nextToken);
            Console.Error.WriteLine($"  Sampled: {nextToken} -> '{_tokenizer.GetToken(nextToken)}'");

            // Stop if EOS
            if (nextToken == _tokenizer.EosId)
                break;
        }

        Console.Error.WriteLine($"Output token IDs: {string.Join(", ", currentIds.Skip(inputIds.Length))}");

        // Decode output (everything after <|output|>)
        return _tokenizer.DecodeOutput(currentIds.ToArray());
    }


    public string GenerateNoBos(string input, float temperature = 0.7f, float topP = 0.9f, int maxTokens = 512)
    {
        // Encode input WITHOUT BOS token
        var inputIds = _tokenizer.Encode(input);
        var currentIds = new List<int>(inputIds);

        // Generation loop
        for (int i = 0; i < maxTokens; i++)
        {
            var inputTensor = new DenseTensor<long>(new[] { 1, currentIds.Count });
            for (int j = 0; j < currentIds.Count; j++)
            {
                inputTensor[0, j] = currentIds[j];
            }

            var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor)
        };

            using var results = _session.Run(inputs);
            var logitsOutput = results.First().AsTensor<float>();

            var seqLen = currentIds.Count;
            var vocabSize = logitsOutput.Dimensions[2];
            var logits = new float[vocabSize];
            for (int v = 0; v < vocabSize; v++)
            {
                logits[v] = logitsOutput[0, seqLen - 1, v];
            }

            var nextToken = SampleWithTemperatureAndTopP(logits, temperature, topP);
            currentIds.Add(nextToken);

            if (nextToken == _tokenizer.EosId)
                break;
        }

        return _tokenizer.DecodeOutput(currentIds.ToArray());
    }
}