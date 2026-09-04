using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    // A pure implementation of the NEWS2 (National Early Warning Score 2) triage
    // algorithm, Scale 1 only - the standard scale for most patients. Scale 2 (used for
    // patients with COPD, where a lower target SpO2 is expected) needs a target-SpO2
    // field the PatientVital model doesn't carry, so it's intentionally out of scope
    // here rather than silently approximated.
    //
    // Each of the seven parameters below scores 0-3; the sum is the aggregate NEWS2
    // score, which this maps onto the app's three-tier TriagePriority using the
    // standard NHS clinical response bands: a score of 7+ is high risk, 5-6 (or any
    // single parameter scoring the maximum 3) is medium risk, everything else is low.
    public static class News2Calculator
    {
        public readonly record struct Result(int Score, TriagePriority Priority);

        public static Result Calculate(
            int respiratoryRate,
            decimal spo2,
            bool onSupplementalOxygen,
            decimal temperature,
            int systolicBp,
            int heartRate,
            ConsciousnessLevel consciousness)
        {
            int respScore = respiratoryRate switch
            {
                <= 8 => 3,
                <= 11 => 1,
                <= 20 => 0,
                <= 24 => 2,
                _ => 3
            };

            int spo2Score = spo2 switch
            {
                <= 91 => 3,
                <= 93 => 2,
                <= 95 => 1,
                _ => 0
            };

            int oxygenScore = onSupplementalOxygen ? 2 : 0;

            int tempScore = temperature switch
            {
                <= 35.0m => 3,
                <= 36.0m => 1,
                <= 38.0m => 0,
                <= 39.0m => 1,
                _ => 2
            };

            int bpScore = systolicBp switch
            {
                <= 90 => 3,
                <= 100 => 2,
                <= 110 => 1,
                <= 219 => 0,
                _ => 3
            };

            int hrScore = heartRate switch
            {
                <= 40 => 3,
                <= 50 => 1,
                <= 90 => 0,
                <= 110 => 1,
                <= 130 => 2,
                _ => 3
            };

            int consciousnessScore = consciousness == ConsciousnessLevel.Alert ? 0 : 3;

            var scores = new[] { respScore, spo2Score, oxygenScore, tempScore, bpScore, hrScore, consciousnessScore };
            int total = 0;
            foreach (var s in scores) total += s;
            bool anyMaxed = System.Array.Exists(scores, s => s == 3);

            var priority = total >= 7 ? TriagePriority.Emergency
                : (total >= 5 || anyMaxed) ? TriagePriority.Urgent
                : TriagePriority.Normal;

            return new Result(total, priority);
        }
    }
}
