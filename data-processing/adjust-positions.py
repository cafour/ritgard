from sklearn.neighbors import NearestNeighbors
import numpy as np
import argparse
import logging
from pathlib import Path
import datatypes as dt
import matplotlib.pyplot as plt

PROGRAM_NAME = "ritgard." + Path(__file__).stem

log = logging.getLogger(PROGRAM_NAME)

def get_nearest_neighbor_distances(
        positions: np.ndarray[tuple[int, int], np.dtype[np.float32]],
):
    neighbors = NearestNeighbors(n_neighbors=2).fit(positions)
    distances, indices = neighbors.kneighbors(positions, n_neighbors=2)
    return distances[0:, 1], indices[0:, 1]

def main():
    logging.basicConfig(level=logging.INFO)
    args_parser = argparse.ArgumentParser(PROGRAM_NAME)
    args_parser.add_argument("input_path")
    args_parser.add_argument("--quantile", default=0.5)
    args_parser.add_argument("--radius", default=5)
    args = args_parser.parse_args()
    input_path = Path(args.input_path)
    if not input_path.exists():
        raise RuntimeError(f"Input file '{args.input_path}' does not exist")

    log.info(f"Reading input file '{input_path.name}'")
    model = dt.TopicModellingResult.model_validate_json(input_path.read_bytes())
    positions = np.array([[item.x, item.y] for item in sorted(model.items.values(), key=lambda i: i.id)])
    nn_distances, nn_indices = get_nearest_neighbor_distances(positions)
    base_distance = np.quantile(nn_distances, args.quantile)
    scale = args.radius / base_distance
    log.info(f"Base distance: {base_distance}")
    log.info(f"Scale: {scale}")
    plt.boxplot(nn_distances)
    plt.show()

if __name__ == '__main__':
    main()