namespace Dor.Api.Models;

// One person's place in one venue's queue for one day.
public class Ticket
{
    public int Id { get; set; }

    public int VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Numbers restart at 1 every day, per venue (like the paper tickets: "No. 17").
    public DateOnly ServiceDate { get; set; }
    public int Number { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.Waiting;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CalledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // So we send the "you are almost up" notification only once.
    public bool AlmostNotified { get; set; }
}
