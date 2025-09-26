namespace Ritgard.Mining;

public record IssueTopic(
    long Id,
    double X,
    double Y,
    string Topic,
    int NearestNeighbor,
    double NearestNeighborDistance
);
