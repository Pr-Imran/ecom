using System.Globalization;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Invoices;
using FashionStore.Application.Formatting;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FashionStore.Infrastructure.Invoicing;

/// <summary>
/// Builds the A4 invoice PDF with QuestPDF. Content comes only from the immutable
/// order snapshot carried in <see cref="InvoiceDto"/>; the store branding block is
/// resolved from <see cref="InvoiceSettings"/>. The document uses a repeating
/// header/footer, keeps item rows together where possible and produces stable bytes
/// for the same input so regenerating an unchanged invoice yields an identical file.
/// </summary>
public interface IInvoicePdfGenerator
{
    byte[] Generate(InvoiceDto invoice);
}

public sealed class QuestPdfInvoiceGenerator : IInvoicePdfGenerator
{
    private const string Ink = "#111827";
    private const string Body = "#374151";
    private const string Muted = "#6B7280";
    private const string Faint = "#9CA3AF";
    private const string Hairline = "#E5E7EB";
    private const string Panel = "#F9FAFB";

    private static readonly QuestPDF.Infrastructure.Color Brand = QuestPDF.Helpers.Colors.Green.Darken4;

    private readonly InvoiceSettings _settings;

    static QuestPdfInvoiceGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.EnableCaching = false;
    }

    public QuestPdfInvoiceGenerator(IOptions<InvoiceSettings> settings)
    {
        _settings = settings.Value;
    }

    public byte[] Generate(InvoiceDto invoice)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(34);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(Body));
                page.Header().Element(header => ComposeHeader(header, invoice));
                page.Content().Element(content => ComposeContent(content, invoice));
                page.Footer().Element(footer => ComposeFooter(footer, invoice));
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = $"Invoice {invoice.InvoiceNumber}",
            Author = CompanyName,
            Subject = $"Invoice {invoice.InvoiceNumber} for order {invoice.PublicOrderNumber}",
            CreationDate = invoice.GeneratedAtUtc
        });

        return document.GeneratePdf();
    }

    // ---- Header (repeats on every page) ----

    private void ComposeHeader(IContainer container, InvoiceDto invoice)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(CompanyName).FontSize(18).Bold().FontColor(Ink);
                    if (!string.IsNullOrWhiteSpace(_settings.CompanyAddress))
                    {
                        left.Item().PaddingTop(4).Text(_settings.CompanyAddress).FontSize(9).FontColor(Muted);
                    }

                    if (!string.IsNullOrWhiteSpace(_settings.CompanyEmail))
                    {
                        left.Item().Text(_settings.CompanyEmail).FontSize(9).FontColor(Muted);
                    }

                    if (!string.IsNullOrWhiteSpace(_settings.CompanyPhone))
                    {
                        left.Item().Text(_settings.CompanyPhone).FontSize(9).FontColor(Muted);
                    }
                });

                row.ConstantItem(230).Column(right =>
                {
                    right.Item().AlignRight().Text("INVOICE").FontSize(21).Bold().FontColor(Ink);
                    right.Item().AlignRight().PaddingTop(5).Text($"#{invoice.InvoiceNumber}").FontSize(12).Bold().FontColor(Brand);
                    right.Item().AlignRight().PaddingTop(2).Text($"Order {invoice.PublicOrderNumber}").FontSize(9).FontColor(Muted);
                    right.Item().AlignRight().Text($"Issued {InvoiceFormatting.FormatDate(invoice.IssueDateUtc)}").FontSize(9).FontColor(Muted);
                    right.Item().AlignRight().Text($"Status {invoice.Status}").FontSize(9).FontColor(Muted);
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Hairline);
        });
    }

    // ---- Content ----

    private void ComposeContent(IContainer container, InvoiceDto invoice)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(block => ComposeAddressBlock(block, "BILL TO", invoice.BillingAddress));
                row.ConstantItem(24);
                row.RelativeItem().Element(block => ComposeAddressBlock(block, "SHIP TO", invoice.ShippingAddress));
            });

            column.Item().PaddingTop(16).Element(block => ComposeSummary(block, invoice));
            column.Item().PaddingTop(18).Element(block => ComposeItems(block, invoice));
            column.Item().PaddingTop(14).Element(block => ComposeTotals(block, invoice));

            if (invoice.Notes.Count > 0)
            {
                column.Item().PaddingTop(16).Element(block => ComposeNotes(block, invoice));
            }

            if (invoice.RefundReferences.Count > 0)
            {
                column.Item().PaddingTop(16).Element(block => ComposeRefunds(block, invoice));
            }

            if (!string.IsNullOrWhiteSpace(_settings.TaxId) || !string.IsNullOrWhiteSpace(_settings.RegistrationNumber))
            {
                column.Item().PaddingTop(18).Element(block => ComposeRegistration(block, invoice));
            }
        });
    }

    private void ComposeAddressBlock(IContainer container, string title, InvoiceAddressDto? address)
    {
        container.Background(Panel).Border(1).BorderColor(Hairline).Padding(12).Column(column =>
        {
            column.Item().Text(title).FontSize(9).Bold().FontColor(Muted);
            if (address is null)
            {
                column.Item().PaddingTop(4).Text("—").FontSize(9).FontColor(Faint);
                return;
            }

            column.Item().PaddingTop(5).Text(address.RecipientName).FontSize(10).SemiBold().FontColor(Ink);
            if (!string.IsNullOrWhiteSpace(address.Phone))
            {
                column.Item().Text(address.Phone).FontSize(9).FontColor(Muted);
            }

            column.Item().PaddingTop(2).Text(address.AddressLine1).FontSize(9);
            if (!string.IsNullOrWhiteSpace(address.AddressLine2))
            {
                column.Item().Text(address.AddressLine2).FontSize(9);
            }

            column.Item().Text(FormatCityLine(address)).FontSize(9);
            column.Item().Text(address.CountryCode).FontSize(9);
            if (!string.IsNullOrWhiteSpace(address.DeliveryInstructions))
            {
                column.Item().PaddingTop(3).Text($"Note: {address.DeliveryInstructions}").FontSize(8).FontColor(Muted);
            }
        });
    }

    private void ComposeSummary(IContainer container, InvoiceDto invoice)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(block => ComposeSummaryCell(block, "CUSTOMER", customerLines(invoice)));
            row.ConstantItem(24);
            row.RelativeItem().Element(block => ComposeSummaryCell(block, "PAYMENT", paymentLines(invoice)));
            row.ConstantItem(24);
            row.RelativeItem().Element(block => ComposeSummaryCell(block, "DELIVERY", deliveryLines(invoice)));
        });

        return;

        IEnumerable<(string Label, string Value)> customerLines(InvoiceDto invoice)
        {
            yield return ("Name", string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Guest" : invoice.CustomerName);
            if (!string.IsNullOrWhiteSpace(invoice.GuestEmail))
            {
                yield return ("Email", invoice.GuestEmail);
            }

            if (!string.IsNullOrWhiteSpace(invoice.GuestPhone))
            {
                yield return ("Phone", invoice.GuestPhone);
            }
        }

        IEnumerable<(string Label, string Value)> paymentLines(InvoiceDto invoice)
        {
            yield return ("Method", Humanize(invoice.PaymentMethodCode));
            yield return ("Status", invoice.PaymentStatus);
            yield return ("Paid", InvoiceFormatting.FormatMoney(invoice.PaidAmount, invoice.Currency));
            yield return ("Outstanding", InvoiceFormatting.FormatMoney(invoice.OutstandingAmount, invoice.Currency));
        }

        IEnumerable<(string Label, string Value)> deliveryLines(InvoiceDto invoice)
        {
            yield return ("Method", string.IsNullOrWhiteSpace(invoice.ShippingMethodName) ? "—" : invoice.ShippingMethodName);
            yield return ("Tracking", string.IsNullOrWhiteSpace(invoice.TrackingNumber) ? "—" : invoice.TrackingNumber);
        }
    }

    private static void ComposeSummaryCell(IContainer container, string title, IEnumerable<(string Label, string Value)> lines)
    {
        container.Background(Panel).Border(1).BorderColor(Hairline).Padding(12).Column(column =>
        {
            column.Item().Text(title).FontSize(9).Bold().FontColor(Muted);
            foreach (var (label, value) in lines)
            {
                column.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text(label).FontSize(9).FontColor(Muted);
                    row.RelativeItem().AlignRight().Text(value).FontSize(9).SemiBold().FontColor(Ink);
                });
            }
        });
    }

    private void ComposeItems(IContainer container, InvoiceDto invoice)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3.2f);
                columns.RelativeColumn(1.4f);
                columns.RelativeColumn(1.6f);
                columns.RelativeColumn(0.7f);
                columns.RelativeColumn(1.1f);
                columns.RelativeColumn(1.1f);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Item").FontColor(Colors.White).SemiBold();
                header.Cell().Element(HeaderCell).Text("SKU").FontColor(Colors.White).SemiBold();
                header.Cell().Element(HeaderCell).Text("Colour / Size").FontColor(Colors.White).SemiBold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Qty").FontColor(Colors.White).SemiBold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Unit").FontColor(Colors.White).SemiBold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Total").FontColor(Colors.White).SemiBold();
            });

            foreach (var item in invoice.Items)
            {
                var variant = ComposeVariantLabel(item.ColourName, item.SizeName);
                table.Cell().Element(ItemCell).Text(item.ProductName).FontSize(9).SemiBold().FontColor(Ink);
                table.Cell().Element(ItemCell).Text(item.Sku).FontSize(8).FontColor(Muted);
                table.Cell().Element(ItemCell).Text(variant).FontSize(9).FontColor(Body);
                table.Cell().Element(ItemCell).AlignRight().Text(item.Quantity.ToString(CultureInfo.InvariantCulture)).FontSize(9);
                table.Cell().Element(ItemCell).AlignRight().Text(InvoiceFormatting.FormatMoney(item.UnitPrice, invoice.Currency)).FontSize(9);
                table.Cell().Element(ItemCell).AlignRight().Text(InvoiceFormatting.FormatMoney(item.LineTotal, invoice.Currency)).FontSize(9).SemiBold();
            }

            static IContainer HeaderCell(IContainer container) =>
                container.Shrink().Background(Ink).PaddingVertical(6).PaddingHorizontal(4);

            static IContainer ItemCell(IContainer container) =>
                container.Shrink().BorderBottom(0.7f).BorderColor(Hairline).PaddingVertical(6).PaddingHorizontal(4);
        });
    }

    private void ComposeTotals(IContainer container, InvoiceDto invoice)
    {
        container.AlignRight().Width(260).Column(column =>
        {
            column.Item().Row(row => TotalsRow(row, ("Subtotal", InvoiceFormatting.FormatMoney(invoice.Subtotal, invoice.Currency), false)));
            if (invoice.CouponDiscount > 0)
            {
                column.Item().Row(row => TotalsRow(row, ("Coupon discount", "- " + InvoiceFormatting.FormatMoney(invoice.CouponDiscount, invoice.Currency), false)));
            }

            if (invoice.ProductDiscount > 0)
            {
                column.Item().Row(row => TotalsRow(row, ("Product discount", "- " + InvoiceFormatting.FormatMoney(invoice.ProductDiscount, invoice.Currency), false)));
            }

            column.Item().Row(row => TotalsRow(row, ("Shipping", InvoiceFormatting.FormatMoney(invoice.ShippingCharge, invoice.Currency), false)));
            column.Item().Row(row => TotalsRow(row, ("Tax", InvoiceFormatting.FormatMoney(invoice.Tax, invoice.Currency), false)));
            column.Item().PaddingTop(6).Row(row => TotalsRow(row, ("Grand total", InvoiceFormatting.FormatMoney(invoice.GrandTotal, invoice.Currency), true)));

            column.Item().PaddingTop(8).Row(row => TotalsRow(row, ("Paid", InvoiceFormatting.FormatMoney(invoice.PaidAmount, invoice.Currency), false)));
            column.Item().Row(row => TotalsRow(row, ("Outstanding", InvoiceFormatting.FormatMoney(invoice.OutstandingAmount, invoice.Currency), false)));
            if (invoice.RefundedAmount > 0)
            {
                column.Item().Row(row => TotalsRow(row, ("Refunded", InvoiceFormatting.FormatMoney(invoice.RefundedAmount, invoice.Currency), false)));
            }
        });
    }

    private static void TotalsRow(RowDescriptor row, (string Label, string Value, bool Emphasize) line)
    {
        row.RelativeItem().Text(line.Label).FontSize(9).FontColor(Muted);
        var value = row.ConstantItem(130).AlignRight().Text(line.Value)
            .FontSize(line.Emphasize ? 12 : 9)
            .FontColor(line.Emphasize ? Ink : Body);
        if (line.Emphasize)
        {
            value.Bold();
        }
        else
        {
            value.SemiBold();
        }
    }

    private void ComposeNotes(IContainer container, InvoiceDto invoice)
    {
        container.Column(column =>
        {
            column.Item().Text("NOTES").FontSize(9).Bold().FontColor(Muted);
            foreach (var note in invoice.Notes)
            {
                column.Item().PaddingTop(3).Text(note).FontSize(9).FontColor(Body);
            }
        });
    }

    private void ComposeRefunds(IContainer container, InvoiceDto invoice)
    {
        container.Column(column =>
        {
            column.Item().Text("RETURNS & REFUNDS").FontSize(9).Bold().FontColor(Muted);
            foreach (var refund in invoice.RefundReferences)
            {
                var line = string.IsNullOrWhiteSpace(refund.ProviderRefundId)
                    ? $"Refund of {InvoiceFormatting.FormatMoney(refund.Amount, refund.Currency)} on {InvoiceFormatting.FormatDate(refund.CreatedAtUtc)}"
                    : $"Refund {refund.ProviderRefundId} of {InvoiceFormatting.FormatMoney(refund.Amount, refund.Currency)} on {InvoiceFormatting.FormatDate(refund.CreatedAtUtc)}";
                column.Item().PaddingTop(3).Text(line).FontSize(9).FontColor(Body);
            }
        });
    }

    private void ComposeRegistration(IContainer container, InvoiceDto invoice)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(0.7f).LineColor(Hairline);
            column.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"{CompanyName} · {_settings.CompanyAddress}").FontSize(8).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(RegistrationLine()).FontSize(8).FontColor(Muted);
            });
        });

        string RegistrationLine() => string.Join(" · ", new[]
        {
            string.IsNullOrWhiteSpace(_settings.TaxId) ? null : $"Tax ID {_settings.TaxId}",
            string.IsNullOrWhiteSpace(_settings.RegistrationNumber) ? null : $"Reg {_settings.RegistrationNumber}"
        }.Where(part => part is not null));
    }

    // ---- Footer (repeats on every page) ----

    private void ComposeFooter(IContainer container, InvoiceDto invoice)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(0.7f).LineColor(Hairline);
            column.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"Invoice {invoice.InvoiceNumber} · generated {InvoiceFormatting.FormatDate(invoice.GeneratedAtUtc)}").FontSize(8).FontColor(Faint);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Faint));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
            column.Item().PaddingTop(3).AlignCenter().Text("Thank you for shopping with us.").FontSize(8).FontColor(Faint);
        });
    }

    // ---- Helpers ----

    private string CompanyName => string.IsNullOrWhiteSpace(_settings.CompanyName)
        ? "FashionStore"
        : _settings.CompanyName;

    private static string FormatCityLine(InvoiceAddressDto address)
    {
        var city = address.City;
        if (!string.IsNullOrWhiteSpace(address.Region))
        {
            city = $"{city}, {address.Region}";
        }

        return string.IsNullOrWhiteSpace(address.PostalCode) ? city : $"{city} {address.PostalCode}";
    }

    private static string ComposeVariantLabel(string? colour, string? size)
    {
        if (string.IsNullOrWhiteSpace(colour) && string.IsNullOrWhiteSpace(size))
        {
            return "—";
        }

        return string.IsNullOrWhiteSpace(colour)
            ? size!
            : string.IsNullOrWhiteSpace(size)
                ? colour
                : $"{colour} / {size}";
    }

    private static string Humanize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "—";
        }

        var parts = code.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
