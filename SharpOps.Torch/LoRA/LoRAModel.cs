using TorchSharp;
using TorchSharp.Modules;
using SharpOps.Model;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace SharpOps.Torch.LoRA;

/// <summary>
/// Wrapper that adds LoRA to a SharpTinyCoder model.
/// Freezes base model weights and only trains LoRA parameters.
/// </summary>
public sealed class LoRAModel : Module
{
    public readonly SharpTinyCoder BaseModel;
    public readonly int Rank;
    public readonly float Alpha;
    public readonly float DropoutRate;
    public readonly string[] TargetModules;

    private readonly List<(string name, LoRALinear lora)> _loraLayers = [];

    public LoRAModel(
        SharpTinyCoder baseModel,
        int rank = 8,
        float alpha = 32.0f,
        float dropout = 0.0f,
        string[]? targetModules = null) : base("LoRAModel")
    {
        BaseModel = baseModel;
        Rank = rank;
        Alpha = alpha;
        DropoutRate = dropout;
        TargetModules = targetModules ?? ["q_proj", "v_proj"];

        // Freeze base model
        foreach (var param in BaseModel.parameters())
        {
            param.requires_grad = false;
        }

        // Attach LoRA to target modules
        AttachLoRA();
        RegisterComponents();
    }

    private static readonly HashSet<string> AttentionModules = ["q_proj", "k_proj", "v_proj", "o_proj"];
    private static readonly HashSet<string> FfnModules = ["gate_proj", "up_proj", "down_proj"];

    private void AttachLoRA()
    {
        for (int layerIdx = 0; layerIdx < BaseModel.layers.Count; layerIdx++)
        {
            var layer = BaseModel.layers[layerIdx];

            // Attach to attention modules
            foreach (var moduleName in TargetModules)
            {
                if (AttentionModules.Contains(moduleName))
                {
                    var baseLinear = moduleName switch
                    {
                        "q_proj" => layer.attention.q_proj,
                        "k_proj" => layer.attention.k_proj,
                        "v_proj" => layer.attention.v_proj,
                        "o_proj" => layer.attention.o_proj,
                        _ => null
                    };

                    if (baseLinear is Linear linear)
                    {
                        var loraLayer = new LoRALinear(linear, Rank, Alpha, DropoutRate);
                        switch (moduleName)
                        {
                            case "q_proj": layer.attention.q_proj = loraLayer; break;
                            case "k_proj": layer.attention.k_proj = loraLayer; break;
                            case "v_proj": layer.attention.v_proj = loraLayer; break;
                            case "o_proj": layer.attention.o_proj = loraLayer; break;
                        }
                        var name = $"layers.{layerIdx}.attention.{moduleName}";
                        _loraLayers.Add((name, loraLayer));
                        // Explicitly register so .to(device) moves LoRA parameters
                        register_module($"lora_{layerIdx}_{moduleName}", loraLayer);
                    }
                }

                if (FfnModules.Contains(moduleName))
                {
                    var baseLinear = moduleName switch
                    {
                        "gate_proj" => layer.ffn.gate_proj,
                        "up_proj" => layer.ffn.up_proj,
                        "down_proj" => layer.ffn.down_proj,
                        _ => null
                    };

                    if (baseLinear is Linear linear)
                    {
                        var loraLayer = new LoRALinear(linear, Rank, Alpha, DropoutRate);
                        switch (moduleName)
                        {
                            case "gate_proj": layer.ffn.gate_proj = loraLayer; break;
                            case "up_proj": layer.ffn.up_proj = loraLayer; break;
                            case "down_proj": layer.ffn.down_proj = loraLayer; break;
                        }
                        var name = $"layers.{layerIdx}.ffn.{moduleName}";
                        _loraLayers.Add((name, loraLayer));
                        // Explicitly register so .to(device) moves LoRA parameters
                        register_module($"lora_{layerIdx}_ffn_{moduleName}", loraLayer);
                    }
                }
            }
        }

        Console.Error.WriteLine($"Attached LoRA to {_loraLayers.Count} layers:");
        long totalLoraParams = 0;
        foreach (var (name, lora) in _loraLayers)
        {
            var inF = lora.BaseLayer.weight!.shape[1];
            var outF = lora.BaseLayer.weight.shape[0];
            var paramCount = Rank * (inF + outF);
            totalLoraParams += paramCount;
            Console.Error.WriteLine($"  {name}: ({inF}, {outF}) -> rank {Rank} ({paramCount:N0} params)");
        }
        Console.Error.WriteLine($"Total LoRA parameters: {totalLoraParams:N0}");
    }

    public (Tensor logits, Tensor? loss) forward(Tensor inputIds, Tensor? attentionMask, Tensor? labels)
    {
        return BaseModel.forward(inputIds, attentionMask, labels);
    }

    /// <summary>
    /// Return all LoRA parameters for optimizer.
    /// </summary>
    public IReadOnlyList<Parameter> LoRAParameters()
    {
        var result = new List<Parameter>();
        foreach (var (_, loraLayer) in _loraLayers)
        {
            result.AddRange(loraLayer.LoRAParameters);
        }
        return result;
    }

    /// <summary>
    /// Count total LoRA parameters.
    /// </summary>
    public long NumLoRAParameters()
    {
        return LoRAParameters().Sum(p => p.numel());
    }

    /// <summary>
    /// Merge LoRA weights into base model and return clean base model.
    /// After merging, LoRA layers are replaced with original linear layers.
    /// </summary>
    public SharpTinyCoder MergeLoRA()
    {
        foreach (var (name, loraLayer) in _loraLayers)
        {
            loraLayer.MergeWeights();

            // Parse path: layers.{idx}.{attention|ffn}.{module_name}
            var parts = name.Split('.');
            var layerIdx = int.Parse(parts[1]);
            var parentName = parts[2]; // attention or ffn
            var moduleName = parts[3]; // q_proj, v_proj, gate_proj, etc.

            // Replace LoRA layer with merged base layer
            var layer = BaseModel.layers[layerIdx];
            if (parentName == "attention")
            {
                switch (moduleName)
                {
                    case "q_proj": layer.attention.q_proj = loraLayer.BaseLayer; break;
                    case "k_proj": layer.attention.k_proj = loraLayer.BaseLayer; break;
                    case "v_proj": layer.attention.v_proj = loraLayer.BaseLayer; break;
                    case "o_proj": layer.attention.o_proj = loraLayer.BaseLayer; break;
                }
            }
            else // ffn
            {
                switch (moduleName)
                {
                    case "gate_proj": layer.ffn.gate_proj = loraLayer.BaseLayer; break;
                    case "up_proj": layer.ffn.up_proj = loraLayer.BaseLayer; break;
                    case "down_proj": layer.ffn.down_proj = loraLayer.BaseLayer; break;
                }
            }
        }

        Console.Error.WriteLine($"Merged {_loraLayers.Count} LoRA layers into base model");
        _loraLayers.Clear();

        // Unfreeze base model weights
        foreach (var param in BaseModel.parameters())
        {
            param.requires_grad = true;
        }

        return BaseModel;
    }

    /// <summary>
    /// Resolve target modules string to list of module names.
    /// </summary>
    public static string[] ResolveTargetModules(string target)
    {
        return target.ToLowerInvariant() switch
        {
            "qv" => ["q_proj", "v_proj"],
            "attention" => ["q_proj", "k_proj", "v_proj", "o_proj"],
            "ffn" => ["gate_proj", "up_proj", "down_proj"],
            "all" => ["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"],
            _ => ["q_proj", "v_proj"],
        };
    }
}
