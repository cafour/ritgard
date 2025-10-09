from shapely import wkt
import matplotlib.pyplot as plt

wkt_polygon = "POLYGON ((-188.45465087890625 99.41177368164062, -63.561553955078125 258.3214416503906, 7.691192626953125 319.5196838378906, 12.89642333984375 310.981201171875, 16.583099365234375 301.6287841796875, 24.652191162109375 259.5838317871094, 25.411407470703125 249.61270141601562, 15.152679443359375 232.63430786132812, 4.059356689453125 -112.957275390625, -188.45465087890625 99.41177368164062))"

# Parse the WKT into a Shapely geometry
polygon = wkt.loads(wkt_polygon)

# Extract x and y coordinates of the polygon exterior
x, y = polygon.exterior.xy

# Plot the polygon
plt.figure(figsize=(6, 6))
plt.plot(x, y, color="blue", linewidth=2)
plt.fill(x, y, color="lightblue", alpha=0.5)
plt.title("WKT Polygon")
plt.xlabel("X")
plt.ylabel("Y")
plt.axis("equal")  # keep aspect ratio square
plt.show()
