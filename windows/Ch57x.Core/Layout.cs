namespace Ch57x.Core;

/// <summary>Per-model key row layout (ported from web/js/app.js KEY_ROWS). Max 5 cols × 3 rows.</summary>
public static class Layout
{
    private static readonly IReadOnlyDictionary<int, int[]> KeyRowsMap = new Dictionary<int, int[]>
    {
        [1] = new[] { 1 }, [2] = new[] { 2 }, [3] = new[] { 3 }, [4] = new[] { 2, 2 },
        [5] = new[] { 5 }, [6] = new[] { 3, 3 }, [7] = new[] { 4, 3 }, [8] = new[] { 4, 4 },
        [9] = new[] { 3, 3, 3 }, [10] = new[] { 5, 5 }, [11] = new[] { 4, 4, 3 },
        [12] = new[] { 4, 4, 4 }, [13] = new[] { 5, 5, 3 }, [14] = new[] { 5, 5, 4 },
        [15] = new[] { 5, 5, 5 },
    };

    /// <summary>Returns row lengths for the given key count (1..15). Fallback chunks of 5.</summary>
    public static int[] KeyRows(int keyCount)
    {
        if (KeyRowsMap.TryGetValue(keyCount, out var rows)) return rows;
        var fallback = new List<int>();
        for (int n = keyCount; n > 0; n -= 5) fallback.Add(Math.Min(5, n));
        return fallback.ToArray();
    }
}
