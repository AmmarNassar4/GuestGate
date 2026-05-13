using GuestGate.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
namespace GuestGate.Api.Data
{
    public class AppDb : DbContext
    {
        public AppDb(DbContextOptions<AppDb> options) : base(options) { }
        public DbSet<Template> Templates => Set<Template>();
        public DbSet<Guest> Guests => Set<Guest>();
        public DbSet<KioskSession> KioskSessions => Set<KioskSession>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Template>().HasKey(t => t.Id);
            b.Entity<KioskSession>().HasIndex(x => x.EditToken).IsUnique();
            b.Entity<KioskSession>().HasIndex(x => new { x.Kid, x.Status });
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            foreach (var e in ChangeTracker.Entries())
            {
                if (e.State == EntityState.Added || e.State == EntityState.Modified)
                {
                    switch (e.Entity)
                    {
                        case Template t:
                            if (e.State == EntityState.Added && t.CreatedAt == default) t.CreatedAt = now;
                            t.UpdatedAt = now;
                            break;
                        case Guest g:
                            if (e.State == EntityState.Added && g.CreatedAt == default) g.CreatedAt = now;
                            g.UpdatedAt = now;
                            break;
                        case KioskSession ks:
                            if (e.State == EntityState.Added && ks.CreatedAt == default) ks.CreatedAt = now;
                            ks.UpdatedAt = now;
                            break;
                    }
                }
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
