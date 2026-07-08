namespace HospitalManagementSystem.Models
{
    public enum DepartmentType
    {
        Pharmacy,
        Consultation,
        CabinRent,
        Operation,
        General
    }

    public enum OperationStatus
    {
        Scheduled,
        InSurgery,
        Recovery,
        Completed
    }

    public enum AppointmentStatus
    {
        Scheduled,
        Completed,
        Cancelled
    }

    public enum PrescriptionStatus
    {
        PendingPharmacy,
        Dispensed
    }

    public enum BillStatus
    {
        Unpaid,
        PartiallyPaid,
        Paid
    }

    public enum BedCategory
    {
        ICU,
        GeneralWard,
        Cabin,
        VIPCabin
    }
}
