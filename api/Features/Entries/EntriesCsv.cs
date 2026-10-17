using System.Globalization;
using System.Text;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Features.Entries;

// TBL-06 CSV export (also a simple backup, NFR-05). RFC 4180: every field is quoted, quotes are
// doubled and lines end with CRLF. A UTF-8 BOM lets Excel read names that are not ASCII.
public static class EntriesCsv
{
    // One entry with its names looked up. The amount is in minor units (R-MON-01).
    public sealed record Row(DateOnly Date, string Month, string Category, string SubCategory, long AmountMinor, string BankAccount, string Note);

    // Columns in the TBL-01 order.
    public static byte[] Write(IEnumerable<Row> rows, string currency)
    {
        var text = new StringBuilder();
        Line(text, ["Date", "Month", "Category", "Sub-category", $"Amount ({currency})", "Bank account", "Note"]);
        foreach (var row in rows)
        {
            Line(text,
            [
                FirestoreDocuments.ToDateString(row.Date),
                Safe(row.Month),
                Safe(row.Category),
                Safe(row.SubCategory),
                // 125.50: two decimals, a dot, no thousands separator and no currency.
                Money.FromMinor(row.AmountMinor).ToString("0.00", CultureInfo.InvariantCulture),
                Safe(row.BankAccount),
                Safe(row.Note),
            ]);
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    // OWASP "CSV Injection": text that a spreadsheet would run as a formula gets a leading quote.
    public static string Safe(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static void Line(StringBuilder text, IEnumerable<string> fields) =>
        text.AppendJoin(',', fields.Select(field => "\"" + field.Replace("\"", "\"\"") + "\"")).Append("\r\n");
}
