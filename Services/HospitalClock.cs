using System;

namespace HospitalManagementSystem.Services
{
    /// <summary>
    /// Centralized hospital clock and timezone service.
    /// Standardizes all calendar day boundaries and local display times to the hospital's
    /// operational timezone (default: Asia/Dhaka, Bangladesh Standard Time, UTC+6),
    /// preventing UTC day-clipping and queue dropouts across early morning or late evening hours.
    /// </summary>
    public static class HospitalClock
    {
        public static TimeZoneInfo TimeZone { get; private set; } = ResolveTimeZone("Asia/Dhaka");

        public static void Initialize(string? timeZoneId)
        {
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                TimeZone = ResolveTimeZone(timeZoneId);
            }
        }

        private static TimeZoneInfo ResolveTimeZone(string primaryId)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(primaryId);
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Bangladesh Standard Time");
                }
                catch
                {
                    // Fallback to fixed UTC+6 (Bangladesh Standard Time)
                    return TimeZoneInfo.CreateCustomTimeZone(
                        "BST",
                        TimeSpan.FromHours(6),
                        "Bangladesh Standard Time",
                        "Bangladesh Standard Time");
                }
            }
        }

        /// <summary>
        /// Current date and time in the hospital's operational timezone.
        /// </summary>
        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);

        /// <summary>
        /// Current calendar date in the hospital's operational timezone (midnight, time component 00:00:00).
        /// </summary>
        public static DateTime Today => Now.Date;

        /// <summary>
        /// Given a local date (in hospital time), returns the start of that day (00:00:00) converted to UTC.
        /// </summary>
        public static DateTime GetStartOfDayUtc(DateTime localDate)
        {
            var unspecified = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZone);
        }

        /// <summary>
        /// Given a local date (in hospital time), returns the start of the next day (exclusive upper bound) converted to UTC.
        /// </summary>
        public static DateTime GetEndOfDayUtc(DateTime localDate)
        {
            return GetStartOfDayUtc(localDate.Date.AddDays(1));
        }

        /// <summary>
        /// Converts a UTC timestamp into the hospital's operational local time.
        /// </summary>
        public static DateTime ToHospitalLocal(DateTime utcDateTime)
        {
            var utc = utcDateTime.Kind == DateTimeKind.Utc
                ? utcDateTime
                : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZone);
        }
    }

    /// <summary>
    /// Extension methods for convenient hospital timezone conversions.
    /// </summary>
    public static class HospitalClockExtensions
    {
        public static DateTime ToHospitalTime(this DateTime utcDateTime) =>
            HospitalClock.ToHospitalLocal(utcDateTime);
    }
}
