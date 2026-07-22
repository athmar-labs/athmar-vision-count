using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AthmarLabs.VisionCount
{
    public static class CsvExportService
    {
        public static string BuildConfirmedSessionCsv(ScanSessionRecord session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (!session.Confirmed)
                throw new InvalidOperationException("Only a human-confirmed session may be exported.");
            if (session.Lines == null || session.Lines.Count == 0)
                throw new InvalidOperationException("The session has no count lines.");

            var builder = new StringBuilder();
            builder.AppendLine("session_id,started_at_utc,completed_at_utc,operator_reference,location_reference,sku,display_name,proposed_count,confirmed_count,manually_adjusted");

            foreach (var line in session.Lines.OrderBy(value => value.Sku, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(Escape(session.SessionId)).Append(',')
                    .Append(Escape(session.StartedAtUtc)).Append(',')
                    .Append(Escape(session.CompletedAtUtc)).Append(',')
                    .Append(Escape(session.OperatorReference)).Append(',')
                    .Append(Escape(session.LocationReference)).Append(',')
                    .Append(Escape(line.Sku)).Append(',')
                    .Append(Escape(line.DisplayName)).Append(',')
                    .Append(line.ProposedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(line.ConfirmedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(line.ManuallyAdjusted ? "true" : "false")
                    .AppendLine();
            }

            return builder.ToString();
        }

        private static string Escape(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
