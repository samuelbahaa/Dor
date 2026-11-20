namespace Dor.Api.Models;

public enum UserRole
{
    Customer,   // joins queues
    Staff       // owns venues and serves the queue
}

public enum VenueCategory
{
    Clinic,
    Government,
    Bank,
    Pharmacy,
    Salon,
    Other
}

// The life of one ticket:
//   Waiting -> Called -> Serving -> Done
//   Waiting/Called -> Cancelled   (customer left)
//   Called -> NoShow              (customer did not show up in time)
public enum TicketStatus
{
    Waiting,
    Called,
    Serving,
    Done,
    NoShow,
    Cancelled
}
