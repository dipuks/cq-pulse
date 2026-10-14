using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CQPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureSeveritiesJson",
                table: "FailureAnalyses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DecisionEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Question = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceNotesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OptionCount = table.Column<int>(type: "int", nullable: false),
                    FailureAnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalEvaluationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DecisionEvaluations_TestRuns_TestRunId",
                        column: x => x.TestRunId,
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionEvaluations_ProjectId",
                table: "DecisionEvaluations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionEvaluations_TestRunId_RequestedAtUtc",
                table: "DecisionEvaluations",
                columns: new[] { "TestRunId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionEvaluations");

            migrationBuilder.DropColumn(
                name: "SignatureSeveritiesJson",
                table: "FailureAnalyses");
        }
    }
}
