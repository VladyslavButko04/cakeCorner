using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Models;

public sealed class CakeCatalogPage
{
    public const int DefaultPageSize = 12;

    public List<Cake> Items { get; init; } = [];
    public CakeFilterViewModel Filters { get; init; } = new();
    public int Page { get; init; } = 1;
    public int TotalCount { get; init; }
    public int PageSize => DefaultPageSize;
    public int TotalPages => TotalCount / PageSize + (TotalCount % PageSize == 0 ? 0 : 1);
    public int FirstItem => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastItem => TotalCount == 0 ? 0 : FirstItem + Items.Count - 1;
}
