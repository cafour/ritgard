from sklearn.neighbors import NearestNeighbors, KDTree
import numpy as np
import numpy.typing as npt
import argparse
import logging
from pathlib import Path
from scipy.spatial import ConvexHull
import datatypes as dt
import matplotlib.pyplot as plt
from utils import get_now_string

PROGRAM_NAME = "ritgard." + Path(__file__).stem

log = logging.getLogger(PROGRAM_NAME)


def get_nearest_neighbor_distances(
        positions: np.ndarray[tuple[int, int], np.dtype[np.float32]],
):
    neighbors = NearestNeighbors(n_neighbors=2).fit(positions)
    distances, indices = neighbors.kneighbors(positions, n_neighbors=2)
    return distances[0:, 1], indices[0:, 1]


def adjust_positions(
        points,
        radii,
        bbox=None,
        max_iterations=500,
        step_size=0.1,
        tolerance=1e-3
):
    points = points.copy().astype(float)
    point_count = len(points)
    max_r = np.max(radii)
    max_overlap = 0.0

    for it in range(max_iterations):
        if it % 100 == 0:
            log.info(f"[{get_now_string()}] {it} iterations done.")

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

            if bbox is not None:
                r = radii[point_index]
                if pi[0] - r < bbox[0, 0]:
                    # x too small
                    overstep = bbox[0, 0] - (pi[0] - r)
                    displacements[point_index, 0] += min(r, overstep)
                if pi[0] + r > bbox[1, 0]:
                    # x too large
                    overstep = (pi[0] + r) - bbox[1, 0]
                    displacements[point_index, 0] -= min(r, overstep)
                if pi[1] - r < bbox[0, 1]:
                    # y too small
                    overstep = bbox[0, 1] - (pi[1] - r)
                    displacements[point_index, 1] += min(r, overstep)
                if pi[1] + r > bbox[1, 1]:
                    # y too large
                    overstep = (pi[1] + r) - bbox[1, 1]
                    displacements[point_index, 1] -= min(r, overstep)

        points += step_size * displacements

        if max_overlap < tolerance:
            log.info(f"Overlap adjustments done in {it} iterations; max_overlap {max_overlap:.6f}")
            break
        # for point_index in range(point_count):
        #     pi = points[point_index]

    else:
        log.info(
            f"Overlap adjustments reached {max_iterations} iterations, the maximum; final overlap {max_overlap:.6f}"
        )

    return points


def plot_nearest_neighbor_distances(distances):
    plt.boxplot(distances)
    plt.show()


def plot_adjusted_positions(scaled, adjusted, radius, bbox, output_name):
    fig, ax = plt.subplots()
    fig.set_size_inches(12, 12)
    ax.set_aspect('equal')
    ax.scatter(scaled[:, 0], scaled[:, 1], c='red', s=20, label='original')
    for p in adjusted:
        circle = plt.Circle(p, radius, color='blue', fill=False, alpha=0.7)
        ax.add_patch(circle)
    plt.plot(
        [bbox[0, 0], bbox[1, 0], bbox[1, 0], bbox[0, 0], bbox[0, 0]],
        [bbox[0, 1], bbox[0, 1], bbox[1, 1], bbox[1, 1], bbox[0, 1]],
        color="blue", linewidth=2
    )
    plt.legend()
    plt.savefig(output_name)


def get_rotation_matrix(theta: float):
    return np.array(
        [
            [np.cos(theta), -np.sin(theta)],
            [np.sin(theta), np.cos(theta)]
        ]
    )


def get_minimum_obb(positions: npt.NDArray[np.float32], padding: float = 0):
    hull = ConvexHull(positions)
    hull_points = positions[hull.vertices]
    min_area = np.inf
    best_angle = 0
    best_box = None

    for i in range(len(hull_points)):
        p1 = hull_points[i]
        p2 = hull_points[(i + 1) % len(hull_points)]

        edge_angle = np.arctan2(p2[1] - p1[1], p2[0] - p1[0])

        rotation_matrix = get_rotation_matrix(-edge_angle)
        rotated = hull_points @ rotation_matrix

        min_x, max_x = rotated[:, 0].min() - padding, rotated[:, 0].max() + padding
        min_y, max_y = rotated[:, 1].min() - padding, rotated[:, 1].max() + padding
        area = (max_x - min_x) * (max_y - min_y)

        if area < min_area:
            min_area = area
            best_angle = edge_angle

            box_rot = np.array(
                [
                    [min_x, min_y],
                    [max_x, min_y],
                    [max_x, max_y],
                    [min_x, max_y]
                ]
            )

            best_box = box_rot @ rotation_matrix.T

    return best_angle, min_area, best_box


def plot_obb(positions: npt.NDArray[np.float32], box: npt.NDArray[np.float32]):
    plt.figure(figsize=(10, 10))
    plt.scatter(positions[:, 0], positions[:, 1], color='lightblue', label='Positions', alpha=0.7)

    box_closed = np.vstack([box, box[0]])  # close polygon
    plt.plot(box_closed[:, 0], box_closed[:, 1], 'r-', lw=2, label='Min Bounding Box')

    plt.axis('equal')
    plt.title(f"Minimal Bounding Box")
    plt.legend()
    plt.tight_layout()
    plt.show()


def get_auto_world_scale(positions: npt.ArrayLike, cell_radius: float, quantile: float):
    nn_distances, nn_indices = get_nearest_neighbor_distances(positions)
    base_distance = np.quantile(nn_distances, quantile)
    return cell_radius / base_distance


def get_bbox(positions: npt.NDArray[np.float32], padding: float = 0):
    min_x, max_x, min_y, max_y = np.min(positions[:, 0]), np.max(positions[:, 0]), np.min(
        positions[:, 1]
    ), np.max(
        positions[:, 1]
    )
    return np.array([[min_x - padding, min_y - padding], [max_x + padding, max_y + padding]])


def print_bbox(bbox, name):
    bbox_w = bbox[1, 0] - bbox[0, 0]
    bbox_h = bbox[1, 1] - bbox[0, 1]
    log.info(f"{name}: w={bbox_w}, h={bbox_h}, area={bbox_w * bbox_h}")


def get_area(positions: npt.NDArray[np.float32]):
    bbox = get_bbox(positions)
    return (bbox[1, 0] - bbox[0, 0]) * (bbox[1, 1] - bbox[0, 1])


def center_positions(positions: npt.NDArray[np.float32]):
    bbox = get_bbox(positions)
    center = (bbox[0] + bbox[1]) / 2
    return positions - center


def minimize_world_bbox(positions: npt.NDArray[np.float32]):
    angle, area, _ = get_minimum_obb(positions)
    rotation_matrix = get_rotation_matrix(-angle)
    rotated_positions = positions @ rotation_matrix
    rotated_positions = center_positions(rotated_positions)
    return rotated_positions


def get_area_world_scale(
        positions: npt.NDArray[np.float32],
        cell_radius: float,
        target_area: float,
        padding_cells: int = 1
):
    bbox = get_bbox(positions)
    w = bbox[1, 0] - bbox[0, 0]
    h = bbox[1, 1] - bbox[0, 1]
    area = w * h
    log.info(f"Original box: w={w}, h={h}, area={area}")
    target = target_area * (2 * cell_radius * 2 * cell_radius)
    log.info(f"Target box area: {target}")
    padding = 2 * cell_radius * padding_cells
    factor_1 = (padding * (
            (-w - h) + np.sqrt((w - h) * (w - h) + (4 * area * target) / (padding * padding))) / (2 * area))
    factor_2 = (padding * (
            (-w - h) - np.sqrt((w - h) * (w - h) + (4 * area * target) / (padding * padding))) / (2 * area))
    scale = max(factor_1, factor_2)
    return scale

    # scaled_positions = rotated_positions * scale
    #
    # plt.subplots(figsize=(6, 6))
    # plt.scatter(scaled_positions[:, 0], scaled_positions[:, 1], color='lightblue', label='Positions', alpha=0.7)
    # new_bbox = get_bbox(scaled_positions)
    # new_bbox_w = new_bbox[1, 0] - new_bbox[0, 0]
    # new_bbox_h = new_bbox[1, 1] - new_bbox[0, 1]
    # log.info(f"New box without padding: w={new_bbox_w}, h={new_bbox_h}, area={new_bbox_w * new_bbox_h}")
    # pad_bbox = np.array([new_bbox[0, :] - cell_radius, new_bbox[1, :] + cell_radius])
    # plt.plot([new_bbox[0, 0], new_bbox[1, 0], new_bbox[1, 0], new_bbox[0, 0], new_bbox[0, 0]],
    #          [new_bbox[0, 1], new_bbox[0, 1], new_bbox[1, 1], new_bbox[1, 1], new_bbox[0, 1]],
    #          color="red", linewidth=2)
    # plt.plot([pad_bbox[0, 0], pad_bbox[1, 0], pad_bbox[1, 0], pad_bbox[0, 0], pad_bbox[0, 0]],
    #          [pad_bbox[0, 1], pad_bbox[0, 1], pad_bbox[1, 1], pad_bbox[1, 1], pad_bbox[0, 1]],
    #          color="blue", linewidth=2)
    # plt.show()


def main():
    logging.basicConfig(level=logging.INFO)
    args_parser = argparse.ArgumentParser(PROGRAM_NAME)
    args_parser.add_argument("input_path")
    args_parser.add_argument("--cell-radius", default=4.5, type=float)
    args_parser.add_argument("--max-iterations", default=-1, type=int)
    args_parser.add_argument(
        "--world-sizing",
        default=dt.WorldSizing.AUTO,
        type=dt.WorldSizing,
        choices=list(dt.WorldSizing)
    )
    args_parser.add_argument("--world-sizing-ratio", default=0.5, type=float)
    args_parser.add_argument("--world-padding", default=2, type=int)
    args_parser.add_argument("--world-sizing-auto-quantile", default=0.5, type=float)
    args_parser.add_argument("--no-adjustment", default=False, action="store_true")
    args_parser.add_argument("--data-path", default=None)
    args_parser.add_argument("--plot-result", action="store_true")
    args_parser.add_argument("--output", default=None)
    options = dt.PositionAdjustmentOptions(**vars(args_parser.parse_args()))

    if options.world_sizing != dt.WorldSizing.AUTO and options.data_path is None:
        raise RuntimeError("If --world-sizing is not auto, --data-path is required.")

    input_path = Path(options.input_path)
    if not input_path.exists():
        raise RuntimeError(f"Input file '{options.input_path}' does not exist")

    log.info(f"Reading input file '{input_path.name}'")
    model = dt.TopicModellingResult.model_validate_json(input_path.read_bytes())
    data: dt.MiningResult | None = None
    if options.data_path is not None:
        data = dt.MiningResult.model_validate_json(Path(options.data_path).read_bytes())
    items = sorted(model.items.values(), key=lambda i: i.id)
    positions = np.array([[item.x, item.y] for item in items])
    positions = center_positions(positions)
    positions = minimize_world_bbox(positions)
    options.world_scale = 1.0
    padding_cells: int = options.world_padding
    match options.world_sizing:
        case dt.WorldSizing.AUTO:
            log.info("Setting world scale automatically to, on average, prevent collisions.")
            options.world_scale = get_auto_world_scale(
                positions,
                options.cell_radius,
                options.world_sizing_auto_quantile
            )
        case dt.WorldSizing.REPO_SIZE:
            log.info(f"Setting world scale based on repository size ({data.repository.size} KB).")
            log.info(f"Ratio of repository size to number of artifacts: {options.world_sizing_ratio}")
            options.world_scale = get_area_world_scale(
                positions, options.cell_radius,
                target_area=data.repository.size / options.world_sizing_ratio,
                padding_cells=padding_cells
            )
        case dt.WorldSizing.FILE_COUNT:
            file_count = data.repository.cloc.get_file_count()
            log.info(f"Setting world scale based on number of code files ({file_count} files).")
            log.info(f"Ratio of code file count to number of artifacts: {options.world_sizing_ratio}")
            options.world_scale = get_area_world_scale(
                positions, options.cell_radius, target_area=file_count / options.world_sizing_ratio,
                padding_cells=padding_cells
            )
        case dt.WorldSizing.LINE_COUNT:
            line_count = data.repository.git_loc.get_line_count([]) / 1000.0
            log.info(f"Setting world scale based on number of thousands of lines of code ({line_count} kLoC).")
            log.info(f"Cloc reported {data.repository.cloc.get_code_lines() / 1000.0} kLoC.")
            log.info(f"Ratio of kLoC to number of artifacts: {options.world_sizing_ratio}")
            options.world_scale = get_area_world_scale(
                positions, options.cell_radius, target_area=line_count / options.world_sizing_ratio,
                padding_cells=padding_cells
            )

    log.info(f"World scale: {options.world_scale}")
    scaled_positions = positions * options.world_scale
    padded_bbox = get_bbox(scaled_positions, padding=padding_cells * options.cell_radius)
    print_bbox(padded_bbox, "Scaled & padded box")

    if options.max_iterations == -1:
        options.max_iterations = len(model.items)
        log.info(f"Automatically set max iterations to {options.max_iterations}")
    adjusted_positions = adjust_positions(
        scaled_positions,
        radii=np.repeat(options.cell_radius, scaled_positions.size),
        bbox=(None if options.world_sizing == dt.WorldSizing.AUTO else padded_bbox),
        max_iterations=options.max_iterations,
        step_size=options.cell_radius / 10
    )

    for new_pos, item in zip(adjusted_positions, items):
        item.x = new_pos[0]
        item.y = new_pos[1]
    model.items = {item.id: item for item in items}

    now_string = get_now_string()

    if options.output is None:
        options.output = f"./out/adjusted_{model.name}_{now_string}.json"
        log.info(f"Automatically set output path to '{options.output}'")
    output = Path(options.output)

    model.position_adjustment_options = options

    if output.parent is not None:
        output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf8") as output_file:
        output_file.write(model.model_dump_json())

    if options.plot_result:
        plot_adjusted_positions(
            scaled_positions,
            adjusted_positions,
            options.cell_radius,
            padded_bbox,
            f"./out/adjusted_{model.name}_{now_string}.png"
        )


if __name__ == '__main__':
    main()
