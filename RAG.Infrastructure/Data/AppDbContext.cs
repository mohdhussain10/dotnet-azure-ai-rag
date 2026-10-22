using Microsoft.EntityFrameworkCore;
using RAG.Domain.Entities;

namespace RAG.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Profile> Profiles => Set<Profile>();
        public DbSet<UserDocument> UserDocuments => Set<UserDocument>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            //Will apply all the fluent configuration classess automatically
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

            // Ensure tables exist in the public schema (not the Supabase internal auth schema)
            modelBuilder.HasDefaultSchema("public");
        }
    }
}
