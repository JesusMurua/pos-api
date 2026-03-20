namespace POS.Repository.Utils;

public class PageData<T>
{
    public IEnumerable<T> Data { get; set; } = Enumerable.Empty<T>();

    public int RowsCount { get; set; }

    public int TotalPages { get; set; }

    public int CurrentPage { get; set; }
}
