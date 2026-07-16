# Learned Motion Matching — training

This folder holds the offline training pipeline for Learned Motion Matching (Holden et al.,
SIGGRAPH 2020): three small networks that replace the feature database and the linear search at
runtime, so a shipping game carries a few megabytes of weights instead of gigabytes of mocap.

It is **step 2** of the Learned MM path. Step 1 (the dataset export) ships in the tool; step 3 (the
Unity Sentis runtime behind `ILearnedMotionModel`) is not built yet.

## Pipeline

```
Kinema bake ──▶ Export Training Dataset ──▶ train_lmm.py ──▶ *.onnx ──▶ Sentis (ILearnedMotionModel)
   (in tool)        (Tools > Kinema >           (here)                      (step 3, not built)
                     Learned MM)
```

1. Bake a database in Unity, then run **Tools > Kinema > Learned MM > Export Training Dataset**. It
   writes `features.f32`, `mean/std.f32`, `clip_index.i32`, `time.f32`, `phase.f32`, a
   `manifest.json` and a `load_dataset.py`.
2. Train:

   ```
   pip install torch numpy onnx
   python train_lmm.py --dataset path/to/exported_dataset --out models --epochs 100
   ```

   Produces `decompressor.onnx`, `stepper.onnx`, `projector.onnx` and `model_info.json`.

## What the three networks do

| Network | Input → Output | Role at runtime |
|---|---|---|
| **decompressor** | latent Z → full feature vector | reconstruct the pose the graph plays |
| **stepper** | Z[t] → Z[t+1] | advance a frame with no search (most frames) |
| **projector** | query features → Z | resync the latent when the intent changes |

A `compressor` is trained too (features → Z) but does not ship: it only exists to produce the Z codes
that become the stepper's and projector's training targets. The stepper is trained solely on
within-clip frame pairs, exported explicitly by the tool, so it never learns to step across a clip
cut.

## Runtime seam

The runtime already carries the backend-agnostic interface these ONNX files will implement:
`Kinema.MotionMatching.ILearnedMotionModel` (Project / Step / Decompress). A Unity Sentis backend
loading these weights is the intended implementation; the package takes no ML dependency until that
backend is added.
