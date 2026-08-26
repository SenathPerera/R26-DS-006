"""Generate the four paper figures directly from the measured ledger numbers.
Every value here is copied from the executed notebook outputs (component_d_paper.ipynb).
Four DISTINCT chart types: scatter, heatmap, dumbbell, diverging bars.
Outputs vector PDF (for LaTeX) + PNG preview. No fabricated data.
"""
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import MultipleLocator
from matplotlib.colors import LinearSegmentedColormap
import numpy as np
from pathlib import Path

OUT = Path(__file__).parent / "figures"
OUT.mkdir(exist_ok=True)

plt.rcParams.update({
    "font.size": 8.5, "font.family": "serif", "mathtext.fontset": "cm",
    "axes.linewidth": 0.7, "axes.edgecolor": "#444444",
    "axes.axisbelow": True,
    "grid.color": "#cccccc", "grid.linewidth": 0.4,
    "xtick.major.size": 2.5, "ytick.major.size": 2.5,
    "figure.dpi": 300, "savefig.bbox": "tight", "savefig.pad_inches": 0.03,
    "legend.frameon": False, "legend.handlelength": 1.2, "legend.handletextpad": 0.5,
})

VAL  = "#2a7d4f"   # valence / good
ARO  = "#c23b22"   # arousal / problem
NEU  = "#9aa0a6"   # legacy / neutral
SHIP = "#1b6ec2"   # shipped-model highlight
INK  = "#222222"


def save(fig, name):
    fig.savefig(OUT / f"{name}.pdf")
    fig.savefig(OUT / f"{name}.png", dpi=300)
    plt.close(fig)
    print("saved", name)


# ====================================================== 1. INVERSION (scatter)
acted_ccc = [0.81, 0.79, 0.77, 0.65, 0.35]
real_acc  = [0.375, 0.750, 0.625, 0.583, 0.917]
short     = ["acted", "v2", "combined", "iemocap", "meld_base"]
is_ship   = [False, False, False, False, True]

fig, ax = plt.subplots(figsize=(3.4, 2.55))
ax.grid(True)
ax.annotate("", xy=(0.36, 0.905), xytext=(0.80, 0.40),
            arrowprops=dict(arrowstyle="-", color="#e0e0e0", lw=6, alpha=0.8))
ax.plot(acted_ccc, real_acc, "-", color="#b0b0b0", lw=1.0, zorder=2)
for x, y, s, sh in zip(acted_ccc, real_acc, short, is_ship):
    ax.scatter(x, y, s=80 if sh else 44, color=SHIP if sh else ARO,
               zorder=4, edgecolor="k", linewidth=0.6)
    off = (0, 9) if s != "meld_base" else (7, -12)
    ax.annotate(s, (x, y), textcoords="offset points", xytext=off, ha="center",
                fontsize=6.8, color=SHIP if sh else INK,
                fontweight="bold" if sh else "normal")
ax.set_xlabel("In-domain acted arousal CCC\n(higher = better by convention)")
ax.set_ylabel("Real-voice accuracy")
ax.set_xlim(0.28, 0.90); ax.set_ylim(0.30, 1.00)
ax.yaxis.set_major_locator(MultipleLocator(0.1))
ax.text(0.72, 0.86, r"$\rho=-0.60$", fontsize=8.5, style="italic",
        bbox=dict(boxstyle="round,pad=0.25", fc="white", ec="#cccccc", lw=0.5))
save(fig, "fig_inversion")


# ====================================================== 2. DISSOCIATION (heatmap)
ck6     = ["acted", "augmented", "combined", "iemocap", "meld_base", "v2"]
val_neg = [82.4, 94.1, 100.0, 100.0, 100.0, 94.1]
aro_pos = [11.8, 29.4, 29.4, 52.9, 17.6, 29.4]
M = np.array([val_neg, aro_pos])          # 2 rows x 6 cols

green_cmap = LinearSegmentedColormap.from_list(
    "vg", ["#f7fcf5", "#c7e9c0", "#74c476", "#238b45", "#00441b"])

fig, ax = plt.subplots(figsize=(3.6, 1.95))
im = ax.imshow(M, cmap=green_cmap, vmin=0, vmax=100, aspect="auto")
ax.set_xticks(range(6)); ax.set_xticklabels(ck6, rotation=30, ha="right", fontsize=7)
ax.set_yticks([0, 1])
ax.set_yticklabels(["valence\ncorrect (neg)", "arousal\ncorrect (high)"], fontsize=7.5)
for i in range(2):
    for j in range(6):
        v = M[i, j]
        ax.text(j, i, f"{v:.0f}", ha="center", va="center", fontsize=7.5,
                color="white" if v >= 55 else "#333333",
                fontweight="bold")
ax.set_xticks(np.arange(-.5, 6, 1), minor=True)
ax.set_yticks(np.arange(-.5, 2, 1), minor=True)
ax.grid(which="minor", color="white", linewidth=1.2)
ax.tick_params(which="minor", length=0)
cb = fig.colorbar(im, ax=ax, fraction=0.046, pad=0.03)
cb.set_label("% correct", fontsize=7)
cb.ax.tick_params(labelsize=6.5)
save(fig, "fig_dissociation")


# ====================================================== 3. THE FIX (dumbbell)
ck       = ["acted", "augmented", "combined", "iemocap", "meld_base", "v2"]
d_legacy = [1.065, 2.004, 1.018, 1.107, 1.941, 2.004]
d_valp   = [1.520, 2.278, 1.370, 1.735, 2.979, 2.278]
y = np.arange(len(ck))[::-1]              # top-to-bottom order

fig, ax = plt.subplots(figsize=(3.55, 2.55))
ax.grid(axis="x")
for yi, a, b in zip(y, d_legacy, d_valp):
    ax.plot([a, b], [yi, yi], color="#c9c9c9", lw=2.2, zorder=1,
            solid_capstyle="round")
ax.scatter(d_legacy, y, s=46, color=NEU, edgecolor="k", linewidth=0.5,
           zorder=3, label="legacy (arousal-based)")
ax.scatter(d_valp, y, s=52, color=VAL, edgecolor="k", linewidth=0.5,
           zorder=3, label="valence-primary")
ax.set_yticks(y); ax.set_yticklabels(ck, fontsize=7.5)
ax.set_xlabel(r"$d'$  (stressed vs. calm separation)")
ax.set_xlim(0.7, 3.4)
ax.legend(fontsize=6.6, loc="lower left")
ax.text(0.98, 0.96, r"Cohen's $d=1.71$", transform=ax.transAxes,
        ha="right", va="top", fontsize=8,
        bbox=dict(boxstyle="round,pad=0.25", fc="white", ec="#cccccc", lw=0.5))
save(fig, "fig_dprime")


# ====================================================== 4. ENCODER (diverging)
enc      = ["emotion2vec\n(1024)", "WavLM-base+\n(768)", "WavLM-large\n(1024)"]
val_ccc  = [0.660, 0.344, 0.387]
aro_ccc  = [0.570, 0.604, 0.580]
diff     = [v - a for v, a in zip(val_ccc, aro_ccc)]   # + favours valence
y = np.arange(len(enc))[::-1]

fig, ax = plt.subplots(figsize=(3.5, 2.35))
ax.grid(axis="x")
ax.axvline(0, color="#555555", lw=0.8)
colors = [VAL if d > 0 else ARO for d in diff]
bars = ax.barh(y, diff, color=colors, edgecolor="white", linewidth=0.5, height=0.55)
for yi, d, v, a in zip(y, diff, val_ccc, aro_ccc):
    xt = d + (0.012 if d > 0 else -0.012)
    ax.annotate(f"{d:+.2f}", (xt, yi), ha="left" if d > 0 else "right",
                va="center", fontsize=7, fontweight="bold",
                color=VAL if d > 0 else ARO)
    ax.annotate(f"(v {v:.2f} / a {a:.2f})", (0, yi + 0.34), ha="center",
                va="bottom", fontsize=5.6, color="#666666")
ax.set_yticks(y); ax.set_yticklabels(enc, fontsize=7)
ax.set_xlabel(r"$\leftarrow$ favours arousal   $\mid$   favours valence $\rightarrow$"
              "\n" r"(valence CCC $-$ arousal CCC)")
ax.set_xlim(-0.34, 0.20)
save(fig, "fig_encoder")

print("all figures written to", OUT)
