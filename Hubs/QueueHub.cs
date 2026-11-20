using Dor.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Dor.Api.Hubs;

// SignalR keeps a live connection open to every browser, so the server can PUSH news
// ("the queue changed", "it's your turn") instead of the browser asking again and again.
//
// A browser says which venue it is watching. Everything else is sent by QueueNotifier.
public class QueueHub : Hub
{
    public Task SubscribeVenue(int venueId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, QueueNotifier.Group(venueId));

    public Task UnsubscribeVenue(int venueId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, QueueNotifier.Group(venueId));
}
