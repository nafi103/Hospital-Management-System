using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Tests;

public class News2CalculatorTests
{
    // A fully "normal" baseline so each theory can vary exactly one parameter.
    private static News2Calculator.Result CalculateWithDefaults(
        int respiratoryRate = 16,
        decimal spo2 = 98,
        bool onSupplementalOxygen = false,
        decimal temperature = 37.0m,
        int systolicBp = 120,
        int heartRate = 70,
        ConsciousnessLevel consciousness = ConsciousnessLevel.Alert)
    {
        return News2Calculator.Calculate(
            respiratoryRate, spo2, onSupplementalOxygen, temperature, systolicBp, heartRate, consciousness);
    }

    [Theory]
    [InlineData(8, 3)]
    [InlineData(9, 1)]
    [InlineData(11, 1)]
    [InlineData(12, 0)]
    [InlineData(20, 0)]
    [InlineData(21, 2)]
    [InlineData(24, 2)]
    [InlineData(25, 3)]
    public void RespiratoryRate_ScoresAtBoundaries(int rate, int expectedContribution)
    {
        var baseline = CalculateWithDefaults();
        var result = CalculateWithDefaults(respiratoryRate: rate);
        Assert.Equal(baseline.Score + expectedContribution, result.Score);
    }

    [Theory]
    [InlineData(91, 3)]
    [InlineData(92, 2)]
    [InlineData(93, 2)]
    [InlineData(94, 1)]
    [InlineData(95, 1)]
    [InlineData(96, 0)]
    public void Spo2_ScoresAtBoundaries(decimal spo2, int expectedContribution)
    {
        var baseline = CalculateWithDefaults();
        var result = CalculateWithDefaults(spo2: spo2);
        Assert.Equal(baseline.Score + expectedContribution, result.Score);
    }

    [Theory]
    [InlineData(35.0, 3)]
    [InlineData(35.1, 1)]
    [InlineData(36.0, 1)]
    [InlineData(36.1, 0)]
    [InlineData(38.0, 0)]
    [InlineData(38.1, 1)]
    [InlineData(39.0, 1)]
    [InlineData(39.1, 2)]
    public void Temperature_ScoresAtBoundaries(double temperature, int expectedContribution)
    {
        var baseline = CalculateWithDefaults();
        var result = CalculateWithDefaults(temperature: (decimal)temperature);
        Assert.Equal(baseline.Score + expectedContribution, result.Score);
    }

    [Theory]
    [InlineData(90, 3)]
    [InlineData(91, 2)]
    [InlineData(100, 2)]
    [InlineData(101, 1)]
    [InlineData(110, 1)]
    [InlineData(111, 0)]
    [InlineData(219, 0)]
    [InlineData(220, 3)]
    public void SystolicBp_ScoresAtBoundaries(int systolic, int expectedContribution)
    {
        var baseline = CalculateWithDefaults();
        var result = CalculateWithDefaults(systolicBp: systolic);
        Assert.Equal(baseline.Score + expectedContribution, result.Score);
    }

    [Theory]
    [InlineData(40, 3)]
    [InlineData(41, 1)]
    [InlineData(50, 1)]
    [InlineData(51, 0)]
    [InlineData(90, 0)]
    [InlineData(91, 1)]
    [InlineData(110, 1)]
    [InlineData(111, 2)]
    [InlineData(130, 2)]
    [InlineData(131, 3)]
    public void HeartRate_ScoresAtBoundaries(int heartRate, int expectedContribution)
    {
        var baseline = CalculateWithDefaults();
        var result = CalculateWithDefaults(heartRate: heartRate);
        Assert.Equal(baseline.Score + expectedContribution, result.Score);
    }

    [Fact]
    public void SupplementalOxygen_AddsExactlyTwo()
    {
        var baseline = CalculateWithDefaults();
        var result = CalculateWithDefaults(onSupplementalOxygen: true);
        Assert.Equal(baseline.Score + 2, result.Score);
    }

    [Theory]
    [InlineData(ConsciousnessLevel.Alert, 0)]
    [InlineData(ConsciousnessLevel.Voice, 3)]
    [InlineData(ConsciousnessLevel.Pain, 3)]
    [InlineData(ConsciousnessLevel.Unresponsive, 3)]
    public void Consciousness_ScoresAlertAsZeroAndEverythingElseAsThree(ConsciousnessLevel level, int expectedContribution)
    {
        var baseline = CalculateWithDefaults();
        var result = CalculateWithDefaults(consciousness: level);
        Assert.Equal(baseline.Score + expectedContribution, result.Score);
    }

    [Fact]
    public void AlertPatientWithNormalObservations_ScoresZeroAndIsNormal()
    {
        var result = CalculateWithDefaults();
        Assert.Equal(0, result.Score);
        Assert.Equal(TriagePriority.Normal, result.Priority);
    }

    [Fact]
    public void AggregateSevenOrAbove_IsEmergency()
    {
        // Two boundary hits that individually score less than 3 but sum to 7.
        var result = CalculateWithDefaults(respiratoryRate: 22, heartRate: 115, systolicBp: 95, temperature: 38.5m);
        Assert.True(result.Score >= 7, $"expected score >= 7, was {result.Score}");
        Assert.Equal(TriagePriority.Emergency, result.Priority);
    }

    [Fact]
    public void AggregateFiveOrSix_IsUrgent()
    {
        // Three parameters at +2 each (21 -> resp 2, 111 -> hr 2, 39.1 -> temp 2),
        // none individually maxed at 3, summing to 6 - inside the 5-6 Urgent band
        // on aggregate alone, distinct from the single-red-score rule below.
        var result = CalculateWithDefaults(respiratoryRate: 21, heartRate: 111, temperature: 39.1m);
        Assert.InRange(result.Score, 5, 6);
        Assert.Equal(TriagePriority.Urgent, result.Priority);
    }

    [Fact]
    public void SingleParameterScoringThree_EscalatesToUrgentEvenWithLowAggregate()
    {
        // The NHS "single red score" rule: SpO2 of 90 alone scores 3, with every
        // other observation normal, for an aggregate of 3 - below the 5-6 Urgent
        // band on total alone, but still Urgent because one parameter maxed out.
        var result = CalculateWithDefaults(spo2: 90);
        Assert.Equal(3, result.Score);
        Assert.Equal(TriagePriority.Urgent, result.Priority);
    }

    [Fact]
    public void SepticPatient_EscalatesToEmergency()
    {
        // A recognisable septic-shock picture: tachypnoeic, hypoxic on oxygen,
        // febrile, hypotensive, tachycardic, and confused.
        var result = News2Calculator.Calculate(
            respiratoryRate: 26,
            spo2: 90,
            onSupplementalOxygen: true,
            temperature: 39.5m,
            systolicBp: 85,
            heartRate: 135,
            consciousness: ConsciousnessLevel.Voice);

        Assert.Equal(TriagePriority.Emergency, result.Priority);
        Assert.True(result.Score >= 7);
    }
}
