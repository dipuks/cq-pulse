using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CQPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunRerunLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RerunOfRunId",
                table: "TestRuns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_RerunOfRunId",
                table: "TestRuns",
                column: "RerunOfRunId");

            migrationBuilder.AddForeignKey(
                name: "FK_TestRuns_TestRuns_RerunOfRunId",
                table: "TestRuns",
                column: "RerunOfRunId",
                principalTable: "TestRuns",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestRuns_TestRuns_RerunOfRunId",
                table: "TestRuns");

            migrationBuilder.DropIndex(
                name: "IX_TestRuns_RerunOfRunId",
                table: "TestRuns");

            migrationBuilder.DropColumn(
                name: "RerunOfRunId",
                table: "TestRuns");
        }
    }
}
