using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace SharpOps.Model;

/// <summary>
/// Model configuration matching Python CONFIG_TINY.
/// </summary>
public sealed class ModelConfig
{
    public int VocabSize { get; init; } = 4096;
    public int ContextLength { get; init; } = 512;
    public int EmbeddingDim { get; init; } = 256;
    public int NumLayers { get; init; } = 4;
    public int NumHeads { get; init; } = 4;
    public int NumKvHeads { get; init; } = 4;
    public int FfHiddenDim { get; init; } = 704;
    public float Dropout { get; init; } = 0.0f;
    public float RopeTheta { get; init; } = 10000.0f;
    public float NormEps { get; init; } = 1e-6f;
    public int PadTokenId { get; init; } = 0;
    public int BosTokenId { get; init; } = 2;
    public int EosTokenId { get; init; } = 3;
    public bool TieEmbeddings { get; init; } = true;
    public int HeadDim => EmbeddingDim / NumHeads;

    public static ModelConfig ConfigTiny => new()
    {
        VocabSize = 4096,
        ContextLength = 512,
        EmbeddingDim = 256,
        NumLayers = 4,
        NumHeads = 4,
        NumKvHeads = 4,
        FfHiddenDim = 704,
        Dropout = 0.0f,
    };

    /// <summary>
    /// Returns true if the model uses grouped query attention (NumKvHeads less than NumHeads)
    /// </summary>
    /// <returns></returns>
    public bool IsGroupedQueryAttention()
    { throw new NotImplementedException(); }
}

/// <summary>
/// Root Mean Square Layer Normalization.
/// </summary>
public sealed class RMSNorm : Module<Tensor, Tensor>
{
    private readonly Parameter weight;
    private readonly float _eps;

    public RMSNorm(int dim, float eps = 1e-6f) : base("RMSNorm")
    {
        _eps = eps;
        weight = Parameter(torch.ones(new long[] { dim }));
        RegisterComponents();
    }

    public override Tensor forward(Tensor x)
    {
        var rms = torch.rsqrt(x.pow(2).mean(new long[] { -1 }, keepdim: true) + _eps);
        return x * rms * weight;
    }
}

/// <summary>
/// Rotary Position Embedding (RoPE).
/// </summary>
public sealed class RotaryPositionEmbedding : Module
{
    private readonly int _dim;
    private int _maxSeqLen;
    private readonly float _theta;
    private Tensor _invFreq;
    private Tensor _cosCached = null!;
    private Tensor _sinCached = null!;

    public RotaryPositionEmbedding(int dim, int maxSeqLen = 2048, float theta = 10000.0f) : base("RotaryPositionEmbedding")
    {
        _dim = dim;
        _maxSeqLen = maxSeqLen;
        _theta = theta;

        _invFreq = 1.0f / torch.pow(torch.tensor(theta), torch.arange(0, dim, 2, dtype: float32) / dim);

        BuildCache(maxSeqLen);
        RegisterComponents();
    }

    private void BuildCache(int seqLen)
    {
        var t = torch.arange(seqLen, dtype: float32, device: _invFreq.device);
        var freqs = torch.outer(t, _invFreq);
        var emb = torch.cat([freqs, freqs], dim: -1);
        _cosCached = emb.cos();
        _sinCached = emb.sin();
    }

    public (Tensor cos, Tensor sin) Forward(Tensor x, int seqLen)
    {
        if (seqLen > _maxSeqLen)
        {
            _invFreq = _invFreq.to(x.device);
            BuildCache(seqLen);
            _maxSeqLen = seqLen;
        }

        // Move cached tensors to input device if needed
        if (_cosCached.device != x.device)
        {
            _cosCached = _cosCached.to(x.device);
            _sinCached = _sinCached.to(x.device);
        }

        return (
            _cosCached[TensorIndex.Slice(stop: seqLen)].to(x.dtype),
            _sinCached[TensorIndex.Slice(stop: seqLen)].to(x.dtype)
        );
    }
}

/// <summary>
/// Helper functions for RoPE.
/// </summary>
public static class RoPEHelpers
{
    public static Tensor RotateHalf(Tensor x)
    {
        var halfDim = x.shape[^1] / 2;
        var x1 = x[TensorIndex.Ellipsis, TensorIndex.Slice(stop: halfDim)];
        var x2 = x[TensorIndex.Ellipsis, TensorIndex.Slice(start: halfDim)];
        return torch.cat([-x2, x1], dim: -1);
    }

    public static (Tensor q, Tensor k) ApplyRotaryPosEmb(Tensor q, Tensor k, Tensor cos, Tensor sin)
    {
        cos = cos.unsqueeze(0).unsqueeze(0);
        sin = sin.unsqueeze(0).unsqueeze(0);

        var qEmbed = (q * cos) + (RotateHalf(q) * sin);
        var kEmbed = (k * cos) + (RotateHalf(k) * sin);
        return (qEmbed, kEmbed);
    }
}

/// <summary>
/// Multi-head attention with RoPE and optional GQA.
/// Uses non-generic Module since TorchSharp doesn't provide Module with 4+ input type params.
/// </summary>
public sealed class Attention : Module
{
    private readonly ModelConfig _config;
    private readonly int _numHeads;
    private readonly int _numKvHeads;
    private readonly int _headDim;
    private readonly int _numKvGroups;

    public Module<Tensor, Tensor> q_proj;
    public Module<Tensor, Tensor> k_proj;
    public Module<Tensor, Tensor> v_proj;
    public Module<Tensor, Tensor> o_proj;
    private readonly Dropout _dropout;

    public Attention(ModelConfig config) : base("Attention")
    {
        _config = config;
        _numHeads = config.NumHeads;
        _numKvHeads = config.NumKvHeads;
        _headDim = config.HeadDim;
        _numKvGroups = _numHeads / _numKvHeads;

        q_proj = Linear(config.EmbeddingDim, config.NumHeads * _headDim, hasBias: false);
        k_proj = Linear(config.EmbeddingDim, config.NumKvHeads * _headDim, hasBias: false);
        v_proj = Linear(config.EmbeddingDim, config.NumKvHeads * _headDim, hasBias: false);
        o_proj = Linear(config.NumHeads * _headDim, config.EmbeddingDim, hasBias: false);

        _dropout = Dropout(config.Dropout);
        RegisterComponents();
    }

    public Tensor forward(Tensor x, Tensor cos, Tensor sin, Tensor? attentionMask)
    {
        var batchSize = x.shape[0];
        var seqLen = x.shape[1];

        var q = q_proj.forward(x).view(batchSize, seqLen, _numHeads, _headDim).transpose(1, 2);
        var k = k_proj.forward(x).view(batchSize, seqLen, _numKvHeads, _headDim).transpose(1, 2);
        var v = v_proj.forward(x).view(batchSize, seqLen, _numKvHeads, _headDim).transpose(1, 2);

        (q, k) = RoPEHelpers.ApplyRotaryPosEmb(q, k, cos, sin);

        if (_numKvGroups > 1)
        {
            k = k.repeat_interleave(_numKvGroups, dim: 1);
            v = v.repeat_interleave(_numKvGroups, dim: 1);
        }

        var scale = 1.0f / MathF.Sqrt(_headDim);
        var scores = torch.matmul(q, k.transpose(-2, -1)) * scale;

        if (attentionMask is not null)
        {
            scores = scores + attentionMask;
        }

        var attnWeights = torch.nn.functional.softmax(scores, dim: -1);
        if (training)
        {
            attnWeights = _dropout.forward(attnWeights);
        }

        var attnOutput = torch.matmul(attnWeights, v);
        attnOutput = attnOutput.transpose(1, 2).contiguous().view(batchSize, seqLen, -1);
        return o_proj.forward(attnOutput);
    }
}

/// <summary>
/// SwiGLU feed-forward network.
/// </summary>
public sealed class SwiGLU : Module<Tensor, Tensor>
{
    public Module<Tensor, Tensor> gate_proj;
    public Module<Tensor, Tensor> up_proj;
    public Module<Tensor, Tensor> down_proj;
    private readonly Dropout _dropout;

    public SwiGLU(ModelConfig config) : base("SwiGLU")
    {
        gate_proj = Linear(config.EmbeddingDim, config.FfHiddenDim, hasBias: false);
        up_proj = Linear(config.EmbeddingDim, config.FfHiddenDim, hasBias: false);
        down_proj = Linear(config.FfHiddenDim, config.EmbeddingDim, hasBias: false);
        _dropout = Dropout(config.Dropout);
        RegisterComponents();
    }

    public override Tensor forward(Tensor x)
    {
        var gate = torch.nn.functional.silu(gate_proj.forward(x));
        var up = up_proj.forward(x);
        return _dropout.forward(down_proj.forward(gate * up));
    }
}

/// <summary>
/// Single transformer block with pre-norm.
/// </summary>
public sealed class TransformerBlock : Module
{
    public readonly RMSNorm attention_norm;
    public readonly Attention attention;
    public readonly RMSNorm ffn_norm;
    public readonly SwiGLU ffn;

    public TransformerBlock(ModelConfig config) : base("TransformerBlock")
    {
        attention_norm = new RMSNorm(config.EmbeddingDim, config.NormEps);
        attention = new Attention(config);
        ffn_norm = new RMSNorm(config.EmbeddingDim, config.NormEps);
        ffn = new SwiGLU(config);
        RegisterComponents();
    }

    public Tensor forward(Tensor x, Tensor cos, Tensor sin, Tensor? attentionMask)
    {
        x = x + attention.forward(attention_norm.forward(x), cos, sin, attentionMask);
        x = x + ffn.forward(ffn_norm.forward(x));
        return x;
    }
}

/// <summary>
/// SharpTinyCoder: LLaMA-style transformer for C# code generation.
/// Uses non-generic Module since forward returns a tuple.
/// </summary>
public sealed class SharpTinyCoder : Module
{
    private readonly ModelConfig _config;
    public readonly Embedding embed_tokens;
    private readonly RotaryPositionEmbedding _rope;
    public readonly ModuleList<TransformerBlock> layers;
    public readonly RMSNorm norm;
    private readonly Linear? _lmHead;
    private Tensor _causalMask;

    public ModelConfig Config => _config;

    public SharpTinyCoder(ModelConfig config) : base("SharpTinyCoder")
    {
        _config = config;

        embed_tokens = Embedding(config.VocabSize, config.EmbeddingDim);

        _rope = new RotaryPositionEmbedding(
            config.HeadDim,
            maxSeqLen: config.ContextLength,
            theta: config.RopeTheta);

        layers = new ModuleList<TransformerBlock>();
        for (int i = 0; i < config.NumLayers; i++)
        {
            layers.Add(new TransformerBlock(config));
        }

        norm = new RMSNorm(config.EmbeddingDim, config.NormEps);

        if (!config.TieEmbeddings)
        {
            _lmHead = Linear(config.EmbeddingDim, config.VocabSize, hasBias: false);
        }

        _causalMask = torch.full([config.ContextLength, config.ContextLength], float.NegativeInfinity)
            .triu(diagonal: 1);

        RegisterComponents();
    }

    public (Tensor logits, Tensor? loss) forward(Tensor input_ids, Tensor? attention_mask, Tensor? labels)
    {
        var seqLen = (int)input_ids.shape[1];

        var hiddenStates = embed_tokens.forward(input_ids);

        var (cos, sin) = _rope.Forward(hiddenStates, seqLen);

        // Move causal mask to input device if needed
        if (_causalMask.device != input_ids.device)
        {
            _causalMask = _causalMask.to(input_ids.device);
        }

        var causalMask = _causalMask[TensorIndex.Slice(stop: seqLen), TensorIndex.Slice(stop: seqLen)];

        if (attention_mask is not null)
        {
            var paddingMask = attention_mask.unsqueeze(1).unsqueeze(2).to(hiddenStates.dtype);
            paddingMask = (1.0f - paddingMask) * torch.finfo(hiddenStates.dtype).min;
            causalMask = causalMask.unsqueeze(0) + paddingMask;
        }

        foreach (var layer in layers)
        {
            hiddenStates = layer.forward(hiddenStates, cos, sin, causalMask);
        }

        hiddenStates = norm.forward(hiddenStates);

        Tensor logits;
        if (_lmHead is not null)
        {
            logits = _lmHead.forward(hiddenStates);
        }
        else
        {
            logits = torch.nn.functional.linear(hiddenStates, embed_tokens.weight!);
        }

        Tensor? loss = null;
        if (labels is not null)
        {
            var shiftLogits = logits[TensorIndex.Ellipsis, TensorIndex.Slice(stop: -1), TensorIndex.Colon].contiguous();
            var shiftLabels = labels[TensorIndex.Ellipsis, TensorIndex.Slice(start: 1)].contiguous();

            loss = torch.nn.functional.cross_entropy(
                shiftLogits.view(-1, _config.VocabSize),
                shiftLabels.view(-1),
                ignore_index: -100);
        }

        return (logits, loss);
    }

    public long NumParameters(bool trainableOnly = true)
    {
        if (trainableOnly)
            return parameters().Where(p => p.requires_grad).Sum(p => p.numel());
        return parameters().Sum(p => p.numel());
    }

    /// <summary>
    /// Re-registers all sub-modules after external modification (e.g., LoRA merge). Fixes save_py producing empty files when module tree is stale.
    /// </summary>
    public void ReRegisterComponents() => RegisterComponents();
}
