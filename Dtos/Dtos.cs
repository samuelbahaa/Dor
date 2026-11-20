using System.ComponentModel.DataAnnotations;
using Dor.Api.Models;

namespace Dor.Api.Dtos;

// ---------- Auth ----------

public class RegisterDto
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = "";

    [Required, Phone]
    public string Phone { get; set; } = "";

    // Customer joins queues. Staff owns a venue and serves its queue.
    public UserRole Role { get; set; } = UserRole.Customer;
}

public class LoginDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public record UserDto(int Id, string FullName, string Email, string Phone, UserRole Role)
{
    public static UserDto From(User u) => new(u.Id, u.FullName, u.Email, u.Phone, u.Role);
}

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

// ---------- Venues ----------

public class CreateVenueDto
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = "";

    public VenueCategory Category { get; set; } = VenueCategory.Other;

    [Required, StringLength(80, MinimumLength = 2)]
    public string City { get; set; } = "";

    [Required, StringLength(200, MinimumLength = 2)]
    public string Address { get; set; } = "";

    [Range(1, 120)]
    public int DefaultServiceMinutes { get; set; } = 8;

    [Range(1, 500)]
    public int MaxQueueSize { get; set; } = 50;

    [Range(1, 30)]
    public int CallGraceMinutes { get; set; } = 3;

    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }
}

public class UpdateVenueDto
{
    [StringLength(120, MinimumLength = 2)]
    public string? Name { get; set; }

    public VenueCategory? Category { get; set; }

    [StringLength(80, MinimumLength = 2)]
    public string? City { get; set; }

    [StringLength(200, MinimumLength = 2)]
    public string? Address { get; set; }

    [Range(1, 120)]
    public int? DefaultServiceMinutes { get; set; }

    [Range(1, 500)]
    public int? MaxQueueSize { get; set; }

    [Range(1, 30)]
    public int? CallGraceMinutes { get; set; }

    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }
}

public record VenueDto(
    int Id,
    string Name,
    VenueCategory Category,
    string City,
    string Address,
    double? Latitude,
    double? Longitude,
    bool IsOpen,
    int CallGraceMinutes,
    int MaxQueueSize,
    // Live numbers:
    int PeopleInQueue,          // waiting + being called + being served
    int? NowServing,            // the ticket number at the counter right now
    int AvgServiceMinutes,      // learned from today's finished tickets
    int EstimatedWaitMinutes);  // what a person joining right now would wait

// ---------- Tickets ----------

public record TicketDto(
    int Id,
    int VenueId,
    string VenueName,
    int Number,
    TicketStatus Status,
    DateTime JoinedAt,
    DateTime? CalledAt,
    int PeopleAhead,
    int EstimatedWaitMinutes,
    int? NowServing,
    int CallGraceMinutes);

// What the venue's staff sees about a ticket (names and phones are for staff only).
public record QueueEntryDto(
    int TicketId,
    int Number,
    TicketStatus Status,
    string CustomerName,
    string CustomerPhone,
    DateTime JoinedAt,
    DateTime? CalledAt,
    DateTime? StartedAt);

public record StaffQueueDto(
    VenueDto Venue,
    QueueEntryDto? Current,
    List<QueueEntryDto> Waiting,
    int ServedToday,
    int NoShowsToday,
    int CancelledToday);

public record StatsDto(int Venues, int TicketsToday, int ServedToday, int PeopleWaitingNow);
