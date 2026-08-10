using Microsoft.AspNetCore.SignalR;

namespace HospitalManagementSystem.Hubs
{
    public class NotificationHub : Hub
    {
        // Assistant clients will call this to join their assigned doctor's group
        public async Task JoinDoctorGroup(string doctorId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Doctor_{doctorId}");
        }
    }
}
