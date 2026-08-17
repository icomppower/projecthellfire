using System;

namespace Hellfire.Sim
{
    /// <summary>
    /// Uniform-grid spatial hash over flat SoA positions, rebuilt per tick with a
    /// counting sort. Deterministic by construction: cells are scanned in fixed
    /// (row, column) order and each cell holds agent indices in ascending order,
    /// so query results are always sorted ascending — float accumulation order
    /// downstream can never vary. Verified against brute force (step-1 gate 3);
    /// the failure mode being tested for is Pacific Commander's: a bad grid
    /// *drops* occupants silently rather than erroring.
    /// </summary>
    public sealed class SpatialHash
    {
        private readonly float _cellSize;
        private readonly int _cols;
        private readonly int _rows;
        private readonly float _width;
        private readonly float _height;
        private readonly int[] _cellStart;   // length cols*rows + 1
        private readonly int[] _cellCounts;  // scratch, length cols*rows
        private readonly int[] _entries;     // agent indices grouped by cell
        private int _count;

        public SpatialHash(float width, float height, float cellSize, int capacity)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            _width = width;
            _height = height;
            _cellSize = cellSize;
            _cols = Math.Max(1, (int)(width / cellSize));
            _rows = Math.Max(1, (int)(height / cellSize));
            _cellStart = new int[_cols * _rows + 1];
            _cellCounts = new int[_cols * _rows];
            _entries = new int[capacity];
        }

        private int CellIndex(float x, float y)
        {
            // Clamp instead of wrap: out-of-range positions land in edge cells,
            // never outside the grid — occupants cannot be dropped by rounding.
            int cx = (int)(x / _cellSize);
            int cy = (int)(y / _cellSize);
            if (cx < 0) cx = 0; else if (cx >= _cols) cx = _cols - 1;
            if (cy < 0) cy = 0; else if (cy >= _rows) cy = _rows - 1;
            return cy * _cols + cx;
        }

        public void Build(float[] posX, float[] posY, int count)
        {
            if (count > _entries.Length) throw new ArgumentOutOfRangeException(nameof(count));
            _count = count;
            Array.Clear(_cellCounts, 0, _cellCounts.Length);
            for (int i = 0; i < count; i++) _cellCounts[CellIndex(posX[i], posY[i])]++;

            int running = 0;
            for (int c = 0; c < _cellCounts.Length; c++)
            {
                _cellStart[c] = running;
                running += _cellCounts[c];
            }
            _cellStart[_cellCounts.Length] = running;

            // Stable placement: ascending agent index within each cell.
            Array.Clear(_cellCounts, 0, _cellCounts.Length);
            for (int i = 0; i < count; i++)
            {
                int c = CellIndex(posX[i], posY[i]);
                _entries[_cellStart[c] + _cellCounts[c]] = i;
                _cellCounts[c]++;
            }
        }

        /// <summary>
        /// Writes indices of all agents within <paramref name="radius"/> of (x, y)
        /// (excluding <paramref name="self"/>, pass -1 to include all) into
        /// <paramref name="results"/>, ascending. Returns the count.
        /// </summary>
        public int QueryRadius(float x, float y, float radius, int self,
                               float[] posX, float[] posY, int[] results)
        {
            float r2 = radius * radius;
            int cx0 = (int)((x - radius) / _cellSize);
            int cy0 = (int)((y - radius) / _cellSize);
            int cx1 = (int)((x + radius) / _cellSize);
            int cy1 = (int)((y + radius) / _cellSize);
            if (cx0 < 0) cx0 = 0;
            if (cy0 < 0) cy0 = 0;
            if (cx1 >= _cols) cx1 = _cols - 1;
            if (cy1 >= _rows) cy1 = _rows - 1;

            int n = 0;
            for (int cy = cy0; cy <= cy1; cy++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int c = cy * _cols + cx;
                    int end = _cellStart[c + 1];
                    for (int e = _cellStart[c]; e < end; e++)
                    {
                        int j = _entries[e];
                        if (j == self) continue;
                        float dx = posX[j] - x;
                        float dy = posY[j] - y;
                        if (dx * dx + dy * dy <= r2) results[n++] = j;
                    }
                }
            }
            // Cells are scanned row-major and entries are ascending within a cell,
            // but across cells indices interleave — sort so callers see one canonical
            // order regardless of grid geometry.
            Array.Sort(results, 0, n);
            return n;
        }
    }
}
