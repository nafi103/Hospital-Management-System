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
        Scheduled = 0,
        Completed = 1,
        Cancelled = 2,
        InConsultation = 3
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

    public enum TriagePriority
    {
        Normal,
        Urgent,
        Emergency
    }

    // ACVPU consciousness scale, used by NEWS2 triage scoring.
    public enum ConsciousnessLevel
    {
        Alert,
        Voice,
        Pain,
        Unresponsive
    }

    public enum AllergySeverity
    {
        Mild,
        Moderate,
        Severe,
        LifeThreatening
    }

    public enum MedicationRoute
    {
        Oral,
        Topical,
        Injection,
        Drops,
        Inhaled,
        Other
    }

    public enum DoseUnit
    {
        Tablet,
        Capsule,
        Ml,
        Drops,
        Puff,
        Application,
        Other
    }
}
