namespace Dor.Api.Options;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "Dor";
    public string Audience { get; set; } = "DorClients";
    public int ExpiryMinutes { get; set; } = 240;
}

public class QueueOptions
{
    // Stops one person from holding places in many queues at the same time.
    public int MaxActiveTicketsPerUser { get; set; } = 2;

    // Send the "almost your turn" notification when this many people (or fewer) are ahead.
    public int AlmostTurnThreshold { get; set; } = 2;
}
