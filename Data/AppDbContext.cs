using Dor.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dor.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.FullName).HasMaxLength(100);
            e.Property(u => u.Phone).HasMaxLength(30);
            // Enums are stored as readable text ("Staff") instead of numbers.
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Venue>(e =>
        {
            e.Property(v => v.Name).HasMaxLength(120);
            e.Property(v => v.City).HasMaxLength(80);
            e.Property(v => v.Address).HasMaxLength(200);
            e.Property(v => v.Category).HasConversion<string>().HasMaxLength(20);

            e.HasOne(v => v.Owner)
                .WithMany(u => u.Venues)
                .HasForeignKey(v => v.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(v => new { v.City, v.Category });
        });

        b.Entity<Ticket>(e =>
        {
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

            e.HasOne(t => t.Venue)
                .WithMany(v => v.Tickets)
                .HasForeignKey(t => t.VenueId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(t => t.User)
                .WithMany(u => u.Tickets)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Two people can never get the same number at the same venue on the same day.
            // If two requests race, the database rejects the second one and we retry.
            e.HasIndex(t => new { t.VenueId, t.ServiceDate, t.Number }).IsUnique();

            // A person can hold only ONE active ticket per venue (enforced by the database itself).
            e.HasIndex(t => new { t.VenueId, t.UserId })
                .IsUnique()
                .HasFilter("[Status] IN ('Waiting','Called','Serving')");
        });
    }
}
