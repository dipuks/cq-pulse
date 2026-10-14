using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CQPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunConcurrencyAndActiveRunIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TestRuns",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_TestRuns_ActiveTarget",
                table: "TestRuns",
                columns: new[] { "ProjectId", "TestSuiteId" },
                unique: true,
                filter: "[Status] IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TestRuns_ActiveTarget",
                table: "TestRuns");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TestRuns");
        }
    }
}
