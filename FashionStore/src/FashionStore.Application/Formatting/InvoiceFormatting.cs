using System.Globalization;

namespace FashionStore.Application.Formatting;

/// <summary>
/// Shared money / date formatting for invoices so the HTML view, the printed view
/// and the PDF generator all render amounts identically. Amounts are formatted with
/// invariant grouping and two decimals; a small symbol map covers the common
/// currencies and everything else falls back to the ISO code suffix.
/// </summary>
public static class InvoiceFormatting
{
    private static readonly Dictionary<string, string> Symbols =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = "$",
            ["EUR"] = "€",
            ["GBP"] = "£",
            ["CAD"] = "C$",
            ["AUD"] = "A$",
            ["JPY"] = "¥"
        };

    public static string FormatMoney(decimal amount, string currency)
    {
        var value = amount.ToString("N2", CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(currency))
        {
            return value;
        }

        return Symbols.TryGetValue(currency.Trim(), out var symbol)
            ? $"{symbol}{value}"
            : $"{value} {currency.Trim().ToUpperInvariant()}";
    }

    public static string FormatDate(DateTime utcDate)
    {
        return utcDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
