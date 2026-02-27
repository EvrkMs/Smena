using Host.Services.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Host.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260227203000_EnsureRootPanelUsersExists")]
    public partial class EnsureRootPanelUsersExists : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('"RootPanelUsers"') IS NULL THEN
                        CREATE TABLE "RootPanelUsers" (
                            "Id" uuid NOT NULL,
                            "Username" text NOT NULL,
                            "PasswordHash" text NOT NULL,
                            "MustChangePassword" boolean NOT NULL,
                            "CreatedAt" timestamp with time zone NOT NULL,
                            "UpdatedAt" timestamp with time zone NOT NULL,
                            CONSTRAINT "PK_RootPanelUsers" PRIMARY KEY ("Id")
                        );
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_RootPanelUsers_CreatedAt" ON "RootPanelUsers" ("CreatedAt");""");

            migrationBuilder.Sql(
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_RootPanelUsers_Username" ON "RootPanelUsers" ("Username");""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "RootPanelUsers";""");
        }
    }
}
