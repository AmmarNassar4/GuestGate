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
        public DbSet<ConsentRequest> ConsentRequests => Set<ConsentRequest>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Template>().HasKey(t => t.Id);
            b.Entity<KioskSession>().HasIndex(x => x.EditToken).IsUnique();
            b.Entity<KioskSession>().HasIndex(x => new { x.Kid, x.Status });
            b.Entity<KioskSession>().HasIndex(x => new { x.Status, x.ExpiresAt });
            b.Entity<ConsentRequest>().HasIndex(x => new { x.Kid, x.Status });
            b.Entity<ConsentRequest>().Property(x => x.GuestName).HasMaxLength(200);
            b.Entity<ConsentRequest>().Property(x => x.IdentityNumber).HasMaxLength(80);
            b.Entity<ConsentRequest>().Property(x => x.CheckInTime).HasMaxLength(50);
            b.Entity<ConsentRequest>().Property(x => x.Kid).HasMaxLength(50);
            b.Entity<ConsentRequest>().Property(x => x.Language).HasMaxLength(5);
            b.Entity<ConsentRequest>().Property(x => x.Status)
                .HasMaxLength(20)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<ConsentStatus>(v, true));
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
                        case ConsentRequest cr:
                            if (e.State == EntityState.Added && cr.CreatedAt == default) cr.CreatedAt = now;
                            cr.UpdatedAt = now;
                            break;
                    }
                }
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}