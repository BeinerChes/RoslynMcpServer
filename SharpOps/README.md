# SharpOps

Training data generator for C# code generation models. Extracts method bodies as structured ops (SharpOps format) that can be compiled back to valid C# code.

## Format

SharpOps uses UPPERCASE tokens for unambiguous parsing:

```
BLOCK 4 LOCALDECLARATIONSTATEMENT VARIABLEDECLARATION var:1 IDENTIFIERNAME LOCAL:x ...
```

- **UPPERCASE** = SyntaxKind (BLOCK, IFSTATEMENT, RETURNSTATEMENT, etc.)
- **Arguments** follow the SyntaxKind (counts, names, types)
- **IDENTIFIERNAME** includes SymbolKind: `IDENTIFIERNAME PARAMETER:context`, `IDENTIFIERNAME LOCAL:x`, `IDENTIFIERNAME FIELD:_cache`

## Installation

```bash
cd SharpOps
dotnet build
```

## Usage

### Generate Training Data

**From a solution (.sln/.slnx):**
```bash
dotnet run -- "path/to/Solution.slnx" "output/folder" [options]
```

**From a single project (.csproj):**
```bash
dotnet run -- "path/to/Project.csproj" "output/folder" [options]
```

**From a directory of projects:**
```bash
dotnet run -- "path/to/directory" "output/folder" [options]
```

**Options:**
| Option | Default | Description |
|--------|---------|-------------|
| `--min-statements N` | 3 | Minimum statements in method body |
| `--max-ops N` | unlimited | Maximum ops in sequence |
| `--max-string N` | 100 | Maximum string literal length |
| `--include-tests` | false | Include test projects |
| `--no-require-doc` | false | Don't require XML documentation |

**Example - Generate from multiple projects into separate folders:**
```bash
# Each project gets its own folder
dotnet run -- "repos/MyLib/MyLib.csproj" "data/MyLib" --no-require-doc
dotnet run -- "repos/MyLib/MyLib.Core.csproj" "data/MyLib.Core" --no-require-doc
```

### Output Files

Each generation creates:
- `ProjectName.jsonl` - Training samples (one JSON per line)
- `syntax_kinds.txt` - UPPERCASE SyntaxKind tokens for BPE tokenizer
- `symbol_kinds.txt` - UPPERCASE SymbolKind tokens for BPE tokenizer

**JSONL format:**
```json
{
  "input": "public void MyMethod(int x)\n\n<|output|>",
  "output": " BLOCK 1 RETURNSTATEMENT IDENTIFIERNAME PARAMETER:x",
  "strings": ["literal1", "literal2"],
  "sourceFile": "path/to/File.cs",
  "line": 42,
  "method": "Namespace.Class.MyMethod(int)"
}
```

### Validate/Compile Ops to C#

**Test compilation from validation results:**
```bash
dotnet run -- validate "path/to/validation_results.json"
```

Input JSON format (array of predictions):
```json
[
  {
    "index": 0,
    "input": "method signature",
    "expected": " BLOCK ...",
    "generated": " BLOCK ...",
    "match": true
  }
]
```

Output: `validation_results_compiled.json` with `compiled` (C# code) and `compileError` fields.

**Debug/test ops string directly:**
```bash
dotnet run -- debug BLOCK 1 RETURNSTATEMENT IDENTIFIERNAME LOCAL:x
```

Output:
```
Parsed 3 ops:
  Kind=Block, Arg=1, SymbolKind=
  Kind=ReturnStatement, Arg=, SymbolKind=
  Kind=IdentifierName, Arg=x, SymbolKind=Local

=== COMPILED ===
{
    return x;
}
```

**Compile from JSONL file:**
```bash
dotnet run -- compile "path/to/data.jsonl" [line-number]
```

### Export Special Tokens

Export UPPERCASE tokens for BPE tokenizer:
```bash
dotnet run -- export-tokens "output/special_tokens.txt"
```

## Token Format

### SyntaxKinds (UPPERCASE)
```
BLOCK
IFSTATEMENT
RETURNSTATEMENT
INVOCATIONEXPRESSION
SIMPLEMEMBERACCESSEXPRESSION
IDENTIFIERNAME
VARIABLEDECLARATION
...
```

### SymbolKinds (in IDENTIFIERNAME argument)
```
PARAMETER - method parameter
LOCAL - local variable
FIELD - class field
PROPERTY - class property
METHOD - method reference
NAMEDTYPE - type name
NAMESPACE - namespace
```

### Examples

| C# Code | SharpOps |
|---------|----------|
| `return x;` | `RETURNSTATEMENT IDENTIFIERNAME LOCAL:x` |
| `x.ToString()` | `INVOCATIONEXPRESSION 0 SIMPLEMEMBERACCESSEXPRESSION ToString IDENTIFIERNAME LOCAL:x` |
| `var y = 1;` | `LOCALDECLARATIONSTATEMENT VARIABLEDECLARATION var:1 IDENTIFIERNAME var VARIABLEDECLARATOR y EQUALSVALUECLAUSE NUMERICLITERALEXPRESSION 1` |
| `if (x == null) return;` | `IFSTATEMENT 1 EQUALSEXPRESSION IDENTIFIERNAME LOCAL:x NULLLITERALEXPRESSION RETURNSTATEMENT` |

## Architecture

```
C# Source Code
      │
      ▼
┌─────────────────┐
│ SharpOpsExtractor│  (Roslyn AST → SharpOp[])
└─────────────────┘
      │
      ▼
┌─────────────────┐
│  SharpOpsSequence│  (SharpOp[] + StringTable)
└─────────────────┘
      │
      ▼ ToString()
┌─────────────────┐
│   JSONL Output  │  (Training data)
└─────────────────┘

      ▲ ParseTokens()
      │
┌─────────────────┐
│ SharpOpsCompiler│  (SharpOp[] → Roslyn AST → C#)
└─────────────────┘
```
