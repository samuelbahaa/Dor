# Dor (دور) - your place in line

Dor is a virtual queue. People take a number from their phone, watch their position move **in real time**, and arrive only when it is their turn. Venue staff run the line from a simple console.

> Portfolio project. All venues and people in the demo data are fictional.

## The problem

Clinics, government offices, banks and pharmacies in Egypt run on physical queues. People lose hours standing in line without knowing how long it will take, and staff have no view of how busy they are.

## The solution

- **Customers** pick a venue, see its live wait, take a ticket, and get alerted when they are almost up and when they are called.
- **Staff** open a queue, call the next customer, mark arrival, completion or no-show, and see the day's numbers.
- **The system** learns how long people really take at each counter and uses that to estimate the wait.

## Engineering highlights

| Topic | What was done |
|---|---|
| Real time | **SignalR** pushes queue changes and personal alerts to browsers; the page falls back to polling if the connection drops |
| Correctness under concurrency | Ticket numbers are protected by a unique index `(Venue, Day, Number)`; a **filtered unique index** guarantees one active ticket per person per venue; the service retries on a race |
| Background work | A hosted `BackgroundService` releases called customers who never arrive (no-shows) and cancels stale tickets from previous days |
| Smart estimate | Average service time is learned from the last 10 finished tickets of the day, with a per-venue default until data exists |
| Fair use | Max active tickets per person, max queue size per venue |
| Privacy | The public sees only counts and numbers. Names and phones are visible only to the staff member who owns that venue |
| Auth | JWT bearer, BCrypt password hashing, role-based endpoints (Customer / Staff), hub token via query string accepted only for `/hubs` |
| Timezone | "Today" is calculated in Egypt time so numbers restart at local midnight |
| Stack | ASP.NET Core 10, EF Core 10 + SQL Server LocalDB (migrations), OpenAPI + Scalar, vanilla JS front end |

## Run it locally

Requirements: .NET 10 SDK and SQL Server LocalDB (installed with Visual Studio).

```bash
git clone https://github.com/samuelbahaa/Dor.git
cd Dor
dotnet user-secrets set "Jwt:Key" "put-a-long-random-string-of-32-or-more-characters-here"
dotnet run
```

Then open http://localhost:5218 (the app) or http://localhost:5218/scalar (API docs).
In Development the database is created and filled with demo data automatically.

### Demo accounts (password `Demo@12345`)

| Role | Email |
|---|---|
| Customer | `cust1@demo.dor` ... `cust20@demo.dor` (cust1 and cust2 have no tickets yet) |
| Staff | `staff1@demo.dor` ... `staff5@demo.dor` (each runs one venue) |

Tip: open two browser windows, one as `staff1` and one as a customer, and watch the customer's ticket update live when the staff presses "Call next".

## API

| Method | Route | Who | What |
|---|---|---|---|
| POST | `/api/auth/register`, `/api/auth/login` | anyone | account + JWT |
| GET | `/api/venues` | anyone | live queues (`city`, `category`, `search`) |
| POST | `/api/venues/{id}/join` | customer | take a ticket |
| GET | `/api/tickets/mine` | logged in | my tickets today with position and wait |
| POST | `/api/tickets/{id}/cancel` | ticket owner | leave the queue |
| POST/PUT | `/api/venues`, `/api/venues/{id}` | staff | create / edit own venue |
| POST | `/api/venues/{id}/open`, `/close` | staff | open or close the queue |
| GET | `/api/venues/{id}/queue` | staff (owner) | counter + waiting list + day stats |
| POST | `/api/venues/{id}/call-next` | staff (owner) | call the next customer |
| POST | `/api/tickets/{id}/start`, `/complete`, `/no-show` | staff (owner) | move a ticket forward |
| WS | `/hubs/queue` | anyone | SignalR: `QueueChanged`, `Notification` |

## Roadmap

- SMS / push notifications when the browser is closed
- Multiple counters per venue
- Appointment slots next to walk-in queues
- Automated tests for the queue rules
