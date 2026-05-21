namespace TaskFlow.Application.Common.Models;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)Total / PageSize);
}

public record PageQuery(int Page = 1, int PageSize = 25)
{
    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedSize => PageSize is < 1 or > 100 ? 25 : PageSize;
    public int Skip => (NormalizedPage - 1) * NormalizedSize;
}
