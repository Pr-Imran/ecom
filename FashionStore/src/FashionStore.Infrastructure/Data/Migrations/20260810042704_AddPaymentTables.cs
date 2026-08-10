using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionStore.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestMetadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseMetadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InitiatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestMetadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseMetadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAttempts_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefundRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ProviderRefundId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InitiatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefundRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRefundRecords_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    ResultCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ResultMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestMetadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseMetadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Signature = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentWebhookLogs_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_PaymentId",
                table: "PaymentAttempts",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_PaymentId_AttemptNumber",
                table: "PaymentAttempts",
                columns: new[] { "PaymentId", "AttemptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefundRecords_PaymentId",
                table: "PaymentRefundRecords",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefundRecords_ProviderRefundId",
                table: "PaymentRefundRecords",
                column: "ProviderRefundId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CreatedAtUtc",
                table: "Payments",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ExpiresAtUtc",
                table: "Payments",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_IdempotencyKey",
                table: "Payments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderTransactionId",
                table: "Payments",
                column: "ProviderTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_State",
                table: "Payments",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_CreatedAtUtc",
                table: "PaymentTransactions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_PaymentId",
                table: "PaymentTransactions",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Type",
                table: "PaymentTransactions",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookLogs_PaymentId",
                table: "PaymentWebhookLogs",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookLogs_ProviderCode_ProviderEventId",
                table: "PaymentWebhookLogs",
                columns: new[] { "ProviderCode", "ProviderEventId" },
                unique: true,
                filter: "[ProviderEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookLogs_ReceivedAtUtc",
                table: "PaymentWebhookLogs",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookLogs_Status",
                table: "PaymentWebhookLogs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentAttempts");

            migrationBuilder.DropTable(
                name: "PaymentRefundRecords");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "PaymentWebhookLogs");

            migrationBuilder.DropTable(
                name: "Payments");
        }
    }
}
