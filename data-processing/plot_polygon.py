from shapely import wkt
import matplotlib.pyplot as plt

# Path to your file with one WKT polygon per line
input_file = "polygons.txt"

# Read and parse all WKT polygons
polygons = []
with open(input_file, "r") as f:
    for line in f:
        line = line.strip()
        if not line:
            continue  # skip empty lines
        try:
            geom = wkt.loads(line)
            polygons.append(geom)
        except Exception as e:
            print(f"Skipping invalid WKT: {line}\nError: {e}")

# Plot all polygons
plt.figure(figsize=(8, 8))
for poly in polygons:
    if poly.geom_type == "Polygon":
        x, y = poly.exterior.xy
        plt.fill(x, y, alpha=0.4)
        plt.plot(x, y, color="black", linewidth=1)
    elif poly.geom_type == "MultiPolygon":
        for subpoly in poly.geoms:
            x, y = subpoly.exterior.xy
            plt.fill(x, y, alpha=0.4)
            plt.plot(x, y, color="black", linewidth=1)

plt.title("WKT Polygons from File")
plt.xlabel("X")
plt.ylabel("Y")
plt.axis("equal")
plt.show()
