using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace SharpOps.Torch.LoRA;

/// <summary>
/// LoRA layer that wraps a linear layer with low-rank adaptation.
/// Output = base_output + (x @ A @ B) * scale
/// where scale = alpha / rank.
/// A is initialized with kaiming_uniform, B with zeros,
/// so LoRA contribution starts at zero.
/// </summary>
public sealed class LoRALinear : Module<Tensor, Tensor>
{
    public readonly Linear BaseLayer;
    public readonly int Rank;
    public readonly float Alpha;
    public readonly float Scale;
    public readonly Parameter lora_A;
    public readonly Parameter lora_B;
    private readonly Module<Tensor, Tensor> _dropout;

    public LoRALinear(Linear baseLayer, int rank = 8, float alpha = 32.0f, float dropout = 0.0f)
        : base("LoRALinear")
    {
        BaseLayer = baseLayer;
        Rank = rank;
        Alpha = alpha;
        Scale = alpha / rank;

        var inFeatures = baseLayer.weight!.shape[1];
        var outFeatures = baseLayer.weight.shape[0];

        // A: [in_features, rank] initialized with kaiming_uniform
        lora_A = Parameter(torch.zeros(inFeatures, rank));
        init.kaiming_uniform_(lora_A, a: MathF.Sqrt(5));

        // B: [rank, out_features] initialized with zeros
        lora_B = Parameter(torch.zeros(rank, outFeatures));

        // Optional dropout
        _dropout = dropout > 0 ? Dropout(dropout) : Identity() as Module<Tensor, Tensor>;

        RegisterComponents();
    }

    public override Tensor forward(Tensor x)
    {
        // Base forward pass (frozen)
        var baseOutput = BaseLayer.forward(x);

        // LoRA forward pass: (dropout(x) @ A @ B) * scale
        var loraOutput = torch.matmul(torch.matmul(_dropout.forward(x), lora_A), lora_B) * Scale;

        return baseOutput + loraOutput;
    }

    /// <summary>
    /// Merge LoRA weights into base layer (permanent, irreversible).
    /// </summary>
    public void MergeWeights()
    {
        using (torch.no_grad())
        {
            // delta_w = (A @ B * scale).T
            var deltaW = torch.matmul(lora_A, lora_B).mul(Scale).t();
            BaseLayer.weight!.add_(deltaW);
        }
    }

    /// <summary>
    /// Return only LoRA parameters (for optimizer).
    /// </summary>
    public Parameter[] LoRAParameters => [lora_A, lora_B];
}
