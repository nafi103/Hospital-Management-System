namespace HospitalManagementSystem.Models
{
    public class Doctor
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Specialization { get; set; }
        public string ContactInfo { get; set; }
        public decimal ConsultationFee { get; set; }
    }
}