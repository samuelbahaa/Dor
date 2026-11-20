using Dor.Api.Data;
using Dor.Api.Dtos;
using Dor.Api.Models;
using Dor.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VenuesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly QueueService _queue;

    public VenuesController(AppDbContext db, QueueService queue)
    {
        _db = db;
        _queue = queue;
    }

    // ---------- Public: anyone can look at the live queues ----------

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<VenueDto>>> List(
        [FromQuery] string? city, [FromQuery] VenueCategory? category, [FromQuery] string? search)
    {
        var query = _db.Venues.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(v => v.City == city.Trim());

        if (category != null)
            query = query.Where(v => v.Category == category);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(v => v.Name.Contains(s) || v.Address.Contains(s));
        }

        var venues = await query.OrderBy(v => v.Name).ToListAsync();
        return await _queue.BuildVenueDtosAsync(venues);
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<VenueDto>> Get(int id)
    {
        var venue = await _db.Venues.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
        if (venue == null)
            return NotFound();

        return await _queue.BuildVenueDtoAsync(venue);
    }

    // ---------- Customer ----------

    [HttpPost("{id:int}/join")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<TicketDto>> Join(int id)
    {
        var result = await _queue.JoinAsync(id, User.GetUserId());
        if (!result.IsOk)
            return StatusCode(result.Status, new { message = result.Error });

        return result.Value!;
    }

    // ---------- Staff (venue owner) ----------

    [HttpGet("mine")]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public async Task<ActionResult<List<VenueDto>>> Mine()
    {
        var me = User.GetUserId();
        var venues = await _db.Venues.AsNoTracking()
            .Where(v => v.OwnerId == me)
            .OrderBy(v => v.Name)
            .ToListAsync();

        return await _queue.BuildVenueDtosAsync(venues);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public async Task<ActionResult<VenueDto>> Create(CreateVenueDto dto)
    {
        var venue = new Venue
        {
            Name = dto.Name.Trim(),
            Category = dto.Category,
            City = dto.City.Trim(),
            Address = dto.Address.Trim(),
            DefaultServiceMinutes = dto.DefaultServiceMinutes,
            MaxQueueSize = dto.MaxQueueSize,
            CallGraceMinutes = dto.CallGraceMinutes,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            OwnerId = User.GetUserId()
        };

        _db.Venues.Add(venue);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = venue.Id }, await _queue.BuildVenueDtoAsync(venue));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public async Task<ActionResult<VenueDto>> Update(int id, UpdateVenueDto dto)
    {
        var (venue, error) = await GetOwnedVenueAsync(id);
        if (venue == null)
            return error!;

        // Only the fields that were sent are changed.
        if (dto.Name != null) venue.Name = dto.Name.Trim();
        if (dto.Category != null) venue.Category = dto.Category.Value;
        if (dto.City != null) venue.City = dto.City.Trim();
        if (dto.Address != null) venue.Address = dto.Address.Trim();
        if (dto.DefaultServiceMinutes != null) venue.DefaultServiceMinutes = dto.DefaultServiceMinutes.Value;
        if (dto.MaxQueueSize != null) venue.MaxQueueSize = dto.MaxQueueSize.Value;
        if (dto.CallGraceMinutes != null) venue.CallGraceMinutes = dto.CallGraceMinutes.Value;
        if (dto.Latitude != null) venue.Latitude = dto.Latitude;
        if (dto.Longitude != null) venue.Longitude = dto.Longitude;

        await _db.SaveChangesAsync();
        await _queue.AfterChangeAsync(venue);
        return await _queue.BuildVenueDtoAsync(venue);
    }

    [HttpPost("{id:int}/open")]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public Task<ActionResult<VenueDto>> Open(int id) => SetOpenAsync(id, true);

    [HttpPost("{id:int}/close")]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public Task<ActionResult<VenueDto>> Close(int id) => SetOpenAsync(id, false);

    // The staff console: who is at the counter, who is waiting, and today's numbers.
    [HttpGet("{id:int}/queue")]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public async Task<ActionResult<StaffQueueDto>> Queue(int id)
    {
        var (venue, error) = await GetOwnedVenueAsync(id);
        if (venue == null)
            return error!;

        return await _queue.GetStaffQueueAsync(venue);
    }

    [HttpPost("{id:int}/call-next")]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public async Task<ActionResult<QueueEntryDto>> CallNext(int id)
    {
        var (venue, error) = await GetOwnedVenueAsync(id);
        if (venue == null)
            return error!;

        var result = await _queue.CallNextAsync(venue);
        if (!result.IsOk)
            return StatusCode(result.Status, new { message = result.Error });

        return result.Value!;
    }

    // ---------- helpers ----------

    private async Task<ActionResult<VenueDto>> SetOpenAsync(int id, bool open)
    {
        var (venue, error) = await GetOwnedVenueAsync(id);
        if (venue == null)
            return error!;

        venue.IsOpen = open;
        await _db.SaveChangesAsync();
        await _queue.AfterChangeAsync(venue);
        return await _queue.BuildVenueDtoAsync(venue);
    }

    // Loads the venue only if the logged-in staff member owns it.
    private async Task<(Venue? Venue, ActionResult? Error)> GetOwnedVenueAsync(int id)
    {
        var venue = await _db.Venues.FirstOrDefaultAsync(v => v.Id == id);
        if (venue == null)
            return (null, NotFound(new { message = "Venue not found." }));

        if (venue.OwnerId != User.GetUserId())
            return (null, StatusCode(403, new { message = "This venue is not yours." }));

        return (venue, null);
    }
}
