using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CQPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFailureAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FailureAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InputDigest = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    InputFailureCount = table.Column<int>(type: "int", nullable: false),
                    InputTruncated = table.Column<bool>(type: "bit", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ExternalAnalysisId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SignalScore = table.Column<double>(type: "float", nullable: true),
                    TopCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FindingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecommendationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FailureAnalyses_TestRuns_TestRunId",
                        column: x => x.TestRunId,
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FailureAnalyses_Status",
                table: "FailureAnalyses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FailureAnalyses_TestRunId_RequestedAtUtc",
                table: "FailureAnalyses",
                columns: new[] { "TestRunId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FailureAnalyses");
        }
    }
}
