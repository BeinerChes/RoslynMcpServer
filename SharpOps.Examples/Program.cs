using SharpOps.Model;
using TorchSharp;
using TorchSharp.PyBridge;

var checkpointPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "Models", "checkpoint.pt");

Console.WriteLine($"Loading SharpTinyCoder from {checkpointPath}...");

var config = ModelConfig.ConfigTiny;
var model = new SharpTinyCoder(config);
model.load_py(checkpointPath, strict: false);
model.eval();

Console.WriteLine($"\n{"".PadRight(60, '=')}");
Console.WriteLine("SharpTinyCoder Model Info");
Console.WriteLine($"{"".PadRight(60, '=')}");
Console.WriteLine($"  Vocab size:     {config.VocabSize}");
Console.WriteLine($"  Context length: {config.ContextLength}");
Console.WriteLine($"  Embedding dim:  {config.EmbeddingDim}");
Console.WriteLine($"  Layers:         {config.NumLayers}");
Console.WriteLine($"  Heads:          {config.NumHeads}");
Console.WriteLine($"  KV heads:       {config.NumKvHeads}");
Console.WriteLine($"  FFN hidden:     {config.FfHiddenDim}");
Console.WriteLine($"  Tied embeddings:{config.TieEmbeddings}");
Console.WriteLine($"  Total params:   {model.NumParameters(trainableOnly: false):N0}");

Console.WriteLine($"\nPARAMETERS:");
foreach (var (name, param) in model.named_parameters())
{
    Console.WriteLine($"  {name}: shape=[{string.Join(", ", param.shape)}], dtype={param.dtype}");
}
