using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Host.Migrations
{
    /// <inheritdoc />
    public partial class AddNonCashOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NonCashOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonCashOperations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NonCashOperations_CreatedAt",
                table: "NonCashOperations",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NonCashOperations");
        }
    }
}
