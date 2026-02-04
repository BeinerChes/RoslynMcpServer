using System.Text.Json;
using SharpOps.Inference;
using TorchSharp;
using static TorchSharp.torch;

namespace SharpOps.Torch.Training;

/// <summary>
/// Dataset for fine-tuning. Loads JSONL, tokenizes with SharpOpsTokenizer,
/// creates input_ids/labels/attention_mask tensors.
/// </summary>
public sealed class FinetuneDataset
{
    private readonly SharpOpsTokenizer _tokenizer;
    private readonly int _maxLength;
    private readonly List<(int[] tokenIds, int outputStart)> _tokenized = [];

    public int Count => _tokenized.Count;

    public FinetuneDataset(List<Dictionary<string, JsonElement>> examples, SharpOpsTokenizer tokenizer, int maxLength = 512)
    {
        _tokenizer = tokenizer;
        _maxLength = maxLength;

        Console.Error.Write("Tokenizing... ");
        foreach (var ex in examples)
        {
            var (tokens, outputStart) = TokenizeExample(ex);
            _tokenized.Add((tokens, outputStart));
        }
        Console.Error.WriteLine($"{_tokenized.Count} examples");
    }

    private (int[] tokenIds, int outputStart) TokenizeExample(Dictionary<string, JsonElement> example)
    {
        var inputText = example["input"].GetString() ?? "";
        var outputText = example["output"].GetString() ?? "";

        var inputIds = _tokenizer.Encode(inputText);
        var outputIds = _tokenizer.Encode(outputText);

        // full_ids = input_ids + output_ids + [eos_id]
        var fullIds = new int[inputIds.Length + outputIds.Length + 1];
        Array.Copy(inputIds, fullIds, inputIds.Length);
        Array.Copy(outputIds, 0, fullIds, inputIds.Length, outputIds.Length);
        fullIds[^1] = _tokenizer.EosId;

        return (fullIds, inputIds.Length);
    }

    /// <summary>
    /// Get a single example as tensors.
    /// </summary>
    public (Tensor inputIds, Tensor labels, Tensor attentionMask) GetItem(int idx)
    {
        var (tokenIds, outputStart) = _tokenized[idx];

        // Truncate if needed
        int len = Math.Min(tokenIds.Length, _maxLength);
        int actualOutputStart = Math.Min(outputStart, len);

        // Create labels: -100 for input tokens (masked), actual ids for output
        var labels = new int[_maxLength];
        Array.Fill(labels, -100);
        for (int i = actualOutputStart; i < len; i++)
        {
            labels[i] = tokenIds[i];
        }

        // Pad input_ids to max_length
        var paddedInputIds = new int[_maxLength];
        Array.Fill(paddedInputIds, _tokenizer.PadId);
        Array.Copy(tokenIds, paddedInputIds, len);

        // Attention mask: 1 for valid tokens, 0 for padding
        var attentionMask = new int[_maxLength];
        for (int i = 0; i < len; i++)
        {
            attentionMask[i] = 1;
        }

        return (
            torch.tensor(paddedInputIds, dtype: ScalarType.Int64),
            torch.tensor(labels, dtype: ScalarType.Int64),
            torch.tensor(attentionMask, dtype: ScalarType.Int64)
        );
    }

    /// <summary>
    /// Create a batch of tensors from indices.
    /// </summary>
    public (Tensor inputIds, Tensor labels, Tensor attentionMask) GetBatch(int[] indices)
    {
        var inputIdsList = new List<Tensor>();
        var labelsList = new List<Tensor>();
        var masksList = new List<Tensor>();

        foreach (var idx in indices)
        {
            var (ids, lbl, msk) = GetItem(idx);
            inputIdsList.Add(ids.unsqueeze(0));
            labelsList.Add(lbl.unsqueeze(0));
            masksList.Add(msk.unsqueeze(0));
        }

        return (
            torch.cat(inputIdsList, dim: 0),
            torch.cat(labelsList, dim: 0),
            torch.cat(masksList, dim: 0)
        );
    }

    /// <summary>
    /// Generate batched indices with optional shuffling.
    /// </summary>
    public List<int[]> GetBatchIndices(int batchSize, bool shuffle = false)
    {
        var indices = Enumerable.Range(0, _tokenized.Count).ToArray();
        if (shuffle)
        {
            var rng = new Random();
            for (int i = indices.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
        }

        var batches = new List<int[]>();
        for (int i = 0; i < indices.Length; i += batchSize)
        {
            var end = Math.Min(i + batchSize, indices.Length);
            batches.Add(indices[i..end]);
        }
        return batches;
    }
}

/// <summary>
/// Helper to load JSONL data files.
/// </summary>
public static class DataLoader
{
    public static List<Dictionary<string, JsonElement>> LoadJsonl(string path)
    {
        var examples = new List<Dictionary<string, JsonElement>>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var record = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
            if (record != null)
                examples.Add(record);
        }
        return examples;
    }

    public static List<Dictionary<string, JsonElement>> LoadData(string dataPath)
    {
        var allExamples = new List<Dictionary<string, JsonElement>>();

        if (Directory.Exists(dataPath))
        {
            var jsonlFiles = Directory.GetFiles(dataPath, "*.jsonl", SearchOption.AllDirectories);
            Console.Error.WriteLine($"  Found {jsonlFiles.Length} JSONL files in {dataPath}");
            foreach (var file in jsonlFiles)
            {
                allExamples.AddRange(LoadJsonl(file));
            }
        }
        else if (File.Exists(dataPath))
        {
            allExamples.AddRange(LoadJsonl(dataPath));
        }
        else
        {
            throw new FileNotFoundException($"Data path not found: {dataPath}");
        }

        return allExamples;
    }
}
