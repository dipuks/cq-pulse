using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CQPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFailureSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Signature",
                table: "TestCaseResults",
                type: "char(12)",
                unicode: false,
                fixedLength: true,
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCaseResults_Outcome_CompletedAtUtc",
                table: "TestCaseResults",
                columns: new[] { "Outcome", "CompletedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "Signature", "TestRunId", "TestCaseId", "FailureCategory", "DurationMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestCaseResults_Outcome_CompletedAtUtc",
                table: "TestCaseResults");

            migrationBuilder.DropColumn(
                name: "Signature",
                table: "TestCaseResults");
        }
    }
}
