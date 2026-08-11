using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionStore.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Roles_RoleId1",
                table: "UserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Users_UserId1",
                table: "UserRoles");

            migrationBuilder.DropIndex(
                name: "IX_UserRoles_RoleId1",
                table: "UserRoles");

            migrationBuilder.DropIndex(
                name: "IX_UserRoles_UserId1",
                table: "UserRoles");

            migrationBuilder.DropIndex(
                name: "IX_ProductReviews_ProductId_IsApproved",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "RoleId1",
                table: "UserRoles");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "UserRoles");

            migrationBuilder.RenameColumn(
                name: "IsApproved",
                table: "ProductReviews",
                newName: "IsFlagged");

            migrationBuilder.AddColumn<decimal>(
                name: "AverageRating",
                table: "Products",
                type: "decimal(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount1",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount2",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount3",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount4",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount5",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReviewCount",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "ProductReviews",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HelpfulCount",
                table: "ProductReviews",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ModerationNotes",
                table: "ProductReviews",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "ProductReviews",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderItemId",
                table: "ProductReviews",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "ProductReviews",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ReviewHelpfulVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewHelpfulVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewHelpfulVotes_ProductReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "ProductReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewImages_ProductReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "ProductReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_IsVerifiedPurchase",
                table: "ProductReviews",
                column: "IsVerifiedPurchase");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ProductId_Status",
                table: "ProductReviews",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_UserId_ProductId",
                table: "ProductReviews",
                columns: new[] { "UserId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewHelpfulVotes_ReviewId_UserId",
                table: "ReviewHelpfulVotes",
                columns: new[] { "ReviewId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewImages_ReviewId",
                table: "ReviewImages",
                column: "ReviewId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewHelpfulVotes");

            migrationBuilder.DropTable(
                name: "ReviewImages");

            migrationBuilder.DropIndex(
                name: "IX_ProductReviews_IsVerifiedPurchase",
                table: "ProductReviews");

            migrationBuilder.DropIndex(
                name: "IX_ProductReviews_ProductId_Status",
                table: "ProductReviews");

            migrationBuilder.DropIndex(
                name: "IX_ProductReviews_UserId_ProductId",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "AverageRating",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RatingCount1",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RatingCount2",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RatingCount3",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RatingCount4",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RatingCount5",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReviewCount",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "HelpfulCount",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "ModerationNotes",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "OrderItemId",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ProductReviews");

            migrationBuilder.RenameColumn(
                name: "IsFlagged",
                table: "ProductReviews",
                newName: "IsApproved");

            migrationBuilder.AddColumn<string>(
                name: "RoleId1",
                table: "UserRoles",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId1",
                table: "UserRoles",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "ProductReviews",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId1",
                table: "UserRoles",
                column: "RoleId1");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId1",
                table: "UserRoles",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ProductId_IsApproved",
                table: "ProductReviews",
                columns: new[] { "ProductId", "IsApproved" });

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Roles_RoleId1",
                table: "UserRoles",
                column: "RoleId1",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Users_UserId1",
                table: "UserRoles",
                column: "UserId1",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
