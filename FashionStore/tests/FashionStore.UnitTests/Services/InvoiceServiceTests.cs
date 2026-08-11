using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Invoices;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Invoicing;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

/// <summary>
/// Exercises the invoice pipeline: generation from order snapshots (never the live
/// catalogue), sequential and concurrency-safe numbering, idempotency, regeneration
/// that reflects refunds while keeping the number, and PDF byte production.
/// </summary>
public class InvoiceServiceTests
{
    private sealed class Fixture
    {
        public AppDbContext Context { get; }
        public Mock<IInvoicePdfGenerator> PdfGenerator { get; } = new();
        public Mock<IEmailService> EmailService { get; } = new();

        public Fixture(string? sharedDatabaseName = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(sharedDatabaseName ?? $"fashionstore-invoice-{Guid.NewGuid()}")
                .Options;
            Context = new AppDbContext(options);

            PdfGenerator
                .Setup(g => g.Generate(It.IsAny<InvoiceDto>()))
                .Returns(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }); // "%PDF-1.4"

            EmailService
                .Setup(e => e.SendEmailWithAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        public InvoiceService CreateService() =>
            new(
                Context,
                PdfGenerator.Object,
                EmailService.Object,
                Options.Create(new InvoiceSettings
                {
                    CompanyName = "FashionStore Inc.",
                    InvoicePrefix = "INV-",
                    YearAware = true
                }),
                NullLogger<InvoiceService>.Instance);
    }

    private static InvoiceService BuildService(AppDbContext context)
    {
        var generator = new Mock<IInvoicePdfGenerator>();
        generator.Setup(g => g.Generate(It.IsAny<InvoiceDto>())).Returns(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 });

        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendEmailWithAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new InvoiceService(
            context,
            generator.Object,
            email.Object,
            Options.Create(new InvoiceSettings { InvoicePrefix = "INV-", YearAware = true }),
            NullLogger<InvoiceService>.Instance);
    }

    private static Order SeedOrder(
        AppDbContext context,
        string number = "ORD-2026-000001",
        PaymentStatus payment = PaymentStatus.Unpaid,
        decimal paid = 0m,
        decimal refunded = 0m,
        decimal subtotal = 100m,
        decimal discount = 10m,
        decimal shipping = 8m,
        decimal tax = 5m,
        decimal total = 103m,
        string email = "jane@example.com")
    {
        var order = new Order
        {
            PublicOrderNumber = number,
            InvoiceNumber = null,
            UserId = "user-1",
            GuestEmail = email,
            GuestPhone = "555-0100",
            CustomerName = "Jane Doe",
            Currency = "USD",
            Subtotal = subtotal,
            ProductDiscount = discount,
            CouponDiscount = 0m,
            ShippingCharge = shipping,
            Tax = tax,
            GrandTotal = total,
            PaidAmount = paid,
            RefundedAmount = refunded,
            PaymentMethodCode = "card",
            ShippingMethodName = "Standard Delivery",
            OrderStatus = OrderStatus.Placed,
            PaymentStatus = payment,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        order.Items.Add(new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductVariantId = Guid.NewGuid(),
            ProductName = "Cashmere Sweater",
            ProductSlug = "cashmere-sweater",
            Sku = "SW-1001-GREY-M",
            ColourName = "Grey",
            ColourValue = "#808080",
            SizeName = "M",
            ImageUrl = "/img/sweater.jpg",
            UnitPrice = 100m,
            Discount = 0m,
            Tax = 5m,
            Quantity = 1,
            LineTotal = 100m
        });

        order.ShippingAddress = new OrderAddress
        {
            AddressType = OrderAddressType.Shipping,
            RecipientName = "Jane Doe",
            Phone = "555-0100",
            AddressLine1 = "1 Main Street",
            City = "New York",
            Region = "NY",
            PostalCode = "10001",
            CountryCode = "US"
        };

        order.BillingAddress = new OrderAddress
        {
            AddressType = OrderAddressType.Billing,
            RecipientName = "Jane Doe",
            AddressLine1 = "1 Main Street",
            City = "New York",
            Region = "NY",
            PostalCode = "10001",
            CountryCode = "US"
        };

        context.Orders.Add(order);
        context.SaveChanges();
        return order;
    }

    [Fact]
    public async Task EnsureForOrderAsync_AssignsSequentialUniqueInvoiceNumbers()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var first = SeedOrder(fixture.Context, "ORD-2026-000001");
        var second = SeedOrder(fixture.Context, "ORD-2026-000002");

        var invoiceOne = await service.EnsureForOrderAsync(first.Id);
        var invoiceTwo = await service.EnsureForOrderAsync(second.Id);

        var year = DateTime.UtcNow.Year;
        Assert.Equal($"INV-{year}-000001", invoiceOne.InvoiceNumber);
        Assert.Equal($"INV-{year}-000002", invoiceTwo.InvoiceNumber);
        Assert.NotEqual(invoiceOne.InvoiceNumber, invoiceTwo.InvoiceNumber);

        var reloadedFirst = await fixture.Context.Orders.AsNoTracking().FirstAsync(o => o.Id == first.Id);
        Assert.Equal(invoiceOne.InvoiceNumber, reloadedFirst.InvoiceNumber);
    }

    [Fact]
    public async Task EnsureForOrderAsync_IsIdempotent_ReturnsSameInvoiceWithoutDuplicate()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var order = SeedOrder(fixture.Context);

        var first = await service.EnsureForOrderAsync(order.Id);
        var second = await service.EnsureForOrderAsync(order.Id);

        Assert.Equal(first.InvoiceId, second.InvoiceId);
        Assert.Equal(first.InvoiceNumber, second.InvoiceNumber);
        Assert.Equal(1, await fixture.Context.Invoices.CountAsync(i => i.OrderId == order.Id));
    }

    [Fact]
    public async Task EnsureForOrderAsync_UsesOrderSnapshot_NotLiveCatalogueData()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var order = SeedOrder(fixture.Context);

        // A live catalogue product that disagrees with the snapshot. The invoice must
        // never see the renamed / repriced product.
        fixture.Context.Products.Add(new Product
        {
            Name = "Renamed Product",
            Slug = "renamed-product",
            BaseSku = "SW-1001",
            BasePrice = 999m,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();

        var invoice = await service.EnsureForOrderAsync(order.Id);

        var item = Assert.Single(invoice.Items);
        Assert.Equal("Cashmere Sweater", item.ProductName);
        Assert.Equal("SW-1001-GREY-M", item.Sku);
        Assert.Equal("Grey", item.ColourName);
        Assert.Equal("M", item.SizeName);
        Assert.Equal(100m, item.UnitPrice);
        Assert.Equal(100m, item.LineTotal);
    }

    [Fact]
    public async Task EnsureForOrderAsync_ComputesFinancialState_FromOrderSnapshot()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var order = SeedOrder(
            fixture.Context,
            payment: PaymentStatus.PartiallyPaid,
            paid: 60m,
            refunded: 0m,
            subtotal: 100m,
            discount: 10m,
            shipping: 8m,
            tax: 5m,
            total: 103m);

        var invoice = await service.EnsureForOrderAsync(order.Id);

        Assert.Equal("PartiallyPaid", invoice.Status);
        Assert.Equal(103m, invoice.GrandTotal);
        Assert.Equal(100m, invoice.Subtotal);
        Assert.Equal(10m, invoice.ProductDiscount);
        Assert.Equal(8m, invoice.ShippingCharge);
        Assert.Equal(5m, invoice.Tax);
        Assert.Equal(60m, invoice.PaidAmount);
        Assert.Equal(43m, invoice.OutstandingAmount);
        Assert.Equal(0m, invoice.RefundedAmount);
    }

    [Fact]
    public async Task RegenerateAsync_KeepsNumber_ReflectsRefund_AndBumpsVersion()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var order = SeedOrder(
            fixture.Context,
            payment: PaymentStatus.PartiallyPaid,
            paid: 60m,
            refunded: 0m,
            total: 103m);

        var original = await service.EnsureForOrderAsync(order.Id);
        Assert.Equal(1, original.Version);

        // A refund is recorded against the order after the invoice was generated.
        order.RefundedAmount = 40m;
        await fixture.Context.SaveChangesAsync();

        var regenerated = await service.RegenerateAsync(order.Id);

        Assert.Equal(original.InvoiceNumber, regenerated.InvoiceNumber);
        Assert.Equal(original.InvoiceId, regenerated.InvoiceId);
        Assert.Equal(2, regenerated.Version);
        Assert.Equal(40m, regenerated.RefundedAmount);
        Assert.Equal(3m, regenerated.OutstandingAmount);
        Assert.Equal("PartiallyPaid", regenerated.Status);
    }

    [Fact]
    public async Task RegenerateAsync_WhenNoInvoiceExists_CreatesOne()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var order = SeedOrder(fixture.Context, payment: PaymentStatus.Paid, paid: 103m);

        var invoice = await service.RegenerateAsync(order.Id);

        Assert.NotNull(invoice);
        Assert.Equal(1, invoice.Version);
        Assert.Equal("Paid", invoice.Status);
        Assert.Equal(0m, invoice.OutstandingAmount);
    }

    [Fact]
    public async Task EnsureForOrderAsync_UniqueNumbers_UnderConcurrentGeneration()
    {
        var root = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("fashionstore-invoice-concurrent", root)
            .Options;

        using var firstContext = new AppDbContext(options);
        var first = SeedOrder(firstContext, "ORD-C-000001");
        using var secondContext = new AppDbContext(options);
        var second = SeedOrder(secondContext, "ORD-C-000002");

        var serviceOne = BuildService(firstContext);
        var serviceTwo = BuildService(secondContext);

        var invoiceOne = await serviceOne.EnsureForOrderAsync(first.Id);
        var invoiceTwo = await serviceTwo.EnsureForOrderAsync(second.Id);

        Assert.NotEqual(invoiceOne.InvoiceNumber, invoiceTwo.InvoiceNumber);

        using var verifyContext = new AppDbContext(options);
        var stored = await verifyContext.Invoices.AsNoTracking().ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(2, stored.Select(i => i.InvoiceNumber).Distinct().Count());
    }

    [Fact]
    public async Task BuildPdfAsync_ReturnsGeneratorBytes()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var order = SeedOrder(fixture.Context);

        var invoice = await service.EnsureForOrderAsync(order.Id);
        var pdf = await service.BuildPdfAsync(invoice);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 0);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, pdf.Take(4));
        fixture.PdfGenerator.Verify(
            g => g.Generate(It.Is<InvoiceDto>(d => d.InvoiceId == invoice.InvoiceId)),
            Times.Once);
    }
}
