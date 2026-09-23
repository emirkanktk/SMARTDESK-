using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SmartDesk.API.Hubs;

[Authorize]
public class ReservationHub : Hub
{
}
