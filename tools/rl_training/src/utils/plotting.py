from __future__ import annotations

from pathlib import Path
from typing import Dict

import matplotlib.pyplot as plt
import pandas as pd


def plot_training_curve(csv_path: str | Path, output_path: str | Path, title: str) -> None:
    frame = pd.read_csv(csv_path)
    plt.figure(figsize=(8, 4))
    plt.plot(frame["timesteps"], frame["mean_reward"], label="Mean Reward")
    plt.xlabel("Timesteps")
    plt.ylabel("Mean Reward")
    plt.title(title)
    plt.grid(True, alpha=0.25)
    plt.legend()
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    plt.tight_layout()
    plt.savefig(output_path, dpi=160)
    plt.close()


def plot_comparison_bar(metric_frame: pd.DataFrame, metric_name: str, output_path: str, title: str) -> None:
    plt.figure(figsize=(9, 4))
    plt.bar(metric_frame["algorithm"], metric_frame[metric_name], yerr=metric_frame.get(f"{metric_name}_std"))
    plt.ylabel(metric_name.replace("_", " ").title())
    plt.title(title)
    plt.grid(True, axis="y", alpha=0.20)
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    plt.tight_layout()
    plt.savefig(output_path, dpi=160)
    plt.close()
