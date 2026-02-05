# Finetune

## Description

Finetune runs LoRA fine-tuning on the SharpTinyCoder model using training data collected from `UpdateMethod` and `AddMember` corrections. It runs in the background — returning immediately with a log file path — and automatically reloads the model when training completes. No configuration is needed; sensible defaults match the Python training pipeline.

The tool is idempotent for status checks: calling `Finetune()` while training is running returns the current status, and calling it after completion returns the results.

For a full explanation of the self-improving model workflow, see [SharpOps/WORKFLOW.md](../../SharpOps/WORKFLOW.md).

## How Training Data Is Collected

Training data accumulates automatically during normal development:

| Action | Collects Training Data? |
|--------|------------------------|
| `AddMember(auto: true)` — model output accepted | No (don't train on model's own output) |
| `AddMember(auto: true)` — then `UpdateMethod` correction | **Yes** (the correction) |
| `AddMember(auto: false)` — Claude writes code | **Yes** |
| `UpdateMethod` with new code | **Yes** |
| Read-only tools (GetMethodBody, FindSymbol, etc.) | No |
| Fields, properties, events | No |

Training examples are stored as JSONL in `.roslyn-mcp/Models/finetune/dataset/` (one file per solution).

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `dataPath` | string | No | Path to JSONL file or folder. Default: `.roslyn-mcp/Models/finetune/dataset/` |

## Default Training Configuration

| Setting | Value | Notes |
|---------|-------|-------|
| LoRA rank | 8 | Good balance for a 4.3M parameter model |
| LoRA alpha | 32 | Scale factor = alpha/rank = 4.0 |
| Target modules | query/value attention | Most efficient layers to adapt |
| Max epochs | 200 | Runs all epochs when no validation split |
| Learning rate | 1e-4 | Standard for LoRA |
| Batch size | 1 | Small model, small dataset |
| Max sequence length | 512 | Fits in GPU memory |
| Validation split | 0.0 | No validation by default |
| Weight decay | 0.0 | Default |

## Lifecycle

### 1. Start training

```
Finetune()
```

**Response:**
```json
{
  "status": "started",
  "message": "Fine-tuning started in background. Call Finetune again to check status.",
  "logFile": ".roslyn-mcp/Models/finetune/training_20260205_143000.log",
  "config": {
    "dataPath": ".roslyn-mcp/Models/finetune/dataset/",
    "Rank": 8,
    "Alpha": 32,
    "TargetModules": "qv",
    "Epochs": 200,
    "LearningRate": 0.0001,
    "Patience": 20,
    "earlyStopMinDelta": 0.001
  }
}
```

### 2. Check status (while running)

```
Finetune()
```

**Response:**
```json
{
  "status": "running",
  "message": "Training is already in progress.",
  "logFile": ".roslyn-mcp/Models/finetune/training_20260205_143000.log"
}
```

### 3. Get results (after completion)

```
Finetune()
```

**Response:**
```json
{
  "status": "completed",
  "message": "Fine-tuning completed. Model has been reloaded.",
  "bestLoss": -1,
  "epochsRun": 200,
  "earlyStopped": false,
  "outputPath": ".roslyn-mcp/Models/checkpoint.pt",
  "logFile": ".roslyn-mcp/Models/finetune/training_20260205_143000.log"
}
```

Note: `bestLoss: -1` means no validation split was used (JSON doesn't support infinity). The training log file contains per-epoch train loss.

### Error cases

**No training data:**
```json
{
  "error": "Training failed: no training examples found"
}
```

**Data path not found:**
```json
{
  "error": "Data path not found: /some/path"
}
```

**Checkpoint missing:**
```json
{
  "error": "Checkpoint not found: .roslyn-mcp/Models/checkpoint.pt"
}
```

## What Happens During Training

1. **LoRA layers** are added to the model's query and value attention projections
2. **Training** runs for up to 200 epochs on the collected JSONL data
3. **LoRA weights are merged** back into the base model parameters
4. **Checkpoint is saved** to `.roslyn-mcp/Models/checkpoint.pt` (overwrites the active model)
5. **Model is hot-reloaded** — inference immediately uses the new weights, no MCP restart needed
6. **Dataset is archived** to `.roslyn-mcp/Models/finetune/archive/{timestamp}/` so the next training run only uses fresh examples

## File Locations

| What | Where |
|------|-------|
| Active model checkpoint | `.roslyn-mcp/Models/checkpoint.pt` |
| Tokenizer | `.roslyn-mcp/Models/tokenizer/tokenizer.json` |
| Pending training data | `.roslyn-mcp/Models/finetune/dataset/` |
| Archived training data | `.roslyn-mcp/Models/finetune/archive/{timestamp}/` |
| Training logs | `.roslyn-mcp/Models/finetune/training_{timestamp}.log` |

## Common Workflows

### 1. Fine-tune after a coding session

After Claude Code has corrected several model outputs:
```
Finetune()
→ "started", keep working

Finetune()
→ "running" or "completed"
```

### 2. Custom training data

Point at a specific JSONL file:
```
Finetune(dataPath: "D:/my-data/custom-examples.jsonl")
```

### 3. Monitor training progress

Read the log file while training runs:
```
Read(file_path: <logFile path from response>)
```

Typical output:
```
Epoch   1/200 | Train: 4.7312
Epoch  50/200 | Train: 0.0607
Epoch 100/200 | Train: 0.0036
Epoch 200/200 | Train: 0.0015

Merging LoRA weights...
Saving merged model to .roslyn-mcp/Models/checkpoint.pt...
[Finetune] Model reloaded for inference.
[Finetune] Dataset archived (1 file)
```

### 4. Reset model to base weights

If fine-tuning produces worse results, restore the original checkpoint:
```bash
copy D:\RMS\CSharpRobot\checkpoints_sharp\checkpoint.pt .roslyn-mcp\Models\checkpoint.pt
```

Then call `Finetune()` again with fresh training data.
