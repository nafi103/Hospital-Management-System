using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HospitalManagementSystem.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        // Doctor and Assistant clients call this to join their assigned doctor's group
        public async Task JoinDoctorGroup(string doctorId)
        {
            if (string.IsNullOrWhiteSpace(doctorId))
            {
                return;
            }

            var user = Context.User;
            if (user == null)
            {
                throw new HubException("Unauthorized.");
            }

            var userId = user.FindFirst("UserId")?.Value;
            var assignedDoctorId = user.FindFirst("AssignedDoctorId")?.Value;
            var isDoctor = user.IsInRole("Doctor");
            var isAssistant = user.IsInRole("Assistant");

            // Allow if:
            // 1. User is a Doctor and their own UserId matches doctorId
            // 2. User is an Assistant and their AssignedDoctorId matches doctorId
            bool isAuthorized = (isDoctor && userId == doctorId) ||
                                (isAssistant && assignedDoctorId == doctorId);

            if (!isAuthorized)
            {
                throw new HubException("Forbidden: Not authorized to join this doctor's notification group.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, $"Doctor_{doctorId}");
        }
    }
}
