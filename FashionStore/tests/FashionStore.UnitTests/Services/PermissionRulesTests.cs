using System.Reflection;
using FashionStore.Application.Authorization;
using FashionStore.Infrastructure.Data;

namespace FashionStore.UnitTests.Services;

public class PermissionRulesTests
{
    [Fact]
    public void AllPermissions_HasNoDuplicates()
    {
        var duplicates = ApplicationPermissions.AllPermissions
            .GroupBy(p => p)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void AllPermissions_AreNonEmptyAndUseDomainPrefix()
    {
        Assert.NotEmpty(ApplicationPermissions.AllPermissions);
        foreach (var permission in ApplicationPermissions.AllPermissions)
        {
            Assert.Contains('.', permission);
            Assert.True(permission.Length > 3);
            Assert.All(permission, c => Assert.True(char.IsLetterOrDigit(c) || c == '.'));
        }
    }

    [Theory]
    [InlineData("Dashboard.View")]
    [InlineData("Products.View")]
    [InlineData("Products.Create")]
    [InlineData("Products.Update")]
    [InlineData("Products.Delete")]
    [InlineData("Orders.View")]
    [InlineData("Orders.Cancel")]
    [InlineData("Returns.View")]
    [InlineData("Returns.Refund")]
    [InlineData("Reports.View")]
    [InlineData("Settings.Manage")]
    [InlineData("Inventory.Manage")]
    public void PolicyPermissions_AreRegistered(string permission)
    {
        Assert.Contains(permission, ApplicationPermissions.AllPermissions);
    }

    [Fact]
    public void OrderPolicies_AllReferenceRegisteredPermissions()
    {
        var values = typeof(OrderPolicies).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.All(values, v => Assert.Contains(v, ApplicationPermissions.AllPermissions));
    }

    [Fact]
    public void InventoryPolicies_AllReferenceRegisteredPermissions()
    {
        var values = typeof(InventoryPolicies).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.All(values, v => Assert.Contains(v, ApplicationPermissions.AllPermissions));
    }

    [Fact]
    public void ReturnPolicies_AllReferenceRegisteredPermissions()
    {
        var values = typeof(ReturnPolicies).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.All(values, v => Assert.Contains(v, ApplicationPermissions.AllPermissions));
    }

    [Fact]
    public void AllPolicyGroups_ReferenceOnlyRegisteredPermissions()
    {
        var policyTypes = new[]
        {
            typeof(ContentPolicies), typeof(DashboardPolicies), typeof(EmailPolicies),
            typeof(InventoryPolicies), typeof(OrderPolicies), typeof(ReportsPolicies),
            typeof(ReturnPolicies), typeof(ReviewPolicies), typeof(SettingsPolicies),
            typeof(ShippingPolicies)
        };

        foreach (var type in policyTypes)
        {
            var values = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly)
                .Select(f => (string)f.GetRawConstantValue()!);

            Assert.All(values, v => Assert.Contains(v, ApplicationPermissions.AllPermissions));
        }
    }
}
