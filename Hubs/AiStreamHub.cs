using Microsoft.AspNetCore.SignalR;

namespace HospitalManagementSystem.Hubs
{
    public class AiStreamHub : Hub
    {
        // The browser generates a streamId before it kicks off generation and joins
        // this group first, so it never misses a chunk to a race against the POST.
        public async Task JoinStream(string streamId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"AiStream_{streamId}");
        }
    }
}
