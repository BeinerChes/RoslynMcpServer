# SharpTinyCoder: How the Self-Improving Model Works

The Roslyn MCP server includes **SharpTinyCoder**, a tiny (4.3M parameter) code generation model that runs locally on your machine. You don't interact with it directly — Claude Code uses it behind the scenes when writing C# code. Over time, Claude Code teaches the model by correcting its mistakes, and the model gets better at your codebase's patterns.

This document explains what's happening under the hood.

## The Big Picture

```
You ask Claude Code to add a method
        ↓
Claude Code asks the tiny model to try first
        ↓
   Model generates code (maybe right, maybe wrong)
        ↓
Claude Code reviews it, fixes if needed
        ↓
   The correction becomes training data
        ↓
   Eventually: fine-tune → model improves
        ↓
   Next time, the model does better
```

It's a feedback loop. The more Claude Code works on your project, the better the model gets at your patterns.

## What SharpTinyCoder Actually Does

The model doesn't generate C# directly. It generates **SharpOps**, a compact intermediate representation designed for tiny models. The SharpOps compiler then turns it into real C#.

```
Method Signature + Context → [Model] → SharpOps IR → [Compiler] → C# Code
```

The model is intentionally small. It won't write complex algorithms, but it handles common patterns: simple returns, field access, null checks, LINQ expressions, builder patterns, etc. The value isn't in being perfect — it's in learning *your* project's conventions over time.

## How Claude Code Uses the Model

### Adding Methods

When Claude Code adds a new method to a class, it internally calls `AddMember` with `auto=true`. This means:

1. Claude Code sends just the method signature to the model
2. The model generates a body
3. Claude Code **reviews the result** — three things can happen:

| Outcome | What Claude Code Does | Training Data? |
|---------|----------------------|----------------|
| Code is correct | Keeps it, moves on | No — we don't train on the model's own output |
| Code compiles but is wrong | Calls `UpdateMethod` with the correct code | **Yes** — the correction is saved |
| Model fails entirely | A `NotImplementedException` stub is inserted, then Claude Code writes the real implementation via `UpdateMethod` | **Yes** — the implementation is saved |

The key insight: **corrections are training data**. Every time Claude Code fixes the model's output, that fix is recorded as an example of "given this signature, here's what the code should be."

### Editing Existing Methods

When Claude Code modifies an existing method via `UpdateMethod`, that also collects training data. So even normal code editing contributes to the model's learning.

### What Doesn't Collect Data

- `AddMember(auto=true)` where the model's output is accepted as-is — no point training on what it already knows
- Adding fields, properties, events — these are structural, not generated
- Read-only operations (GetMethodBody, GetTypeMembers, FindSymbol, etc.)

## Training Data

Training examples accumulate in `.roslyn-mcp/Models/finetune/dataset/` as JSONL files (one per solution). Each record contains:

- The method signature and class context (fields, description)
- The SharpOps IR extracted from the final correct C# code
- Metadata (source file, type name, timestamp)

Records are keyed by fully-qualified method name — if the same method is updated multiple times, only the latest version is kept.

## Fine-Tuning

### How It Works

When enough corrections have accumulated (even 5-10 can help), Claude Code calls `Finetune()`. This:

1. **Returns immediately** — training runs in the background, Claude Code can keep working
2. **Uses sensible defaults** — no configuration needed:

| Setting | Value | Why |
|---------|-------|-----|
| LoRA rank | 8 | Good balance for a 4.3M model |
| LoRA alpha | 32 | Scale factor = alpha/rank = 4.0 |
| Target modules | query/value attention | Most efficient layers to adapt |
| Max epochs | 200 | Runs all epochs (no early stopping without validation split) |
| Learning rate | 1e-4 | Standard for LoRA |
| Temperature | 0.0 | Greedy decoding — tiny models can't handle sampling |

3. **Runs to completion** — with no validation split, training runs all 200 epochs (loss typically converges well before then)
4. **Merges weights** back into the base model
5. **Hot-reloads** the model — no restart needed, inference immediately uses the new weights
6. **Archives the dataset** — moves training data to `finetune/archive/{timestamp}/` so the next training run only uses fresh examples

### Status Checking

Claude Code can call `Finetune()` again to check progress:
- `"running"` — still training (log file available for details)
- `"completed"` — done, returns best loss, epochs run, whether it early-stopped

### Typical Training Output

```
Epoch   1/200 | Train: 4.7312
Epoch  50/200 | Train: 0.0607
Epoch 100/200 | Train: 0.0036
Epoch 150/200 | Train: 0.0019
Epoch 200/200 | Train: 0.0015

[6/6] Merging LoRA weights...
Saving merged model to .roslyn-mcp/Models/checkpoint.pt...
  Saved 38 params — 17,060,981 bytes (Python format)

Fine-tuning complete!
  Best loss: ∞
  Merged checkpoint: .roslyn-mcp/Models/checkpoint.pt
  Epochs: 200
[Finetune] Model reloaded for inference.
[Finetune] Dataset archived (1 file)
```

## The Feedback Loop in Practice

Here's what a typical session looks like from the model's perspective:

**Day 1:** Claude Code works on your project. The model tries to generate 20 methods. It gets 8 right, 12 wrong. Claude Code corrects the 12 — that's 12 training examples.

**Fine-tune:** Someone (or Claude Code) calls `Finetune()`. The model trains on those 12 examples. Loss drops from 9.0 to 1.5.

**Day 2:** Claude Code works again. The model now gets 14 out of 20 right — it learned patterns like "this project uses `_repository` not `_db`" and "methods in this codebase return `Result<T>` not raw values."

**Over time:** The model becomes specialized to *your* codebase. It's not a general-purpose model — it's a tiny pattern matcher that gets better at the specific conventions, field names, and code style used in your project.

## Real-World Test: TaskBoard (SharpOps.Examples)

This section shows actual test runs of the full workflow. We created a `TaskBoard` class in the `SharpOps.Examples` project and let the model try generating every method across multiple rounds, fine-tuning between each.

### Setup

```
AddType(projectName: "SharpOps.Examples", typeName: "TaskBoard")
AddMember(typeName: "TaskBoard", memberCode: "private readonly List<TaskItem> _tasks = [];", auto: false)
AddMember(typeName: "TaskBoard", memberCode: "private int _nextId = 1;", auto: false)
```

Fields and types use `auto: false` — nothing to generate.

### Round 1: Base Model (No Fine-Tuning)

Claude Code added 7 methods, each time letting the model try first with `auto: true`:

| Method | Model Result | What Happened |
|--------|-------------|---------------|
| `AddTask(string title, Priority priority)` | Failed — no generation | Stub inserted, Claude Code wrote correct code |
| `GetTask(int id)` | Generated `return new IsNot(AddTask);` | Nonsense — Claude Code replaced with `_tasks.FirstOrDefault(...)` |
| `CompleteTask(int id)` | Generated `throw new ArgumentException();` | Wrong — Claude Code wrote the find-and-set logic |
| `GetPendingTasks()` | Failed — no generation | Claude Code wrote LINQ filter + OrderByDescending |
| `RemoveTask(int id)` | Failed — no generation | Claude Code wrote find-and-remove logic |
| `GetByPriority(Priority priority)` | Failed — no generation | Claude Code wrote LINQ Where filter |
| `GetCompletionRate()` | Failed — no generation | Claude Code wrote count ratio with zero-check |

**Score: 0/7 correct.** Expected for a pre-trained model seeing unfamiliar types for the first time. Each of the 7 corrections was automatically saved as training data.

**Fine-tune:** 103 epochs, loss 9.2 → 0.92, early-stopped. Model reloaded, dataset archived.

### Round 2: After First Fine-Tune — 7/7 Success!

After fixing several bugs discovered during testing, the C# LoRA training pipeline now works correctly. Methods were cleared back to stubs and retested:

| Method | Model Result | Match? |
|--------|-------------|--------|
| `AddTask(string title, Priority priority)` | Correct — object initializer with `_nextId++`, Add, return | ✓ |
| `GetTask(int id)` | Correct — `FirstOrDefault` with lambda | ✓ |
| `CompleteTask(int id)` | Correct — find, null check, set `IsCompleted`, return bool | ✓ |
| `GetPendingTasks()` | Correct — `Where(!IsCompleted).OrderByDescending(Priority).ToList()` | ✓ |
| `RemoveTask(int id)` | Correct — find, null check, Remove, return bool | ✓ |
| `GetByPriority(Priority priority)` | Correct — `Where` filter with `.ToList()` | ✓ |
| `GetCompletionRate()` | Correct — zero check, `Count()` with predicate, division | ✓ |

**Score: 7/7 correct.** The model learned all 7 patterns from a single fine-tuning session.

**Fine-tune details:** 200 epochs, loss dropped from 4.73 to 0.0015. Model hot-reloaded, dataset archived.

### Bugs Fixed During Testing

Several bugs were discovered and fixed during the TaskBoard test:

#### 1. Field Ordering Bug

The model sees fields in its prompt as indexed symbols (`FIELD:$0`, `FIELD:$1`). Three places must agree on field order:

| Component | Order Used | Effect |
|-----------|-----------|--------|
| `ContextExtractor` (training data) | Alphabetical (`.OrderBy(f => f.Name)`) | Training examples use alphabetical indices |
| `PopulateSymbolTablesFromType` (compiler) | Alphabetical | Compiled code resolves indices alphabetically |
| `HandleAutoGenerateAsync` (inference prompt) | **Declaration order** (bug!) | Model saw `_tasks` as $0, but compiler resolved $0 to `_nextId` |

**Fix:** Changed `HandleAutoGenerateAsync` to sort fields alphabetically, matching the other two components.

#### 2. Checkpoint Save Format

TorchSharp's `save_py()` is broken after LoRA merge — it writes empty/corrupt ZIP files. The workaround:
1. Serialize merged model parameters as raw float32 binary + manifest
2. Shell out to Python `torch.save()` to assemble a proper pickle file
3. Load with `load_py()` which handles flat state dicts correctly

#### 3. Container Checkpoint Loading

Python training scripts save checkpoints as container dicts (`{"model_state_dict": ..., "epoch": ...}`), but TorchSharp's `load_py()` expects flat state dicts. Fixed by detecting container format and extracting `model_state_dict` via Python before loading.

#### 4. Greedy Decoding

The 4.3M parameter model can't handle temperature sampling — it produces garbage. Fixed by defaulting temperature to 0.0 (greedy decoding).

#### 5. LoRA Hyperparameter Parity

C# defaults didn't match Python's `finetune.py`:
- Alpha: C# had 16, Python has 32. Fixed.
- MaxLength: C# had 512, Python has 2048. Fixed (then reverted to 512 for GPU memory).

#### 6. Tokenizer Added Tokens

Only 5 special tokens were hardcoded, but the tokenizer has 158. Fixed to load all from `tokenizer.json`.

### What This Proves

The TaskBoard test validates that the **entire pipeline works end-to-end**:
1. Training data collection via `UpdateMethod`
2. C# LoRA fine-tuning with TorchSharp
3. LoRA weight merging into base model
4. Checkpoint saving in Python-compatible format
5. Hot model reload without MCP restart
6. Inference produces correct SharpOps output

With just 7 training examples, the model achieves 100% accuracy on the training set. Real-world generalization requires more diverse data, but the infrastructure is proven.

### Working Demo

The demo program (`SharpOps.Examples/Program.cs`) exercises every method:

```
Board has 5 tasks

Pending tasks (by priority):
  [Critical] #1 Fix login bug
  [High    ] #3 Add unit tests
  [High    ] #5 Review PR #42
  [Medium  ] #4 Deploy to staging
  [Low     ] #2 Update README

Completed tasks #1 and #3
Completion rate: 40%

High priority tasks: 2
  #3 Add unit tests (completed: True)
  #5 Review PR #42 (completed: False)

Removed #2, board now has 4 tasks
Found task #4: Deploy to staging
Task #999: not found
```

## File Locations

| What | Where |
|------|-------|
| Active model checkpoint | `.roslyn-mcp/Models/checkpoint.pt` |
| Tokenizer | `.roslyn-mcp/Models/tokenizer/tokenizer.json` |
| Pending training data | `.roslyn-mcp/Models/finetune/dataset/` |
| Archived training data | `.roslyn-mcp/Models/finetune/archive/{timestamp}/` |
| Training logs | `.roslyn-mcp/Models/finetune/training_{timestamp}.log` |

## FAQ

**Q: Does this send my code anywhere?**
No. SharpTinyCoder runs 100% locally. The model file is on disk, inference runs on your CPU/GPU, training data stays on your machine. Nothing leaves your environment.

**Q: Can the model break my code?**
No. Claude Code always reviews the model's output before accepting it. If the model generates nonsense, Claude Code replaces it with correct code. The model is a helper, not the decision maker.

**Q: How much disk space does training data use?**
Very little. Each JSONL record is a few KB. Even hundreds of training examples stay under 1 MB.

**Q: What if I want to reset the model?**
Copy the original `checkpoint.pt` from the Python training pipeline back to `.roslyn-mcp/Models/checkpoint.pt`. The model returns to its base state.

**Q: Can I fine-tune manually?**
The SharpOps.Torch project includes a CLI. Run `dotnet run --project SharpOps.Torch -- finetune --data <path> --rank 8 --target-modules qv --epochs 200 --lr 1e-4` for manual control.
