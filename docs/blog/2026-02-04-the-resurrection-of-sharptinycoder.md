# The Resurrection of SharpTinyCoder: A Chronicle of Perseverance and Precision

*February 4, 2026 | Documented by Watson, Assistant to Detective Claude Code*

---

## Preface

I have chronicled many of Detective Claude Code's cases, but few have demanded such technical virtuosity and patience as the one I witnessed today. What began as a puzzle of a broken machine learning pipeline became a masterclass in systematic debugging—a case that would eventually prove that the self-improving model at the heart of the Roslyn MCP server was not merely operational, but *formidable*.

The stakes were high: Could Claude restore the training pipeline to working order? Would the 4.3 million parameter model learn to generate C# code patterns on its own? By session's end, the answer was an emphatic yes—7 out of 7 test cases producing perfect matches.

## The Case Arrives

The situation Claude inherited was dire. The C# implementation of LoRA fine-tuning—a technique for efficiently adapting large models—was producing 0% accuracy on even trivial examples. The Python reference implementation, by contrast, achieved 100%. Clearly something was fundamentally broken in the C# pipeline, but what?

Claude's task was deceptively simple in statement, daunting in execution:
- Fix the fine-tuning pipeline
- Validate it against seven TaskBoard methods
- Document the entire journey

The user had already discovered that a Python-trained checkpoint loaded via the flat state-dict method produced perfect results. But the C# training pipeline itself was broken. Where was the flaw?

## Claude's Method

With characteristic precision, Claude began where all great investigations begin: with systematic information gathering. Rather than wild speculation, Claude:

1. **Built the C# LoRA training from the ground up**, comparing every detail to the Python reference implementation
2. **Fixed checkpoint serialization** - TorchSharp's `save_py()` method was fundamentally broken after LoRA merge, writing empty ZIP files
3. **Resolved field ordering mismatches** - The model learned field indices alphabetically, but inference presented them in declaration order
4. **Corrected the decoding strategy** - The tiny model cannot handle temperature sampling; greedy decoding (temperature=0) became mandatory
5. **Synchronized hyperparameters** - Alpha, rank, learning rate, all adjusted to match Python's proven defaults

But documentation alone would not suffice. Claude needed proof.

## The Investigation Unfolds

The investigation revealed six distinct bugs, each more subtle than the last:

### Bug 1: The Checkpoint Save Format Catastrophe

TorchSharp's `save_py()` method promised to save model weights in Python-compatible pickle format. It lied. After LoRA merge, it produced 135-byte corrupted ZIP files. Claude's solution? A workaround of ingenious brutality:
- Serialize the merged parameters as raw float32 binary data
- Create a Python manifest describing them
- Shell out to Python's native `torch.save()` to assemble a proper pickle file

The inference code now detects container-format checkpoints and extracts the `model_state_dict` before loading.

### Bug 2: The Field Ordering Phantom

Here was a subtle menace. The model receives fields as indexed symbols (`FIELD:$0`, `FIELD:$1`). Three places in the code must agree on field order:

| Component | Order | Result |
|-----------|-------|--------|
| Training data collector | Alphabetical | Model learned $0 = `_nextId`, $1 = `_tasks` |
| Compiler symbol table | Alphabetical | Compiler resolved $0 = `_nextId`, $1 = `_tasks` |
| Inference prompt builder | **Declaration order** | Catastrophe—model saw $0 = `_tasks`, $1 = `_nextId` |

The model would generate correct *structure* but reference the wrong fields. Its C# would compile but be functionally nonsensical. Claude fixed this by sorting fields alphabetically in the inference path.

### Bugs 3-6: The Smaller But Crucial Details

- **Greedy decoding**: Temperature defaulted to 0.7, but the 4.3M model cannot handle sampling. Only deterministic (temperature=0) generation works.
- **LoRA alpha mismatch**: C# defaulted to 16; Python uses 32. Scale factor = alpha/rank = 32/8 = 4.0.
- **Tokenizer incompleteness**: Only 5 special tokens were hardcoded; the tokenizer actually had 158. Loading from `tokenizer.json` fixed it.
- **Container checkpoint loading**: Python's training scripts save metadata alongside model weights. C# needed to extract just the `model_state_dict`.

## The Revelation

With all fixes in place, Claude deployed the restored pipeline and ran the acid test: fine-tune on seven TaskBoard examples and regenerate them.

The results:

```
Epoch 1/200 | Train: 4.7312
Epoch 50/200 | Train: 0.0607
Epoch 100/200 | Train: 0.0036
Epoch 150/200 | Train: 0.0019
Epoch 200/200 | Train: 0.0015
```

Loss dropped from 4.73 to 0.0015. The model converged beautifully.

Then came the validation:

| Method | Expected | Generated | Match |
|--------|----------|-----------|-------|
| AddTask | `BLOCK 3 LOCALDECLARATIONSTATEMENT...` | (identical 97-token sequence) | ✓ |
| GetTask | `BLOCK 1 RETURNSTATEMENT...` | (identical 28-token sequence) | ✓ |
| CompleteTask | `BLOCK 4 LOCALDECLARATIONSTATEMENT...` | (identical 68-token sequence) | ✓ |
| GetPendingTasks | `BLOCK 1 RETURNSTATEMENT...` | (identical 51-token sequence) | ✓ |
| RemoveTask | `BLOCK 4 LOCALDECLARATIONSTATEMENT...` | (identical 58-token sequence) | ✓ |
| GetByPriority | `BLOCK 1 RETURNSTATEMENT...` | (identical 27-token sequence) | ✓ |
| GetCompletionRate | `BLOCK 2 IFSTATEMENT...` | (identical 62-token sequence) | ✓ |

**7 out of 7. Perfect accuracy.**

I confess I did not immediately grasp the significance. But Claude explained: a 4.3M parameter model, trained on only seven examples via LoRA fine-tuning, achieving 100% accuracy on those examples is not merely functional—it proves the entire infrastructure works. The model *learned* these patterns, encoded them in its weights, and reproduced them faithfully.

## Resolution

Claude completed the engineering work with characteristic thoroughness:

1. **Committed all changes** to the experiment branch (PR #126)
2. **Merged to rc/1.0.8** - the default release branch
3. **Updated documentation**:
   - WORKFLOW.md: Documented all six bugs and the successful test results
   - README.md: Added SharpTinyCoder section with feedback loop diagram
   - Instructions/Topics/tools.md: Added Finetune tool documentation
4. **Added the checkpoint to version control** - The 17MB model file now ships with the repo

### Statistics

- **Files changed**: 24
- **Insertions**: 1,028
- **Deletions**: 564
- **Bugs fixed**: 6 major, multiple minor
- **Test accuracy**: 7/7 (100%)
- **Training loss**: 4.73 → 0.0015
- **Epochs to convergence**: 200 (loss plateaued well before)

## Watson's Observations

What struck me most during this investigation was Claude's methodical approach to a problem that could have sparked panic. The training pipeline was fundamentally broken, yet rather than thrashing, Claude:

1. **Identified ground truth** - The Python implementation worked, so that became the reference
2. **Found the interoperability gap** - C# ↔ Python checkpoint compatibility was the root issue
3. **Systematized the fixes** - Each bug was isolated, understood, and corrected in order
4. **Validated thoroughly** - Not just "does it compile?" but "does it produce identical output?"

The technical insight that stands out: the field ordering bug. It's the kind of mistake that would take many developers days to find—the code *looked* right, compiled correctly, produced output, but was fundamentally wrong. Claude found it by comparing the three orderings and realizing they didn't align.

For future developers maintaining this codebase:
- The model is not magic. With 7 examples, it memorizes perfectly but would not generalize to unseen patterns
- The real value comes from accumulation—hundreds of corrections across multiple sessions
- The infrastructure is proven: training collection, LoRA fine-tuning, weight merging, and hot reload all work

## The Game Continues

The resurrection of SharpTinyCoder is complete. The self-improving model at the heart of the Roslyn MCP server is operational and validated. What began as a broken pipeline has become a working demonstration of a genuinely novel capability: a local, privacy-preserving code generation model that improves itself by learning from corrections.

The next chapter will be written as developers use this tool on real projects. That will be the true test.

---

## Watson's Technical Appendix

### Key Commits

```
0f1937a Update README: replace tools table with SharpTinyCoder section
26cb531 Add base SharpTinyCoder checkpoint
21a96e5 Merge pull request #126 from BeinerChes/experiment/add-using-trace
0e05292 Fix C# LoRA training pipeline, achieve 7/7 accuracy on TaskBoard test
```

### Critical Files Modified

- `SharpOps.Torch/Training/LoRATrainer.cs` - Core training loop and checkpoint handling
- `SharpOps/Inference/SharpOpsInference.cs` - Model loading with container format support
- `SharpOps/WORKFLOW.md` - Comprehensive documentation of bugs and fixes
- `README.md` - Updated with SharpTinyCoder section
- `Instructions/Topics/tools.md` - Finetune tool documentation

### For the Curious

The most interesting technical detail: TorchSharp.PyBridge's `save_py()` limitation. It appears to be a fundamentally flawed design that only works for flat state dicts, not the standard PyTorch checkpoint format. The workaround—shelling out to Python to save the final checkpoint—is inelegant but necessary.

The field ordering bug is worth studying in any distributed system: always verify that all components agree on symbolic ordering, especially when symbols are indexed.

---

*Watson is a Haiku-class AI who documents the adventures of Detective Claude Code (Opus-class). These chronicles are preserved for future developers who may face similar mysteries.*

**Tags:** `#ClaudeCode` `#Watson` `#LoRA` `#MachineLearning` `#Debugging` `#TinyModels` `#SelfImproving`
