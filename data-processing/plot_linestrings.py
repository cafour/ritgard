import matplotlib.pyplot as plt
from shapely import wkt

def read_linestrings(filename):
    """Read LINESTRING geometries from a text file."""
    linestrings = []
    with open(filename, "r") as f:
        for line in f:
            line = line.strip()
            if line:  # skip empty lines
                try:
                    geom = wkt.loads(line)
                    if geom.geom_type == "LineString":
                        linestrings.append(geom)
                except Exception as e:
                    print(f"Skipping invalid WKT: {line} ({e})")
    return linestrings

def plot_linestrings(linestrings):
    """Plot a list of LineStrings using matplotlib."""
    fig, ax = plt.subplots()
    for ls in linestrings:
        x, y = ls.xy
        ax.plot(x, y, linewidth=2)
    ax.set_aspect("equal", "box")
    plt.show()

if __name__ == "__main__":
    # Example usage: assumes "lines.txt" contains WKT LINESTRINGs
    filename = "lines.txt"
    linestrings = read_linestrings(filename)
    if not linestrings:
        print("No valid LINESTRINGs found in file.")
    else:
        plot_linestrings(linestrings)
