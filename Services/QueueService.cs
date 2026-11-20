using Dor.Api.Data;
using Dor.Api.Dtos;
using Dor.Api.Models;
using Dor.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dor.Api.Services;

// A small "result" wrapper so the service can say "it worked" or "no, because ..." with an HTTP status,
// and the controller just forwards it.
public record Result<T>(T? Value, string? Error = null, int Status = 200)
{
    public bool IsOk => Error == null;
    public static Result<T> Success(T value) => new(value);
    public static Result<T> Fail(string error, int status) => new(default, error, status);
}

// ALL the queue rules live here (not in the controllers), so they are easy to read and test:
//   - taking a ticket (numbers, limits, one ticket per venue)
//   - the wait-time estimate
//   - calling the next customer, starting, finishing, no-shows
//   - telling people in real time
public class QueueService
{
    // A List (not an array): EF Core turns List.Contains(...) into a SQL "IN (...)" without any surprises.
    private static readonly List<TicketStatus> ActiveStatuses = new()
    {
        TicketStatus.Waiting, TicketStatus.Called, TicketStatus.Serving
    };

    private readonly AppDbContext _db;
    private readonly QueueNotifier _notifier;
    private readonly QueueOptions _options;

    public QueueService(AppDbContext db, QueueNotifier notifier, IOptions<QueueOptions> options)
    {
        _db = db;
        _notifier = notifier;
        _options = options.Value;
    }

    private static bool IsActive(TicketStatus s) =>
        s == TicketStatus.Waiting || s == TicketStatus.Called || s == TicketStatus.Serving;

    // SQL Server gives dates back without a timezone label. We mark them as UTC
    // so the browser shows the correct local time.
    private static DateTime U(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static DateTime? U(DateTime? d) => d.HasValue ? DateTime.SpecifyKind(d.Value, DateTimeKind.Utc) : null;

    // ------------------------------------------------------------------
    // 1) Taking a ticket
    // ------------------------------------------------------------------
    public async Task<Result<TicketDto>> JoinAsync(int venueId, int userId)
    {
        var venue = await _db.Venues.FirstOrDefaultAsync(v => v.Id == venueId);
        if (venue == null)
            return Result<TicketDto>.Fail("Venue not found.", 404);

        if (!venue.IsOpen)
            return Result<TicketDto>.Fail("This venue is closed right now.", 409);

        var day = AppClock.Today();

        // Tickets left over from a previous day are no longer valid.
        await _db.Tickets
            .Where(t => t.UserId == userId && t.ServiceDate < day && ActiveStatuses.Contains(t.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TicketStatus.Cancelled)
                .SetProperty(t => t.CompletedAt, (DateTime?)DateTime.UtcNow));

        // Rule: at most N active tickets in total, and only one per venue.
        var myActiveVenues = await _db.Tickets
            .Where(t => t.UserId == userId && t.ServiceDate == day && ActiveStatuses.Contains(t.Status))
            .Select(t => t.VenueId)
            .ToListAsync();

        if (myActiveVenues.Contains(venueId))
            return Result<TicketDto>.Fail("You already have a ticket in this queue.", 409);

        if (myActiveVenues.Count >= _options.MaxActiveTicketsPerUser)
            return Result<TicketDto>.Fail(
                $"You can hold at most {_options.MaxActiveTicketsPerUser} active tickets at the same time.", 409);

        // Rule: the queue has a maximum size.
        var inQueue = await _db.Tickets.CountAsync(t =>
            t.VenueId == venueId && t.ServiceDate == day && ActiveStatuses.Contains(t.Status));
        if (inQueue >= venue.MaxQueueSize)
            return Result<TicketDto>.Fail("This queue is full right now.", 409);

        // The next number = the biggest number today + 1.
        // If two people press the button at the same moment they may compute the same number;
        // the unique index in the database rejects the second one and we simply try again.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var last = await _db.Tickets
                .Where(t => t.VenueId == venueId && t.ServiceDate == day)
                .MaxAsync(t => (int?)t.Number) ?? 0;

            var ticket = new Ticket
            {
                VenueId = venueId,
                UserId = userId,
                ServiceDate = day,
                Number = last + 1,
                Status = TicketStatus.Waiting,
                JoinedAt = DateTime.UtcNow
            };

            _db.Tickets.Add(ticket);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                _db.Entry(ticket).State = EntityState.Detached;
                continue;
            }

            await AfterChangeAsync(venue);
            return Result<TicketDto>.Success(await BuildTicketDtoAsync(ticket, venue));
        }

        return Result<TicketDto>.Fail("Could not take a ticket, please try again.", 409);
    }

    // ------------------------------------------------------------------
    // 2) The customer leaves
    // ------------------------------------------------------------------
    public async Task<Result<TicketDto>> CancelAsync(int ticketId, int userId)
    {
        var ticket = await _db.Tickets.Include(t => t.Venue).FirstOrDefaultAsync(t => t.Id == ticketId);
        if (ticket == null || ticket.UserId != userId)
            return Result<TicketDto>.Fail("Ticket not found.", 404);

        if (ticket.Status != TicketStatus.Waiting && ticket.Status != TicketStatus.Called)
            return Result<TicketDto>.Fail("Only a waiting or called ticket can be cancelled.", 409);

        ticket.Status = TicketStatus.Cancelled;
        ticket.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await AfterChangeAsync(ticket.Venue);
        return Result<TicketDto>.Success(await BuildTicketDtoAsync(ticket, ticket.Venue));
    }

    // ------------------------------------------------------------------
    // 3) Staff actions
    // ------------------------------------------------------------------

    // "Next please": calls the lowest waiting number.
    public async Task<Result<QueueEntryDto>> CallNextAsync(Venue venue)
    {
        var day = AppClock.Today();

        var busy = await _db.Tickets.AnyAsync(t =>
            t.VenueId == venue.Id && t.ServiceDate == day &&
            (t.Status == TicketStatus.Called || t.Status == TicketStatus.Serving));
        if (busy)
            return Result<QueueEntryDto>.Fail("Finish the current customer first.", 409);

        var next = await _db.Tickets
            .Include(t => t.User)
            .Where(t => t.VenueId == venue.Id && t.ServiceDate == day && t.Status == TicketStatus.Waiting)
            .OrderBy(t => t.Number)
            .FirstOrDefaultAsync();
        if (next == null)
            return Result<QueueEntryDto>.Fail("Nobody is waiting.", 404);

        next.Status = TicketStatus.Called;
        next.CalledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _notifier.NotifyUserAsync(
            next.UserId, "YourTurn",
            $"It's your turn at {venue.Name}! Please go to the counter within {venue.CallGraceMinutes} minutes.",
            venue.Id, next.Id);

        await AfterChangeAsync(venue);
        return Result<QueueEntryDto>.Success(ToEntry(next));
    }

    // Called -> Serving (the customer arrived), Serving -> Done, Called -> NoShow.
    public async Task<Result<TicketDto>> StaffMoveAsync(int ticketId, int staffId, TicketStatus from, TicketStatus to)
    {
        var ticket = await _db.Tickets.Include(t => t.Venue).FirstOrDefaultAsync(t => t.Id == ticketId);
        if (ticket == null)
            return Result<TicketDto>.Fail("Ticket not found.", 404);

        if (ticket.Venue.OwnerId != staffId)
            return Result<TicketDto>.Fail("This venue is not yours.", 403);

        if (ticket.Status != from)
            return Result<TicketDto>.Fail(
                $"Ticket {ticket.Number} is {ticket.Status}, but it must be {from} for this action.", 409);

        var now = DateTime.UtcNow;
        ticket.Status = to;
        if (to == TicketStatus.Serving)
            ticket.StartedAt = now;
        else
            ticket.CompletedAt = now;   // Done or NoShow

        await _db.SaveChangesAsync();

        if (to == TicketStatus.NoShow)
        {
            await _notifier.NotifyUserAsync(
                ticket.UserId, "NoShow",
                $"You missed your turn at {ticket.Venue.Name}. Your ticket was released.",
                ticket.VenueId, ticket.Id);
        }

        await AfterChangeAsync(ticket.Venue);
        return Result<TicketDto>.Success(await BuildTicketDtoAsync(ticket, ticket.Venue));
    }

    // ------------------------------------------------------------------
    // 4) After ANY change: warn people who are almost up, then tell everybody to refresh.
    // ------------------------------------------------------------------
    public async Task AfterChangeAsync(Venue venue)
    {
        var day = AppClock.Today();

        var active = await _db.Tickets
            .Where(t => t.VenueId == venue.Id && t.ServiceDate == day && ActiveStatuses.Contains(t.Status))
            .OrderBy(t => t.Number)
            .ToListAsync();

        // The position in this list = how many people are ahead of that ticket.
        var toWarn = new List<(Ticket Ticket, int Ahead)>();
        for (var ahead = 0; ahead < active.Count; ahead++)
        {
            var t = active[ahead];
            if (t.Status == TicketStatus.Waiting && !t.AlmostNotified && ahead <= _options.AlmostTurnThreshold)
            {
                t.AlmostNotified = true;
                toWarn.Add((t, ahead));
            }
        }

        if (toWarn.Count > 0)
            await _db.SaveChangesAsync();

        foreach (var (ticket, ahead) in toWarn)
        {
            var message = ahead == 0
                ? $"You're next at {venue.Name}! Get ready."
                : $"Almost your turn at {venue.Name}: {ahead} {(ahead == 1 ? "person" : "people")} ahead of you.";

            await _notifier.NotifyUserAsync(ticket.UserId, "AlmostYourTurn", message, venue.Id, ticket.Id);
        }

        await _notifier.VenueChangedAsync(venue.Id);
    }

    // Runs every few seconds from QueueMaintenanceService.
    public async Task RunMaintenanceAsync()
    {
        var now = DateTime.UtcNow;
        var day = AppClock.Today();

        // Tickets from yesterday that nobody closed.
        await _db.Tickets
            .Where(t => t.ServiceDate < day && ActiveStatuses.Contains(t.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TicketStatus.Cancelled)
                .SetProperty(t => t.CompletedAt, (DateTime?)now));

        // Called customers who did not arrive within the venue's grace period become NoShow.
        var called = await _db.Tickets
            .Include(t => t.Venue)
            .Where(t => t.Status == TicketStatus.Called && t.CalledAt != null)
            .ToListAsync();

        var expired = called
            .Where(t => t.CalledAt!.Value.AddMinutes(t.Venue.CallGraceMinutes) < now)
            .ToList();

        if (expired.Count == 0)
            return;

        foreach (var t in expired)
        {
            t.Status = TicketStatus.NoShow;
            t.CompletedAt = now;
        }

        await _db.SaveChangesAsync();

        foreach (var byVenue in expired.GroupBy(t => t.VenueId))
        {
            var venue = byVenue.First().Venue;

            foreach (var t in byVenue)
            {
                await _notifier.NotifyUserAsync(
                    t.UserId, "NoShow",
                    $"You did not arrive in time at {venue.Name}. Your ticket was released.",
                    venue.Id, t.Id);
            }

            await AfterChangeAsync(venue);
        }
    }

    // ------------------------------------------------------------------
    // 5) Reading: numbers and estimates
    // ------------------------------------------------------------------

    // The average minutes one customer takes at the counter, learned from today's finished tickets
    // (the last 10). Before the first finished ticket we use the venue's default.
    private async Task<Dictionary<int, int>> AvgMinutesAsync(IReadOnlyCollection<Venue> venues, DateOnly day)
    {
        var ids = venues.Select(v => v.Id).ToList();

        var finished = await _db.Tickets
            .Where(t => ids.Contains(t.VenueId) && t.ServiceDate == day &&
                        t.Status == TicketStatus.Done && t.StartedAt != null && t.CompletedAt != null)
            .Select(t => new { t.VenueId, t.StartedAt, t.CompletedAt })
            .ToListAsync();

        var result = new Dictionary<int, int>();
        foreach (var v in venues)
        {
            var minutes = finished
                .Where(f => f.VenueId == v.Id)
                .OrderByDescending(f => f.CompletedAt)
                .Take(10)
                .Select(f => (f.CompletedAt!.Value - f.StartedAt!.Value).TotalMinutes)
                .ToList();

            result[v.Id] = minutes.Count == 0
                ? v.DefaultServiceMinutes
                : Math.Max(1, (int)Math.Round(minutes.Average()));
        }

        return result;
    }

    public async Task<List<VenueDto>> BuildVenueDtosAsync(IReadOnlyCollection<Venue> venues)
    {
        var result = new List<VenueDto>();
        if (venues.Count == 0)
            return result;

        var day = AppClock.Today();
        var ids = venues.Select(v => v.Id).ToList();

        var active = await _db.Tickets
            .Where(t => ids.Contains(t.VenueId) && t.ServiceDate == day && ActiveStatuses.Contains(t.Status))
            .Select(t => new { t.VenueId, t.Number, t.Status })
            .ToListAsync();

        var avg = await AvgMinutesAsync(venues, day);

        foreach (var v in venues)
        {
            var mine = active.Where(a => a.VenueId == v.Id).ToList();
            var people = mine.Count;

            int? nowServing = mine
                .Where(a => a.Status == TicketStatus.Called || a.Status == TicketStatus.Serving)
                .Select(a => (int?)a.Number)
                .FirstOrDefault();

            result.Add(new VenueDto(
                v.Id, v.Name, v.Category, v.City, v.Address, v.Latitude, v.Longitude,
                v.IsOpen, v.CallGraceMinutes, v.MaxQueueSize,
                people, nowServing, avg[v.Id], people * avg[v.Id]));
        }

        return result;
    }

    public async Task<VenueDto> BuildVenueDtoAsync(Venue venue) =>
        (await BuildVenueDtosAsync(new[] { venue }))[0];

    public async Task<TicketDto> BuildTicketDtoAsync(Ticket t, Venue v)
    {
        var day = t.ServiceDate;
        var peopleAhead = 0;

        if (t.Status == TicketStatus.Waiting)
        {
            // Everyone active with a smaller number is ahead of me.
            peopleAhead = await _db.Tickets.CountAsync(x =>
                x.VenueId == v.Id && x.ServiceDate == day && x.Number < t.Number &&
                ActiveStatuses.Contains(x.Status));
        }

        var avg = (await AvgMinutesAsync(new[] { v }, day))[v.Id];

        var nowServing = await _db.Tickets
            .Where(x => x.VenueId == v.Id && x.ServiceDate == day &&
                        (x.Status == TicketStatus.Called || x.Status == TicketStatus.Serving))
            .Select(x => (int?)x.Number)
            .FirstOrDefaultAsync();

        var eta = t.Status == TicketStatus.Waiting ? peopleAhead * avg : 0;

        return new TicketDto(
            t.Id, v.Id, v.Name, t.Number, t.Status, U(t.JoinedAt), U(t.CalledAt),
            peopleAhead, eta, nowServing, v.CallGraceMinutes);
    }

    public async Task<List<TicketDto>> MyTicketsTodayAsync(int userId)
    {
        var day = AppClock.Today();

        var tickets = await _db.Tickets
            .Include(t => t.Venue)
            .Where(t => t.UserId == userId && t.ServiceDate == day)
            .OrderByDescending(t => t.JoinedAt)
            .ToListAsync();

        var result = new List<TicketDto>();
        foreach (var t in tickets)
            result.Add(await BuildTicketDtoAsync(t, t.Venue));

        return result;
    }

    public async Task<StaffQueueDto> GetStaffQueueAsync(Venue venue)
    {
        var day = AppClock.Today();

        var tickets = await _db.Tickets
            .Include(t => t.User)
            .Where(t => t.VenueId == venue.Id && t.ServiceDate == day)
            .OrderBy(t => t.Number)
            .ToListAsync();

        var current = tickets.FirstOrDefault(t =>
            t.Status == TicketStatus.Called || t.Status == TicketStatus.Serving);

        var waiting = tickets
            .Where(t => t.Status == TicketStatus.Waiting)
            .Select(ToEntry)
            .ToList();

        return new StaffQueueDto(
            await BuildVenueDtoAsync(venue),
            current == null ? null : ToEntry(current),
            waiting,
            tickets.Count(t => t.Status == TicketStatus.Done),
            tickets.Count(t => t.Status == TicketStatus.NoShow),
            tickets.Count(t => t.Status == TicketStatus.Cancelled));
    }

    private static QueueEntryDto ToEntry(Ticket t) => new(
        t.Id, t.Number, t.Status, t.User.FullName, t.User.Phone,
        U(t.JoinedAt), U(t.CalledAt), U(t.StartedAt));
}
