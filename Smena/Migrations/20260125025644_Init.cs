using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Host.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    HourlyRate = table.Column<int>(type: "integer", nullable: false),
                    SalaryThreadId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    FromSafe = table.Column<bool>(type: "boolean", nullable: false),
                    IsNonCash = table.Column<bool>(type: "boolean", nullable: false),
                    SendPhoto = table.Column<bool>(type: "boolean", nullable: false),
                    PhotoSessionKey = table.Column<string>(type: "text", nullable: true),
                    SenderName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Raports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FactCash = table.Column<int>(type: "integer", nullable: false),
                    FactNonCash = table.Column<int>(type: "integer", nullable: false),
                    ProgramCash = table.Column<int>(type: "integer", nullable: false),
                    ProgramNonCash = table.Column<int>(type: "integer", nullable: false),
                    FactSafe = table.Column<int>(type: "integer", nullable: false),
                    WhyMinus = table.Column<string>(type: "text", nullable: false),
                    PhotoSessionKey = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Raports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SafeOperations",
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
                    table.PrimaryKey("PK_SafeOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalaryOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryOperations_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelegramAccounts",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramAccounts", x => x.EmployeeId);
                    table.ForeignKey(
                        name: "FK_TelegramAccounts_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RaportEmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RaportId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hours = table.Column<int>(type: "integer", nullable: false),
                    Minus = table.Column<int>(type: "integer", nullable: false),
                    Salary = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaportEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaportEmployees_Raports_RaportId",
                        column: x => x.RaportId,
                        principalTable: "Raports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Name",
                table: "Employees",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CreatedAt",
                table: "Expenses",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RaportEmployees_EmployeeId",
                table: "RaportEmployees",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_RaportEmployees_RaportId",
                table: "RaportEmployees",
                column: "RaportId");

            migrationBuilder.CreateIndex(
                name: "IX_Raports_CreatedAt",
                table: "Raports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SafeOperations_CreatedAt",
                table: "SafeOperations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryOperations_CreatedAt",
                table: "SalaryOperations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryOperations_EmployeeId",
                table: "SalaryOperations",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "RaportEmployees");

            migrationBuilder.DropTable(
                name: "SafeOperations");

            migrationBuilder.DropTable(
                name: "SalaryOperations");

            migrationBuilder.DropTable(
                name: "TelegramAccounts");

            migrationBuilder.DropTable(
                name: "Raports");

            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
