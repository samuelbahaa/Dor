using Dor.Api.Models;
using Dor.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Dor.Api.Data;

// Fills an empty database with FICTIONAL demo data so the app looks alive on first run.
// Every demo venue is marked "(demo)" and every demo email ends with @demo.dor.
// Safe to run many times: it never duplicates users or venues, and it re-creates
// today's demo tickets if you open the app on a new day.
public static class DbSeeder
{
    public const string DemoPassword = "Demo@12345";

    private static readonly string[] CustomerNames =
    {
        "Ahmed Hassan", "Mona Adel", "Omar Khaled", "Salma Ibrahim", "Youssef Tarek",
        "Nour Samir", "Karim Mostafa", "Hana Ali", "Mahmoud Fathy", "Dina Ramy",
        "Mostafa Said", "Laila Hosny", "Hossam Naguib", "Reem Wael", "Tamer Gamal",
        "Farida Nabil", "Ali Magdy", "Yara Essam", "Sherif Lotfy", "Aya Barakat"
    };

    private record VenueSeed(string Name, VenueCategory Category, string City, string Address,
        double Lat, double Lng, int ServiceMinutes, bool Open);

    private static readonly VenueSeed[] Venues =
    {
        new("Nile Family Clinic (demo)", VenueCategory.Clinic, "Cairo", "Corniche El Nil, Maadi", 29.9602, 31.2569, 10, true),
        new("Giza Civil Registry Office (demo)", VenueCategory.Government, "Giza", "Faisal Street, Giza", 30.0131, 31.2089, 7, true),
        new("Delta Bank - Branch 7 (demo)", VenueCategory.Bank, "Cairo", "Road 9, Maadi", 29.9627, 31.2503, 5, true),
        new("Helwan Pharmacy (demo)", VenueCategory.Pharmacy, "Helwan", "Mansour Street, Helwan", 29.8414, 31.3340, 3, true),
        new("Maadi Barber Studio (demo)", VenueCategory.Salon, "Cairo", "Street 233, Maadi", 29.9606, 31.2585, 20, true),
        new("Old Town Dental (demo) - closed", VenueCategory.Clinic, "Cairo", "El Moez Street", 30.0478, 31.2625, 25, false)
    };

    public static async Task SeedAsync(AppDbContext db)
    {
        // 1) Users and venues (only once).
        if (!await db.Users.AnyAsync(u => u.Email.EndsWith("@demo.dor")))
        {
            // One hash for everybody: hashing 25 passwords one by one would be slow.
            var hash = BCrypt.Net.BCrypt.HashPassword(DemoPassword);

            for (var i = 0; i < CustomerNames.Length; i++)
            {
                db.Users.Add(new User
                {
                    FullName = CustomerNames[i],
                    Email = $"cust{i + 1}@demo.dor",
                    PasswordHash = hash,
                    Phone = $"+2010000{i + 1:D5}",
                    Role = UserRole.Customer
                });
            }

            var staff = new List<User>();
            for (var i = 0; i < Venues.Length; i++)
            {
                var s = new User
                {
                    FullName = $"Staff {i + 1} (demo)",
                    Email = $"staff{i + 1}@demo.dor",
                    PasswordHash = hash,
                    Phone = $"+2011000{i + 1:D5}",
                    Role = UserRole.Staff
                };
                staff.Add(s);
                db.Users.Add(s);
            }

            await db.SaveChangesAsync();

            for (var i = 0; i < Venues.Length; i++)
            {
                var v = Venues[i];
                db.Venues.Add(new Venue
                {
                    Name = v.Name,
                    Category = v.Category,
                    City = v.City,
                    Address = v.Address,
                    Latitude = v.Lat,
                    Longitude = v.Lng,
                    IsOpen = v.Open,
                    DefaultServiceMinutes = v.ServiceMinutes,
                    OwnerId = staff[i].Id
                });
            }

            await db.SaveChangesAsync();
        }

        // 2) Today's tickets (re-created on a new day).
        var day = AppClock.Today();

        var demoVenues = await db.Venues
            .Where(v => v.Owner.Email.EndsWith("@demo.dor") && v.IsOpen)
            .OrderBy(v => v.Id)
            .ToListAsync();

        var demoVenueIds = demoVenues.Select(v => v.Id).ToList();

        if (await db.Tickets.AnyAsync(t => demoVenueIds.Contains(t.VenueId) && t.ServiceDate == day))
            return;

        var customers = await db.Users
            .Where(u => u.Email.EndsWith("@demo.dor") && u.Role == UserRole.Customer)
            .OrderBy(u => u.Id)
            .ToListAsync();

        if (customers.Count < 5)
            return;

        var now = DateTime.UtcNow;

        // The first two customers stay free of tickets, so you can use them to join a queue yourself.
        var activeCounter = 0;
        User NextActiveCustomer() => customers[2 + (activeCounter++ % (customers.Count - 2))];

        for (var vi = 0; vi < demoVenues.Count; vi++)
        {
            var venue = demoVenues[vi];
            var number = 1;

            // A few people already served today (this teaches the wait estimate).
            var doneCount = 3 + vi % 3;
            for (var i = 0; i < doneCount; i++)
            {
                var start = now.AddMinutes(-120 + i * 15);
                db.Tickets.Add(new Ticket
                {
                    VenueId = venue.Id,
                    UserId = customers[(vi * 3 + i) % customers.Count].Id,
                    ServiceDate = day,
                    Number = number++,
                    Status = TicketStatus.Done,
                    JoinedAt = start.AddMinutes(-12),
                    CalledAt = start.AddMinutes(-1),
                    StartedAt = start,
                    CompletedAt = start.AddMinutes(venue.DefaultServiceMinutes - 1 + i % 2)
                });
            }

            // One no-show and one cancellation, for a realistic day.
            if (vi == 0)
            {
                db.Tickets.Add(new Ticket
                {
                    VenueId = venue.Id,
                    UserId = customers[10].Id,
                    ServiceDate = day,
                    Number = number++,
                    Status = TicketStatus.NoShow,
                    JoinedAt = now.AddMinutes(-70),
                    CalledAt = now.AddMinutes(-40),
                    CompletedAt = now.AddMinutes(-36)
                });

                db.Tickets.Add(new Ticket
                {
                    VenueId = venue.Id,
                    UserId = customers[11].Id,
                    ServiceDate = day,
                    Number = number++,
                    Status = TicketStatus.Cancelled,
                    JoinedAt = now.AddMinutes(-65),
                    CompletedAt = now.AddMinutes(-50)
                });
            }

            // Someone is at the counter right now.
            db.Tickets.Add(new Ticket
            {
                VenueId = venue.Id,
                UserId = NextActiveCustomer().Id,
                ServiceDate = day,
                Number = number++,
                Status = TicketStatus.Serving,
                JoinedAt = now.AddMinutes(-25),
                CalledAt = now.AddMinutes(-6),
                StartedAt = now.AddMinutes(-4),
                AlmostNotified = true
            });

            // And a line of people waiting.
            var waitingCount = 3 + vi;
            for (var k = 0; k < waitingCount; k++)
            {
                db.Tickets.Add(new Ticket
                {
                    VenueId = venue.Id,
                    UserId = NextActiveCustomer().Id,
                    ServiceDate = day,
                    Number = number++,
                    Status = TicketStatus.Waiting,
                    JoinedAt = now.AddMinutes(-20 + k * 2),
                    AlmostNotified = true
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
