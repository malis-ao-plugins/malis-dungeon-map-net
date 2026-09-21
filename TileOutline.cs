namespace MalisDungeonMapNet;

/// <summary>
/// The boundary of a tile bitmap, as line segments in tile coordinates.
///
/// An edge belongs to the outline when the two tiles either side of it differ,
/// which covers the outside edge and any hole. Runs of collinear edges are
/// merged, so a straight 30-tile wall is one segment rather than thirty.
/// </summary>
public static class TileOutline
{
    public static List<(float X1, float Z1, float X2, float Z2)> Trace(bool[,] mask, int w, int h)
    {
        var segments = new List<(float, float, float, float)>();
        bool At(int x, int z) => x >= 0 && z >= 0 && x < w && z < h && mask[x, z];

        // edges that run along x, one grid line at a time
        for (int z = 0; z <= h; z++)
        {
            int runStart = -1;
            for (int x = 0; x <= w; x++)
            {
                bool edge = x < w && At(x, z - 1) != At(x, z);
                if (edge && runStart < 0) runStart = x;
                if (!edge && runStart >= 0)
                {
                    segments.Add((runStart, z, x, z));
                    runStart = -1;
                }
            }
        }

        // and the ones that run along z
        for (int x = 0; x <= w; x++)
        {
            int runStart = -1;
            for (int z = 0; z <= h; z++)
            {
                bool edge = z < h && At(x - 1, z) != At(x, z);
                if (edge && runStart < 0) runStart = z;
                if (!edge && runStart >= 0)
                {
                    segments.Add((x, runStart, x, z));
                    runStart = -1;
                }
            }
        }

        return segments;
    }
}
