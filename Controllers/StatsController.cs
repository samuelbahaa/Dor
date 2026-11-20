using Dor.Api.Data;
using Dor.Api.Dtos;
using Dor.Api.Models;
using Dor.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dor.Api.Controllers;

// Public totals for the landing page. They are only numbers, so they leak no personal data.
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class StatsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StatsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<StatsDto>> Get()
    {
        var day = AppClock.Today();

        var venues = await _db.Venues.CountAsync();
        var today = await _db.Tickets.CountAsync(t => t.ServiceDate == day);
        var served = await _db.Tickets.CountAsync(t => t.ServiceDate == day && t.Status == TicketStatus.Done);
        var waiting = await _db.Tickets.CountAsync(t => t.ServiceDate == day && t.Status == TicketStatus.Waiting);

        return new StatsDto(venues, today, served, waiting);
    }
}
