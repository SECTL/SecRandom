namespace SecRandom.Core.Services.Profiles;

/// <summary>
/// 表格导入区域。行号和列号都是 1 起的原始表格坐标，与用户在 Excel 里看到的一致：
/// MiniExcel 以 <c>useHeaderRow: false</c> 读取时会把中间缺失的行补成空行，所以序号即真实行号。
/// <see cref="HeaderRow"/> 为 null 表示整张表没有表头行，列名退化为 A、B、C…。
/// </summary>
public sealed record RosterImportRegion(
    int? HeaderRow,
    int FirstDataRow,
    int? LastDataRow,
    int FirstColumn,
    int? LastColumn)
{
    /// <summary>默认区域：指定表头行，其后取全部数据行与全部列，等价于改造前的「第一行即表头」。</summary>
    public static RosterImportRegion Default(int headerRow) => new(headerRow, headerRow + 1, null, 1, null);
}

/// <summary>区域解析结果。<see cref="Columns"/> 保证非空且互不重复，可直接作为行字典的键。</summary>
public sealed record RosterImportTable(
    IReadOnlyList<string> Columns,
    IReadOnlyList<Dictionary<string, string>> Rows);

/// <summary>
/// 导入区域的纯解析逻辑：表头行识别、区域到列名/行字典的转换。
/// 视图只负责 MiniExcel 读取和交互，移动端接入表格解析后可直接复用。
/// </summary>
public static class RosterImportRegionBuilder
{
    /// <summary>自动识别表头行时最多向下的扫描行数。</summary>
    public const int HeaderScanRowLimit = 10;

    /// <summary>
    /// 按关键字给一行的内容打分，用于自动识别表头行；与列映射共用 <see cref="RosterImportParser.ScoreColumn"/> 的关键字规则。
    /// </summary>
    public static int ScoreRow(IEnumerable<string?> cells, IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(keywords);

        var score = 0;
        foreach (var cell in cells)
        {
            if (string.IsNullOrWhiteSpace(cell))
                continue;

            score += RosterImportParser.ScoreColumn(cell, keywords);
        }

        return score;
    }

    /// <summary>
    /// 在前 <see cref="HeaderScanRowLimit"/> 行里挑最像表头的一行。
    /// 全部识别不出来时返回 1，保持「第一行即表头」的历史行为，用户可在界面上手动改。
    /// </summary>
    public static int DetectHeaderRow(IReadOnlyList<IReadOnlyList<string?>> grid, IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(keywords);

        var bestRow = 1;
        var bestScore = 0;
        var limit = Math.Min(HeaderScanRowLimit, grid.Count);
        for (var row = 1; row <= limit; row++)
        {
            var score = ScoreRow(grid[row - 1], keywords);
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestRow = row;
        }

        return bestRow;
    }

    /// <summary>
    /// 最后一个出现过非空单元格的行号（1 起），整张表都没有内容时返回 0。
    /// MiniExcel 会把文件里存在的空行元素补成整行，表格尾部被按过回车或被格式刷过的空行会让总行数虚高，
    /// 所以行数上限、默认数据区末尾和预览范围都按真实内容算，而不是按网格长度。
    /// </summary>
    public static int ResolveLastContentRow(IReadOnlyList<IReadOnlyList<string?>> grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        for (var row = grid.Count; row >= 1; row--)
        {
            foreach (var cell in grid[row - 1])
            {
                if (!string.IsNullOrWhiteSpace(cell))
                    return row;
            }
        }

        return 0;
    }

    /// <summary>
    /// 从 <paramref name="firstRow"/> 起最后一个出现过非空单元格的列号，用来裁掉右侧整列空白的列，
    /// 避免列映射下拉里出现大量无意义的 A、B、C 占位项。
    /// </summary>
    public static int ResolveColumnCount(IReadOnlyList<IReadOnlyList<string?>> grid, int firstRow)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var columnCount = 0;
        var start = Math.Max(1, firstRow);
        for (var row = start; row <= grid.Count; row++)
        {
            var cells = grid[row - 1];
            for (var column = cells.Count; column > columnCount; column--)
            {
                if (!string.IsNullOrWhiteSpace(cells[column - 1]))
                {
                    columnCount = column;
                    break;
                }
            }
        }

        return columnCount;
    }

    /// <summary>区域 → 列名 + 行字典。列名取表头单元格，空表头回退为列字母，重复列名追加序号。</summary>
    public static RosterImportTable Build(IReadOnlyList<IReadOnlyList<string?>> grid, RosterImportRegion region)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(region);

        if (grid.Count == 0)
            return new RosterImportTable([], []);

        int? headerRow = null;
        if (region.HeaderRow is { } candidateHeaderRow && candidateHeaderRow >= 1 && candidateHeaderRow <= grid.Count)
            headerRow = candidateHeaderRow;

        var lastDataRow = Math.Min(region.LastDataRow ?? ResolveLastContentRow(grid), grid.Count);
        // 数据区永远不含表头行本身，否则把最后一行选成表头时会把表头当成一条数据导进去
        var firstDataRow = Math.Max(1, region.FirstDataRow);
        if (headerRow is { } headerRowNumber && firstDataRow <= headerRowNumber)
            firstDataRow = headerRowNumber + 1;

        var columnCount = ResolveColumnCount(grid, headerRow is null ? firstDataRow : Math.Min(headerRow.Value, firstDataRow));
        var firstColumn = Math.Max(1, region.FirstColumn);
        var lastColumn = Math.Min(region.LastColumn ?? columnCount, columnCount);
        if (columnCount == 0 || lastColumn < firstColumn || lastDataRow < firstDataRow)
            return new RosterImportTable([], []);

        var columns = new List<string>(lastColumn - firstColumn + 1);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var header = headerRow is null ? string.Empty : GetCell(grid[headerRow.Value - 1], column).Trim();
            var name = header.Length > 0 ? header : ColumnName(column);
            var candidate = name;
            var suffix = 2;
            while (!used.Add(candidate))
                candidate = $"{name} ({suffix++})";

            columns.Add(candidate);
        }

        var rows = new List<Dictionary<string, string>>();
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            var cells = grid[row - 1];
            var values = new Dictionary<string, string>(columns.Count, StringComparer.Ordinal);
            var hasValue = false;
            for (var index = 0; index < columns.Count; index++)
            {
                var value = GetCell(cells, firstColumn + index).Trim();
                values[columns[index]] = value;
                hasValue |= value.Length > 0;
            }

            if (hasValue)
                rows.Add(values);
        }

        return new RosterImportTable(columns, rows);
    }

    /// <summary>列号 → Excel 列字母（1 → A，27 → AA）。</summary>
    public static string ColumnName(int column)
    {
        var value = Math.Max(1, column);
        var builder = new System.Text.StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }

        return builder.ToString();
    }

    /// <summary>Excel 列字母 → 列号（A → 1，AA → 27）；不是纯字母时返回 0。</summary>
    public static int ColumnIndex(string column)
    {
        if (column.Length is 0 or > 3)
            return 0;

        var index = 0;
        foreach (var character in column)
        {
            if (character is < 'A' or > 'Z')
                return 0;

            index = index * 26 + (character - 'A' + 1);
        }

        return index;
    }

    private static string GetCell(IReadOnlyList<string?>? cells, int column)
    {
        return cells is not null && column >= 1 && column <= cells.Count
            ? cells[column - 1] ?? string.Empty
            : string.Empty;
    }
}
