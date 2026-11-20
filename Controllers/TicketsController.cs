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
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly QueueService _queue;

    public TicketsController(AppDbContext db, QueueService queue)
    {
        _db = db;
        _queue = queue;
    }

    // My tickets for today (active ones and finished ones).
    [HttpGet("mine")]
    public async Task<ActionResult<List<TicketDto>>> Mine()
    {
        return await _queue.MyTicketsTodayAsync(User.GetUserId());
    }

    // One of my tickets, with live position and estimated wait.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TicketDto>> Get(int id)
    {
        var ticket = await _db.Tickets.Include(t => t.Venue).FirstOrDefaultAsync(t => t.Id == id);

        // Other people's tickets look like they do not exist.
        if (ticket == null || ticket.UserId != User.GetUserId())
            return NotFound();

        return await _queue.BuildTicketDtoAsync(ticket, ticket.Venue);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<TicketDto>> Cancel(int id)
    {
        var result = await _queue.CancelAsync(id, User.GetUserId());
        return Forward(result);
    }

    // ---------- Staff only ----------

    // The called customer arrived at the counter.
    [HttpPost("{id:int}/start")]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public async Task<ActionResult<TicketDto>> Start(int id) =>
        Forward(await _queue.StaffMoveAsync(id, User.GetUserId(), TicketStatus.Called, TicketStatus.Serving));

    // Finished serving this customer.
    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public async Task<ActionResult<TicketDto>> Complete(int id) =>
        Forward(await _queue.StaffMoveAsync(id, User.GetUserId(), TicketStatus.Serving, TicketStatus.Done));

    // The called customer did not come.
    [HttpPost("{id:int}/no-show")]
    [Authorize(Roles = nameof(UserRole.Staff))]
    public async Task<ActionResult<TicketDto>> NoShow(int id) =>
        Forward(await _queue.StaffMoveAsync(id, User.GetUserId(), TicketStatus.Called, TicketStatus.NoShow));

    private ActionResult<TicketDto> Forward(Result<TicketDto> result)
    {
        if (!result.IsOk)
            return StatusCode(result.Status, new { message = result.Error });

        return result.Value!;
    }
}
