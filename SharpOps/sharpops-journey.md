# Building SharpTinyCoder: A 4.3M Parameter C# Code Generator from Scratch

*A journey through tokenizers, transformers, and the discovery that code isn't just text.*

---

## The Goal

Build a tiny (4.3M parameter) transformer model that generates C# method bodies from signatures. Not to compete with GPT-4 or Copilot, but to prove that small, specialized models can be useful for narrow tasks.

The model integrates with a Roslyn-based MCP (Model Context Protocol) server, enabling Claude or other AI assistants to have a specialized "C# brain" for code generation.

**Spoiler:** It works! 5/5 exact matches, all compiling to valid C#.

---

## Part 1: The Naive Approach (Text-Based)

### Initial Architecture

We started with a modern LLaMA-style decoder-only transformer:

| Component | Choice | Why |
|-----------|--------|-----|
| Attention | Multi-Head with RoPE | Modern, efficient positional encoding |
| Normalization | RMSNorm | Faster than LayerNorm, works well |
| Feed-Forward | SwiGLU | Better than ReLU for language modeling |
| Parameters | ~4.3M | Fits on consumer GPU, fast iteration |

### Mistake #1: Using GPT-2 Tokenizer

GPT-2 was trained on internet text, not code:

```
"_sortByAscending" → ["_", "sort", "By", "Asc", "ending"]  // 5 tokens!
```

### Fix: Custom BPE Tokenizer

Trained a custom tokenizer on our C# corpus. Result: `_sortByAscending` → 1-2 tokens.

### Mistake #2: Too Much Context

Initial training data included ALL class fields (600+ tokens) when only 2 were used.

### Fix: Roslyn's DataFlow Analysis

```csharp
var dataFlow = semanticModel.AnalyzeDataFlow(methodBody);
var usedFields = dataFlow.ReadInside.Concat(dataFlow.WrittenInside)
    .Where(s => s.Kind == SymbolKind.Field);
```

Token count dropped from 600+ to 150-200.

### Success: Text-Based Model Works!

After 366 epochs on 167 examples: **4/5 exact matches**. Proof of concept complete!

---

## Part 2: The Insight — Code Is Not Text

**The model was learning two things at once:**
1. C# syntax (brackets, semicolons, keywords)
2. Algorithm structure (if-then, loops, assignments)

What if we separated these? The model only learns **algorithms**, and Roslyn reconstructs **syntax**.

### Introducing SharpOps

Instead of generating text like:
```csharp
if (x > 0) return x;
```

Generate operation sequences that Roslyn's `SyntaxFactory` compiles to valid C#.

---

## Part 3: The Tokenizer Nightmare (Many Attempts)

This is where we spent 80% of our time. Multiple approaches, multiple failures.

### Attempt 1: Space-Separated Lowercase

```
Block 3 IdentifierName Field _cached SimpleMemberAccessExpression HasValue
```

**Problem:** BPE split identifiers AND SyntaxKinds were ambiguous with identifiers.

```
"IdentifierName Parameter context" 
// Is "Parameter" a SymbolKind or an identifier named "Parameter"?
```

### Attempt 2: Special Tokens for SyntaxKinds

Made all SyntaxKinds special tokens (never split by BPE).

**Problem:** Decoder added spaces between ALL tokens:
```
"Write P end ing" instead of "WritePending"
```

### Attempt 3: Smart Decoder

Only add spaces before SyntaxKinds, join everything else.

**Problem:** Still ambiguous. Words like "None", "State", "And" could be identifiers or look like SyntaxKinds.

### Attempt 4: Custom Tokenizer (No BPE)

Each unique word becomes a token. 

**Problem:** Vocab exploded to 24K tokens. Rare words became `<|unk|>` and were lost forever.

### Attempt 5: Hybrid Tokenizer

Keep ops atomic, use BPE for identifiers.

**Problem:** Still couldn't distinguish SyntaxKinds from identifiers reliably.

---

## Part 4: The Breakthrough — UPPERCASE Format

After many failed attempts, we realized: **fix the data format, not the tokenizer.**

### The Solution

**UPPERCASE for SyntaxKinds, colon for arguments:**

```
BLOCK 4 INVOCATIONEXPRESSION 1 SIMPLEMEMBERACCESSEXPRESSION ConfigureGeneratedCodeAnalysis IDENTIFIERNAME PARAMETER:context
```

### Why This Works Perfectly

| Token | Type | How Parser Knows |
|-------|------|------------------|
| `BLOCK` | SyntaxKind | UPPERCASE |
| `4` | Count argument | Follows SyntaxKind |
| `ConfigureGeneratedCodeAnalysis` | Identifier | lowercase/mixed |
| `PARAMETER:context` | SymbolKind:name | UPPERCASE + colon |

### No Ambiguity!

```
Before: "IdentifierName Parameter context"
        Is "Parameter" a SymbolKind or identifier? 🤔

After:  "IDENTIFIERNAME PARAMETER:context"
        PARAMETER is clearly SymbolKind, context is the name ✅
```

### Tokenizer Strategy

```python
special_tokens = [
    "<|pad|>", "<|unk|>", "<|bos|>", "<|eos|>", "<|output|>",
    
    # SyntaxKinds (UPPERCASE) - ~163 tokens
    "BLOCK", "IFSTATEMENT", "RETURNSTATEMENT", "LOCALDECLARATIONSTATEMENT",
    "INVOCATIONEXPRESSION", "SIMPLEMEMBERACCESSEXPRESSION", ...
    
    # SymbolKinds with colon prefix - ~13 tokens
    "NAMEDTYPE:", "FIELD:", "PARAMETER:", "LOCAL:", "METHOD:", ...
]
```

BPE handles identifiers. Special tokens are protected. **Simple and robust!**

---

## Part 5: Final Results — IT WORKS!

### 5/5 Exact Matches, All Compiled!

```json
{
  "index": 0,
  "match": true,
  "compiled": "{\r\n    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);\r\n    context.EnableConcurrentExecution();\r\n    context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);\r\n    context.RegisterSyntaxNodeAction(AnalyzeQualifiedName, SyntaxKind.QualifiedName);\r\n}",
  "compileError": null
}
```

### Complex Example (Nested Patterns!)

**Input:**
```
private static void AnalyzeQualifiedName(SyntaxNodeAnalysisContext context)

FIELDS:
Rule: DiagnosticDescriptor
```

**Generated SharpOps:**
```
BLOCK 6 IFSTATEMENT 1 ISPATTERNEXPRESSION SIMPLEMEMBERACCESSEXPRESSION Node IDENTIFIERNAME PARAMETER:context NOTPATTERN DECLARATIONPATTERN QualifiedNameSyntax:qualifiedName RETURNSTATEMENT ...
```

**Compiled C#:**
```csharp
{
    if (context.Node is not QualifiedNameSyntax qualifiedName)
        return;
    if (qualifiedName.Ancestors().OfType<UsingDirectiveSyntax>().Any())
        return;
    if (qualifiedName.Parent is QualifiedNameSyntax parentQualified)
    {
        var parentName = parentQualified.ToString();
        if (HasMatchingNamespace(parentName))
            return;
    }

    var fullName = qualifiedName.ToString();
    var matchingNamespace = FindMatchingNamespace(fullName);
    if (matchingNamespace != null)
    {
        var diagnostic = Diagnostic.Create(Rule, qualifiedName.GetLocation(), matchingNamespace);
        context.ReportDiagnostic(diagnostic);
    }
}
```

**That's REAL, WORKING C# code generated by a 4.3M parameter model!**

---

## Part 6: Understanding the Architecture

### What Is a Token?

A token is the atomic unit for a language model. The model sees token IDs, not characters.

**BPE (Byte-Pair Encoding)** learns to merge frequent character pairs:
```
'r' + 'e' → 're' → 'ret' → 'return'
```

### Special Tokens vs BPE Tokens

| Token Type | Example | Behavior |
|------------|---------|----------|
| **Special** | `BLOCK`, `IFSTATEMENT` | Never split (protected) |
| **BPE** | `ConfigureGeneratedCodeAnalysis` | May split, decoder joins |

### The Model

```
Input IDs → Embedding → [Transformer Blocks × 4] → LM Head → Logits
                              ↓
                    RoPE positional encoding
```

**Each Transformer Block:**
```
Input → RMSNorm → Multi-Head Attention → + (residual)
      → RMSNorm → SwiGLU FFN → + (residual) → Output
```

### Parameter Count

```
Embeddings:     4096 × 256 = 1.0M
4 Layers:       4 × 0.8M   = 3.2M
Final norm:     256        = 0.0003M
─────────────────────────────
Total:          ~4.3M parameters
```

---

## Part 7: Lessons Learned

### 1. Data Format > Tokenizer Tricks

We tried 5+ tokenizer approaches. The breakthrough was changing the **data format** (UPPERCASE), not the tokenizer algorithm.

### 2. Ambiguity Is The Enemy

```
"IdentifierName Parameter context"  // Ambiguous
"IDENTIFIERNAME PARAMETER:context"  // Crystal clear
```

### 3. Overfit First, Always

If model can't memorize 5 examples perfectly, something is broken. Don't scale until overfit works.

### 4. The Pipeline Matters

```
Roslyn Extractor → SharpOps → Tokenizer → Model → Decoder → Roslyn Compiler → C#
       ✅              ✅          ✅        ✅       ✅            ✅           ✅
```

Every step must work. We had working model but broken decoder → whole thing failed.

### 5. Roslyn Is The Source of Truth

No hardcoded lists. SyntaxKinds extracted from Roslyn. When C# adds new syntax, just re-extract.

---

## Part 8: The Complete Format

### Input (C# Signature)
```
Sort input and return first record if it has name != null

private static Record? DoSomeStuff(MyClass input)

FIELDS:
_db: Database

<|output|>
```

### Output (SharpOps)
```
BLOCK 3 LOCALDECLARATIONSTATEMENT VARIABLEDECLARATION var:1 IDENTIFIERNAME NAMEDTYPE:var VARIABLEDECLARATOR sorted EQUALSVALUECLAUSE INVOCATIONEXPRESSION 1 SIMPLEMEMBERACCESSEXPRESSION OrderBy IDENTIFIERNAME PARAMETER:input SIMPLELAMBDAEXPRESSION x SIMPLEMEMBERACCESSEXPRESSION Name IDENTIFIERNAME PARAMETER:x ...
```

### Compiled C#
```csharp
{
    var sorted = input.OrderBy(x => x.Name);
    var first = sorted.FirstOrDefault();
    if (first?.Name != null)
        return first;
    return null;
}
```

---

## Part 9: What's Next

### Ready Now
- ✅ 34K training examples extracted
- ✅ UPPERCASE format working
- ✅ 5/5 overfit validation passing
- ✅ Roslyn compiler generating valid C#

### Next Steps
1. **Train on full 34K dataset** — Real generalization
2. **ONNX export** — Run in C# without Python
3. **MCP integration** — Claude calls SharpTinyCoder
4. **Raspberry Pi 5 deployment** — Standalone inference device!

---

## Appendix: Complete Error Timeline

| Attempt | Approach | Problem | Time Spent |
|---------|----------|---------|------------|
| 1 | GPT-2 tokenizer | Wrong domain, fragments code | 2 hours |
| 2 | Custom BPE, space-separated | SyntaxKinds ambiguous | 4 hours |
| 3 | Special tokens for SyntaxKinds | Decoder added wrong spaces | 3 hours |
| 4 | Smart decoder (space before SyntaxKind) | "None", "State" ambiguous | 4 hours |
| 5 | Custom tokenizer (no BPE) | 24K vocab, UNK issues | 6 hours |
| 6 | Hybrid tokenizer | Still ambiguous | 3 hours |
| **7** | **UPPERCASE format** | **Works perfectly!** | 1 hour |

**Total tokenizer debugging: ~23 hours**

The model architecture took 2 hours. The tokenizer took 23 hours. **Data format is everything.**

---

## Final Thoughts

Building SharpTinyCoder taught us that the hardest part of ML isn't the model — it's the data pipeline.

The transformer architecture is commoditized. Everyone uses the same attention, normalization, and activation patterns. The real work is in:

- **Data format** — How do you represent the problem?
- **Tokenization** — How does the model see the data?
- **Unambiguity** — Can the parser reconstruct the output?

SharpOps with UPPERCASE format solved all three. A 4.3M parameter model, trained on consumer hardware, generates valid C# code. Not production-ready, but proof that the approach works.

The journey continues.

---

*Built with Claude + Roslyn + PyTorch on an RTX 4060 Ti.*

---

## Code Samples

### SharpOps Format
```
BLOCK 4 INVOCATIONEXPRESSION 1 SIMPLEMEMBERACCESSEXPRESSION ConfigureGeneratedCodeAnalysis IDENTIFIERNAME PARAMETER:context SIMPLEMEMBERACCESSEXPRESSION None IDENTIFIERNAME GeneratedCodeAnalysisFlags
```

### Training Data (JSONL)
```json
{
  "input": "public override void Initialize(AnalysisContext context)\n\n<|output|>",
  "output": " BLOCK 4 INVOCATIONEXPRESSION 1 SIMPLEMEMBERACCESSEXPRESSION ConfigureGeneratedCodeAnalysis IDENTIFIERNAME PARAMETER:context ...",
  "strings": [],
  "sourceFile": "Analyzer.cs"
}
```

### Roslyn Compiler Output
```csharp
{
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();
    context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
    context.RegisterSyntaxNodeAction(AnalyzeQualifiedName, SyntaxKind.QualifiedName);
}
```

**From 4.3M parameters to working C# code. Mission accomplished.** 🚀
