"""
Learned Motion Matching - training pipeline (Holden et al., SIGGRAPH 2020).

Trains the three networks that replace the feature database and the linear search at runtime, from a
dataset exported by Kinema (Tools > Kinema > Learned MM > Export Training Dataset):

  compressor + decompressor : an autoencoder. The compressor turns each frame's features into a
                              small latent code Z; the decompressor reconstructs the features from Z.
                              Only the decompressor ships - the Z codes are precomputed for the whole
                              database and become the stepper/projector's training targets.
  stepper                   : predicts Z[t+1] from Z[t], so at runtime the latent advances a frame
                              without a search (trained only on within-clip pairs - never across a cut).
  projector                 : maps a query feature vector back to the nearest Z, for when the intent
                              changes and the latent must resync.

Exports decompressor / stepper / projector to ONNX, which Unity Sentis runs behind the runtime's
`ILearnedMotionModel`. Requires: torch, numpy, onnx.

Run:
    python train_lmm.py --dataset path/to/exported_dataset --out path/to/models --epochs 100
"""

import argparse
import json
import os

import numpy as np
import torch
import torch.nn as nn


# ----------------------------------------------------------------------------- data

def load_dataset(folder):
    """Reads the flat binaries + manifest that Kinema's exporter wrote."""
    manifest = json.load(open(os.path.join(folder, "manifest.json")))

    def arr(key, dtype):
        spec = manifest["files"][key]
        data = np.fromfile(os.path.join(folder, spec["file"]), dtype=dtype)
        return data.reshape(spec["shape"])

    features = arr("features", np.float32)      # [frames, dim], already normalized
    clips = manifest["clips"]                    # [[start, count], ...]

    # Stepper pairs (t -> t+1) that never straddle a clip boundary.
    pairs = np.array(
        [(s + i, s + i + 1) for s, n in clips for i in range(n - 1)],
        dtype=np.int64,
    )
    return features, pairs, manifest


# ----------------------------------------------------------------------------- nets

class MLP(nn.Module):
    """A plain multilayer perceptron - the paper's networks are all small MLPs."""

    def __init__(self, sizes):
        super().__init__()
        layers = []
        for i in range(len(sizes) - 1):
            layers.append(nn.Linear(sizes[i], sizes[i + 1]))
            if i < len(sizes) - 2:
                layers.append(nn.ELU())
        self.net = nn.Sequential(*layers)

    def forward(self, x):
        return self.net(x)


def build(feature_dim, latent_dim, hidden):
    compressor = MLP([feature_dim, hidden, hidden, latent_dim])
    decompressor = MLP([latent_dim, hidden, hidden, feature_dim])
    stepper = MLP([latent_dim, hidden, latent_dim])
    projector = MLP([feature_dim, hidden, hidden, latent_dim])
    return compressor, decompressor, stepper, projector


# ----------------------------------------------------------------------------- train

def train(features, pairs, feature_dim, latent_dim, hidden, epochs, batch, lr, device):
    x = torch.from_numpy(features).to(device)
    compressor, decompressor, stepper, projector = (
        n.to(device) for n in build(feature_dim, latent_dim, hidden)
    )

    mse = nn.MSELoss()

    # Stage 1: autoencoder. Learn a latent that reconstructs the features.
    ae_opt = torch.optim.Adam(list(compressor.parameters()) + list(decompressor.parameters()), lr=lr)
    for epoch in range(epochs):
        perm = torch.randperm(x.shape[0], device=device)
        total = 0.0
        for i in range(0, x.shape[0], batch):
            idx = perm[i:i + batch]
            xb = x[idx]
            z = compressor(xb)
            recon = decompressor(z)
            loss = mse(recon, xb)
            ae_opt.zero_grad(); loss.backward(); ae_opt.step()
            total += loss.item() * xb.shape[0]
        if epoch % 10 == 0:
            print(f"[ae]   epoch {epoch:3d}  recon {total / x.shape[0]:.5f}")

    # Freeze the codes: Z for every frame becomes the training target below.
    with torch.no_grad():
        z_all = compressor(x)

    a = torch.from_numpy(pairs[:, 0]).to(device)
    b = torch.from_numpy(pairs[:, 1]).to(device)

    # Stage 2: stepper. Z[t] -> Z[t+1] on within-clip pairs.
    st_opt = torch.optim.Adam(stepper.parameters(), lr=lr)
    for epoch in range(epochs):
        perm = torch.randperm(a.shape[0], device=device)
        total = 0.0
        for i in range(0, a.shape[0], batch):
            idx = perm[i:i + batch]
            loss = mse(stepper(z_all[a[idx]]), z_all[b[idx]])
            st_opt.zero_grad(); loss.backward(); st_opt.step()
            total += loss.item() * idx.shape[0]
        if epoch % 10 == 0:
            print(f"[step] epoch {epoch:3d}  {total / a.shape[0]:.6f}")

    # Stage 3: projector. Features -> Z (so a query can resync to the latent).
    pr_opt = torch.optim.Adam(projector.parameters(), lr=lr)
    for epoch in range(epochs):
        perm = torch.randperm(x.shape[0], device=device)
        total = 0.0
        for i in range(0, x.shape[0], batch):
            idx = perm[i:i + batch]
            loss = mse(projector(x[idx]), z_all[idx])
            pr_opt.zero_grad(); loss.backward(); pr_opt.step()
            total += loss.item() * idx.shape[0]
        if epoch % 10 == 0:
            print(f"[proj] epoch {epoch:3d}  {total / x.shape[0]:.6f}")

    return decompressor, stepper, projector


# ----------------------------------------------------------------------------- export

def export_onnx(decompressor, stepper, projector, feature_dim, latent_dim, out):
    os.makedirs(out, exist_ok=True)
    z = torch.zeros(1, latent_dim)
    f = torch.zeros(1, feature_dim)
    common = dict(opset_version=11, input_names=["input"], output_names=["output"])
    torch.onnx.export(decompressor, z, os.path.join(out, "decompressor.onnx"), **common)
    torch.onnx.export(stepper, z, os.path.join(out, "stepper.onnx"), **common)
    torch.onnx.export(projector, f, os.path.join(out, "projector.onnx"), **common)
    json.dump(
        {"feature_size": feature_dim, "latent_size": latent_dim},
        open(os.path.join(out, "model_info.json"), "w"),
    )
    print(f"exported decompressor/stepper/projector.onnx -> {out}")


# ----------------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dataset", required=True, help="folder from Kinema's dataset export")
    ap.add_argument("--out", default="models")
    ap.add_argument("--latent", type=int, default=32)
    ap.add_argument("--hidden", type=int, default=256)
    ap.add_argument("--epochs", type=int, default=100)
    ap.add_argument("--batch", type=int, default=256)
    ap.add_argument("--lr", type=float, default=1e-3)
    args = ap.parse_args()

    features, pairs, manifest = load_dataset(args.dataset)
    feature_dim = manifest["dimension"]
    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"dataset: {features.shape[0]} frames x {feature_dim} dims, "
          f"{pairs.shape[0]} step pairs, latent {args.latent}, device {device}")

    decompressor, stepper, projector = train(
        features, pairs, feature_dim, args.latent, args.hidden,
        args.epochs, args.batch, args.lr, device,
    )
    export_onnx(decompressor.cpu(), stepper.cpu(), projector.cpu(), feature_dim, args.latent, args.out)


if __name__ == "__main__":
    main()
