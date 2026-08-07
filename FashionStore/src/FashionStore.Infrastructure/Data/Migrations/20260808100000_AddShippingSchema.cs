using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionStore.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShippingMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    EstimatedMinDays = table.Column<int>(type: "int", nullable: false),
                    EstimatedMaxDays = table.Column<int>(type: "int", nullable: false),
                    SupportsCashOnDelivery = table.Column<bool>(type: "bit", nullable: false),
                    RequiresShippingAddress = table.Column<bool>(type: "bit", nullable: false),
                    FreeShippingThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaxPackageWeight = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    PickupInstructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShippingZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingZones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryBlackouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryBlackouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryBlackouts_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShippingMethodCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsExclusion = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethodCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingMethodCategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShippingMethodCategories_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShippingMethodProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsExclusion = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethodProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingMethodProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShippingMethodProducts_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShippingRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RateType = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinWeightKg = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    MaxWeightKg = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    MinOrderAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingRates_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShippingRates_ShippingZones_ShippingZoneId",
                        column: x => x.ShippingZoneId,
                        principalTable: "ShippingZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShippingZoneCities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedCityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingZoneCities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingZoneCities_ShippingZones_ShippingZoneId",
                        column: x => x.ShippingZoneId,
                        principalTable: "ShippingZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShippingZoneCountries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingZoneCountries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingZoneCountries_ShippingZones_ShippingZoneId",
                        column: x => x.ShippingZoneId,
                        principalTable: "ShippingZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryBlackouts_IsActive",
                table: "DeliveryBlackouts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryBlackouts_ShippingMethodId",
                table: "DeliveryBlackouts",
                column: "ShippingMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryBlackouts_StartAtUtc",
                table: "DeliveryBlackouts",
                column: "StartAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethodCategories_CategoryId",
                table: "ShippingMethodCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethodCategories_ShippingMethodId_CategoryId_IsExclusion",
                table: "ShippingMethodCategories",
                columns: new[] { "ShippingMethodId", "CategoryId", "IsExclusion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethodProducts_ProductId",
                table: "ShippingMethodProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethodProducts_ShippingMethodId_ProductId_IsExclusion",
                table: "ShippingMethodProducts",
                columns: new[] { "ShippingMethodId", "ProductId", "IsExclusion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_Code",
                table: "ShippingMethods",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_DisplayOrder",
                table: "ShippingMethods",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_IsActive",
                table: "ShippingMethods",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_Type",
                table: "ShippingMethods",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRates_RateType",
                table: "ShippingRates",
                column: "RateType");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRates_ShippingMethodId",
                table: "ShippingRates",
                column: "ShippingMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRates_ShippingZoneId",
                table: "ShippingRates",
                column: "ShippingZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingZoneCities_NormalizedCityName",
                table: "ShippingZoneCities",
                column: "NormalizedCityName");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingZoneCities_ShippingZoneId_NormalizedCityName",
                table: "ShippingZoneCities",
                columns: new[] { "ShippingZoneId", "NormalizedCityName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingZoneCountries_CountryCode",
                table: "ShippingZoneCountries",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingZoneCountries_ShippingZoneId_CountryCode",
                table: "ShippingZoneCountries",
                columns: new[] { "ShippingZoneId", "CountryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingZones_DisplayOrder",
                table: "ShippingZones",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingZones_IsActive",
                table: "ShippingZones",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryBlackouts");

            migrationBuilder.DropTable(
                name: "ShippingMethodCategories");

            migrationBuilder.DropTable(
                name: "ShippingMethodProducts");

            migrationBuilder.DropTable(
                name: "ShippingRates");

            migrationBuilder.DropTable(
                name: "ShippingZoneCities");

            migrationBuilder.DropTable(
                name: "ShippingZoneCountries");

            migrationBuilder.DropTable(
                name: "ShippingMethods");

            migrationBuilder.DropTable(
                name: "ShippingZones");
        }
    }
}
