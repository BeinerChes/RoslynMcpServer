namespace SharpOps.Examples.Data;

public class BlogContext : DbContext
{


    public DbSet<Blog> Blogs => Set<Blog>();


    public DbSet<Post> Posts => Set<Post>();


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        return optionsBuilder.UseSqlite(DbLogger.Post);
    }
}