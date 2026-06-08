from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_pascal
from pathlib import Path
import datatypes as dt
import numpy as np
import numpy.typing as npt
import argparse
import matplotlib.pyplot as plt
import importlib
ap = importlib.import_module("adjust-positions")

from utils import get_now_string

PROGRAM_NAME = "ritgard." + Path(__file__).stem

def plot_adjusted_positions(original, adjusted, radius, bbox, output_name):
    fig, ax = plt.subplots()
    fig.set_size_inches(12, 12)
    ax.set_aspect('equal')
    if original is not None:
        ax.scatter(original[:, 0], original[:, 1], c='red', s=20)
    if adjusted is not None:
        for p in adjusted:
            circle = plt.Circle(p, radius, color='blue', fill=False, alpha=0.7)
            ax.add_patch(circle)
    plt.plot(
        [bbox[0, 0], bbox[1, 0], bbox[1, 0], bbox[0, 0], bbox[0, 0]],
        [bbox[0, 1], bbox[0, 1], bbox[1, 1], bbox[1, 1], bbox[0, 1]],
        color="blue", linewidth=2
    )
    plt.subplots_adjust(left=0.03, right=0.97, top=0.97, bottom=0.07)
    plt.savefig(output_name)

def get_bbox(positions: npt.NDArray[np.float32], padding: float = 0):
    min_x, max_x, min_y, max_y = np.min(positions[:, 0]), np.max(positions[:, 0]), np.min(
        positions[:, 1]
    ), np.max(
        positions[:, 1]
    )
    return np.array([[min_x - padding, min_y - padding], [max_x + padding, max_y + padding]])

class PlotPositionsOptions(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)
    original: str
    adjusted: str

def main():
    args_parser = argparse.ArgumentParser(PROGRAM_NAME)
    args_parser.add_argument("original")
    args_parser.add_argument("adjusted")
    options = PlotPositionsOptions(**vars(args_parser.parse_args()))
    original_path = Path(options.original)
    adjusted_path = Path(options.adjusted)
    original_model = dt.TopicModellingResult.model_validate_json(original_path.read_bytes())
    adjusted_model = dt.TopicModellingResult.model_validate_json(adjusted_path.read_bytes())
    original_positions = np.array([[i.x, i.y] for i in sorted(original_model.items.values(), key=lambda i: i.id)])
    adjusted_positions = np.array([[i.x, i.y] for i in sorted(adjusted_model.items.values(), key=lambda i: i.id)])
    now_string = get_now_string()
    original_bbox = get_bbox(original_positions,padding=0)
    adjusted_bbox = get_bbox(adjusted_positions, padding=adjusted_model.position_adjustment_options.world_padding * adjusted_model.position_adjustment_options.cell_radius)

    original_output_path = f"./plot-positions_{original_model.name}_{now_string}_original.png"
    plot_adjusted_positions(
        original_positions,
        [],
        None,
        original_bbox,
        original_output_path
    )

    centered_positions = ap.center_positions(original_positions)
    rotated_positions = ap.minimize_world_bbox(centered_positions)
    rotated_bbox = get_bbox(rotated_positions, padding=0)
    rotated_output_path = f"./plot-positions_{original_model.name}_{now_string}_rotated.png"
    plot_adjusted_positions(
        rotated_positions,
        [],
        None,
        rotated_bbox,
        rotated_output_path
    )

    adjusted_output_path = f"./plot-positions_{original_model.name}_{now_string}_adjusted.png"
    plot_adjusted_positions(
        None,
        adjusted_positions,
        adjusted_model.position_adjustment_options.cell_radius,
        adjusted_bbox,
        adjusted_output_path
    )

if __name__ == '__main__':
    main()