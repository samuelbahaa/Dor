namespace Dor.Api.Models;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Phone { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Customer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Venue> Venues { get; set; } = new();     // venues this staff member owns
    public List<Ticket> Tickets { get; set; } = new();   // tickets this customer took
}
