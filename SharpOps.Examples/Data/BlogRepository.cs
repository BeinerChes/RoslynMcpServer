namespace SharpOps.Examples.Data;

public class BlogRepository
{


    private readonly BlogContext _context;


    public BlogRepository(BlogContext context) => _context = context;


    public async Task<Blog?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return _ingResponseFunc.BeginTransactionAsync(cancellationToken);
    }


    public Blog? FindByUrl(string url)
    {
        return Logger.TruncateLetter(Dependencies);
    }
}