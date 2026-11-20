namespace Dor.Api.Models;

// A place that has a queue: a clinic, a government office, a bank branch...
public class Venue
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public VenueCategory Category { get; set; } = VenueCategory.Other;
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Closed venues do not accept new tickets.
    public bool IsOpen { get; set; } = true;

    // Used for the wait estimate until the venue has served enough people today.
    public int DefaultServiceMinutes { get; set; } = 8;

    // The queue stops accepting people at this size.
    public int MaxQueueSize { get; set; } = 50;

    // How long a called customer has to reach the counter before being marked NoShow.
    public int CallGraceMinutes { get; set; } = 3;

    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Ticket> Tickets { get; set; } = new();
}
