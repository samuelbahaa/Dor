using Dor.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Dor.Api.Services;

// The one place that talks to browsers in real time.
public class QueueNotifier
{
    private readonly IHubContext<QueueHub> _hub;

    public QueueNotifier(IHubContext<QueueHub> hub)
    {
        _hub = hub;
    }

    public static string Group(int venueId) => $"venue-{venueId}";

    // Tells everybody watching this venue: "something changed, refresh your numbers".
    public Task VenueChangedAsync(int venueId) =>
        _hub.Clients.Group(Group(venueId)).SendAsync("QueueChanged", venueId);

    // A personal message for one logged-in user (all of their open tabs/devices).
    public Task NotifyUserAsync(int userId, string type, string message, int venueId, int ticketId) =>
        _hub.Clients.User(userId.ToString()).SendAsync("Notification", new { type, message, venueId, ticketId });
}
