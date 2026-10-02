namespace HospitalManagementSystem.Models
{
    public class BkashSettings
    {
        public string AppKey { get; set; } = string.Empty;
        public string AppSecret { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://tokenized.sandbox.bka.sh/v1.2.0-beta";
    }
}
