from sklearn.neighbors import NearestNeighbors, KDTree
import numpy as np
import argparse
import logging
from pathlib import Path
import datatypes as dt
import matplotlib.pyplot as plt
from datetime import datetime, UTC

from utils import get_now_string

PROGRAM_NAME = "ritgard." + Path(__file__).stem

log = logging.getLogger(PROGRAM_NAME)


def get_nearest_neighbor_distances(
        positions: np.ndarray[tuple[int, int], np.dtype[np.float32]],
):
    neighbors = NearestNeighbors(n_neighbors=2).fit(positions)
    distances, indices = neighbors.kneighbors(positions, n_neighbors=2)
    return distances[0:, 1], indices[0:, 1]


def adjust_positions(points, radii,
                     max_iterations=500, step_size=0.1, tolerance=1e-3):
    points = points.copy().astype(float)
    point_count = len(points)
    max_r = np.max(radii)
    max_overlap = 0.0

    for it in range(max_iterations):
        tree = KDTree(points)

        displacements = np.zeros_like(points)
        max_overlap = 0.0

        for point_index in range(point_count):
            pi = points[point_index]
            ri = radii[point_index]
            search_r = ri + max_r

            # Query neighbors within search_r
            neighbor_indices = tree.query_radius(np.reshape(pi, (1, -1)), r=search_r)

            for neighbor_index in neighbor_indices[0]:
                if neighbor_index <= point_index:
                    continue
                pn = points[neighbor_index]
                rn = radii[neighbor_index]
                delta = pn - pi
                dist = np.linalg.norm(delta)
                min_dist = ri + rn

                if min_dist > dist > 1e-8:
                    overlap = min_dist - dist
                    max_overlap = max(max_overlap, overlap)

                    direction = delta / dist

                    move = 0.5 * overlap * direction
                    displacements[point_index] -= move
                    displacements[neighbor_index] += move

        points += step_size * displacements

        if max_overlap < tolerance:
            log.info(f"Overlap adjustments done in {it} iterations; max_overlap {max_overlap:.6f}")
            break
    else:
        log.info(
            f"Overlap adjustments reached {max_iterations} iterations, the maximum; final overlap {max_overlap:.6f}")

    return points


def plot_nearest_neighbor_distances(distances):
    plt.boxplot(distances)
    plt.show()


def plot_adjusted_positions(scaled, adjusted, radius):
    fig, ax = plt.subplots()
    fig.set_size_inches(12, 12)
    ax.set_aspect('equal')
    ax.scatter(scaled[:, 0], scaled[:, 1], c='red', s=20, label='original')
    for p in adjusted:
        circle = plt.Circle(p, radius, color='blue', fill=False, alpha=0.7)
        ax.add_patch(circle)
    plt.legend()
    plt.show()


def main():
    logging.basicConfig(level=logging.INFO)
    args_parser = argparse.ArgumentParser(PROGRAM_NAME)
    args_parser.add_argument("input_path")
    args_parser.add_argument("--quantile", default=0.5, type=float)
    args_parser.add_argument("--radius", default=5, type=float)
    args_parser.add_argument("--max-iterations", default=-1, type=int)
    args_parser.add_argument("--output", default=None)
    args = args_parser.parse_args()
    input_path = Path(args.input_path)
    if not input_path.exists():
        raise RuntimeError(f"Input file '{args.input_path}' does not exist")

    log.info(f"Reading input file '{input_path.name}'")
    model = dt.TopicModellingResult.model_validate_json(input_path.read_bytes())
    items = sorted(model.items.values(), key=lambda i: i.id)
    positions = np.array([[item.x, item.y] for item in items])
    nn_distances, nn_indices = get_nearest_neighbor_distances(positions)
    base_distance = np.quantile(nn_distances, args.quantile)
    scale = args.radius / base_distance
    log.info(f"Base distance: {base_distance}")
    log.info(f"Scale: {scale}")
    scaled_positions = positions * scale
    max_iterations: int = args.max_iterations
    if max_iterations == -1:
        max_iterations = len(model.items)
        log.info(f"Automatically set max iterations to {max_iterations}")
    adjusted_positions = adjust_positions(
        scaled_positions,
        np.repeat(args.radius, scaled_positions.size),
        step_size=args.radius / 10,
        max_iterations=max_iterations
    )
    # plot_nearest_neighbor_distances(nn_distances)
    # plot_adjusted_positions(scaled_positions, adjusted_positions, args.radius)

    for new_pos, item in zip(adjusted_positions, items):
        item.x = new_pos[0]
        item.y = new_pos[1]
    model.items = {item.id: item for item in items}

    output_path = args.output
    if args.output is None:
        output_path = f"./out/adjusted_{get_now_string()}.json"
        log.info(f"Automatically set output path to '{output_path}'")
    output = Path(output_path)
    if output.parent is not None:
        output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf8") as output_file:
        output_file.write(model.model_dump_json())


if __name__ == '__main__':
    main()
