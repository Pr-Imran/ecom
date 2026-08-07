namespace FashionStore.Infrastructure.Data;

public static class ApplicationPermissions
{
    public static class Dashboard
    {
        public const string View = "Dashboard.View";
    }

    public static class Products
    {
        public const string View = "Products.View";
        public const string Create = "Products.Create";
        public const string Update = "Products.Update";
        public const string Delete = "Products.Delete";
        public const string ManageInventory = "Products.ManageInventory";
    }

    public static class Categories
    {
        public const string Manage = "Categories.Manage";
    }

    public static class Brands
    {
        public const string Manage = "Brands.Manage";
    }

    public static class Orders
    {
        public const string View = "Orders.View";
        public const string UpdateStatus = "Orders.UpdateStatus";
        public const string Cancel = "Orders.Cancel";
        public const string Refund = "Orders.Refund";
        public const string PrintInvoice = "Orders.PrintInvoice";
    }

    public static class Customers
    {
        public const string View = "Customers.View";
        public const string Update = "Customers.Update";
    }

    public static class Reviews
    {
        public const string Manage = "Reviews.Manage";
    }

    public static class Promotions
    {
        public const string Manage = "Promotions.Manage";
    }

    public static class Coupons
    {
        public const string Manage = "Coupons.Manage";
    }

    public static class Shipping
    {
        public const string Manage = "Shipping.Manage";
    }

    public static class Content
    {
        public const string Manage = "Content.Manage";
    }

    public static class Reports
    {
        public const string View = "Reports.View";
    }

    public static class Settings
    {
        public const string Manage = "Settings.Manage";
    }

    public static class Users
    {
        public const string Manage = "Users.Manage";
    }

    public static class Roles
    {
        public const string Manage = "Roles.Manage";
    }

    public static class AuditLogs
    {
        public const string View = "AuditLogs.View";
    }

    public static readonly string[] AllPermissions =
    {
        Dashboard.View,
        Products.View, Products.Create, Products.Update, Products.Delete, Products.ManageInventory,
        Categories.Manage,
        Brands.Manage,
        Orders.View, Orders.UpdateStatus, Orders.Cancel, Orders.Refund, Orders.PrintInvoice,
        Customers.View, Customers.Update,
        Reviews.Manage,
        Promotions.Manage,
        Coupons.Manage,
        Shipping.Manage,
        Content.Manage,
        Reports.View,
        Settings.Manage,
        Users.Manage,
        Roles.Manage,
        AuditLogs.View
    };
}
