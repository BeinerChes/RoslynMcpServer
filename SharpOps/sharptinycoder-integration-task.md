# Task: Integrate SharpTinyCoder ONNX Model into RoslynMcpServer

## Context

We have a trained 4.3M parameter transformer model (SharpTinyCoder) that generates C# method bodies from signatures. The model outputs a custom intermediate format called **SharpOps** — operation sequences that get compiled to valid C# via Roslyn's `SyntaxFactory`.

The model is already exported to ONNX. The tokenizer is already exported. Now we need C# inference code to run the model inside RoslynMcpServer.

### Files Available

- `d:\RMS\CSharpRobot\sharptinycoder.onnx` — the exported ONNX model
- `d:\RMS\CSharpRobot\tokenizer\tokenizer.json` — the BPE tokenizer (HuggingFace tokenizers format)

### Branch

Working branch: `SharpOps` — there's already a `SharpOps/` folder in the solution.

---

## What To Build

Create a `SharpOps.Inference` namespace/folder inside the `SharpOps/` project with three components:

### 1. `SharpOpsTokenizer.cs` — BPE Tokenizer in C#

Reads `tokenizer.json` and implements encoding/decoding.

The tokenizer has two types of tokens:
- **Special tokens** (never split by BPE): `<|pad|>`, `<|unk|>`, `<|bos|>`, `<|eos|>`, `<|output|>`, plus ~163 UPPERCASE SyntaxKind tokens (`BLOCK`, `IFSTATEMENT`, `RETURNSTATEMENT`, `INVOCATIONEXPRESSION`, `SIMPLEMEMBERACCESSEXPRESSION`, etc.) and ~13 SymbolKind tokens with colon (`NAMEDTYPE:`, `FIELD:`, `PARAMETER:`, `LOCAL:`, `METHOD:`, etc.)
- **BPE tokens**: regular subword tokens for identifiers like `ConfigureGeneratedCodeAnalysis`

Key behaviors:
- `Encode(string text) -> int[]` — tokenize input text to token IDs
- `Decode(int[] ids) -> string` — convert token IDs back to text
- Special tokens must be recognized as atomic units (never split)
- BPE tokens that form parts of an identifier should be joined WITHOUT spaces
- Spaces should appear only BEFORE special (UPPERCASE) tokens

Parse `tokenizer.json` to extract:
- `model.vocab` — the vocabulary (token string → ID)
- `model.merges` — BPE merge rules
- `added_tokens` — special tokens list

Consider using the NuGet package `Microsoft.ML.Tokenizers` if it supports loading HuggingFace tokenizer.json files — it may handle BPE logic out of the box. If not, implement BPE manually.

### 2. `SharpOpsInference.cs` — ONNX Runtime Inference Engine

Uses `Microsoft.ML.OnnxRuntime` NuGet package.

```csharp
public class SharpOpsInference : IDisposable
{
    private InferenceSession _session;
    private SharpOpsTokenizer _tokenizer;

    public SharpOpsInference(string modelPath, string tokenizerPath);

    // Main generation method
    public string Generate(string input, float temperature = 0.7f, float topP = 0.9f, int maxTokens = 512);

    public void Dispose();
}
```

**Generation loop** (no KV-cache, stateless):

```
1. Encode input text → token IDs (append <|bos|> at start)
2. Loop:
   a. Create input tensor from current token IDs
   b. Run ONNX session: input_ids → logits
   c. Take logits for LAST position only
   d. Apply temperature scaling: logits = logits / temperature
   e. Apply top-p (nucleus) sampling
   f. Sample next token from probability distribution
   g. Append next token to sequence
   h. If next token == <|eos|> → stop
   i. If length >= maxTokens → stop
3. Decode output tokens (everything after <|output|>) → SharpOps string
```

**ONNX model details:**
- Input: `input_ids` — shape `[1, seq_len]`, dtype `int64`
- Output: `logits` — shape `[1, seq_len, vocab_size]`
- No attention mask needed (the model uses causal masking internally)
- No KV-cache (recompute full sequence each step — model is tiny, this is fine)

**Important:** The model input is the FULL sequence (prompt + generated tokens so far). Each step reprocesses everything. This is intentional — no KV-cache for simplicity.

### 3. `SharpOpsService.cs` — High-Level Service

Ties inference to the rest of RoslynMcpServer.

```csharp
public class SharpOpsService
{
    private SharpOpsInference _inference;

    // Format: builds the model input from a method signature + fields
    public string BuildPrompt(string methodSignature, Dictionary<string, string> fields = null, string description = null);

    // Generate SharpOps from prompt, returns the raw SharpOps string
    public string GenerateSharpOps(string methodSignature, Dictionary<string, string> fields = null, string description = null);
}
```

**Prompt format** (this is what the model was trained on):

```
[optional description]

[method signature]

FIELDS:
[fieldName]: [fieldType]
[fieldName]: [fieldType]

<|output|>
```

Example:
```
Sort input and return first record if it has name != null

private static Record? DoSomeStuff(MyClass input)

FIELDS:
_db: Database

<|output|>
```

If no fields, omit the FIELDS section entirely. If no description, omit that line.

---

## NuGet Packages Needed

Add to the SharpOps project (or RoslynMcpServer.csproj if SharpOps isn't a separate project):

```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.21.*" />
```

Optionally for tokenizer:
```xml
<PackageReference Include="Microsoft.ML.Tokenizers" Version="1.0.*" />
```

---

## Model File Location

For now, hardcode or use configuration for model paths. The ONNX model and tokenizer.json will be placed in:
```
SharpOps/Models/sharptinycoder.onnx
SharpOps/Models/tokenizer/tokenizer.json
```

Copy them into the project and set `CopyToOutputDirectory = PreserveNewest` in the csproj.

---

## Testing

Create a simple test that:
1. Loads the model and tokenizer
2. Runs inference on this input:
```
public override void Initialize(AnalysisContext context)

<|output|>
```
3. Prints the generated SharpOps string
4. Verifies it starts with `BLOCK`

Also create a console test or unit test that measures generation time to confirm the model runs fast enough (should be well under 1 second on CPU for typical outputs).

---

## What NOT To Do

- Do NOT implement KV-cache — unnecessary for 4.3M params, adds complexity
- Do NOT implement the SharpOps → Roslyn SyntaxFactory compiler yet — that's a separate task
- Do NOT register an MCP tool yet — we need to verify inference works first
- Do NOT download any models — they are already exported and available locally

---

## Architecture Summary

```
Input: "void Foo(int x)\n\n<|output|>"
  ↓
SharpOpsTokenizer.Encode() → [token IDs]
  ↓
SharpOpsInference (ONNX Runtime loop) → [generated token IDs]
  ↓
SharpOpsTokenizer.Decode() → "BLOCK 2 RETURNSTATEMENT ..."
  ↓
(FUTURE: SharpOps Parser → Roslyn SyntaxFactory → valid C#)
```

## Output Format Reminder

The model generates UPPERCASE operation sequences like:
```
BLOCK 4 INVOCATIONEXPRESSION 1 SIMPLEMEMBERACCESSEXPRESSION ConfigureGeneratedCodeAnalysis IDENTIFIERNAME PARAMETER:context SIMPLEMEMBERACCESSEXPRESSION None IDENTIFIERNAME GeneratedCodeAnalysisFlags
```

Rules:
- UPPERCASE words = SyntaxKind operations (e.g., `BLOCK`, `IFSTATEMENT`, `RETURNSTATEMENT`)
- Numbers following a SyntaxKind = child count (e.g., `BLOCK 4` means block with 4 statements)
- `UPPERCASE:name` = SymbolKind reference (e.g., `PARAMETER:context`, `FIELD:_db`)
- lowercase/mixedCase words = identifiers (e.g., `ConfigureGeneratedCodeAnalysis`, `HasValue`)
- Spaces separate all tokens
