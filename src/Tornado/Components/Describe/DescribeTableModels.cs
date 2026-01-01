using Tornado.Models;

namespace Tornado.Components.Describe;

public sealed record DescribeTableColumn(
    string Key,
    string Label,
    bool Sortable = true,
    string? Align = null,
    string? Width = null
);

public sealed record DescribeTableCell(
    string Text,
    string? SortValue = null,
    string? CopyValue = null,
    ResourceRef? Link = null,
    bool IsHtml = false
);

public sealed record DescribeTableRow(
    IReadOnlyDictionary<string, DescribeTableCell> Cells
);
