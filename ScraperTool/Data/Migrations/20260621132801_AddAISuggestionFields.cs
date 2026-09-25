using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScraperTool.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAISuggestionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AiAttemptedAt",
                table: "ValidationIssues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedValue",
                table: "ValidationIssues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestionReason",
                table: "ValidationIssues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestionSeverity",
                table: "ValidationIssues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuggestionStatus",
                table: "ValidationIssues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAttemptedAt",
                table: "ValidationIssues");

            migrationBuilder.DropColumn(
                name: "SuggestedValue",
                table: "ValidationIssues");

            migrationBuilder.DropColumn(
                name: "SuggestionReason",
                table: "ValidationIssues");

            migrationBuilder.DropColumn(
                name: "SuggestionSeverity",
                table: "ValidationIssues");

            migrationBuilder.DropColumn(
                name: "SuggestionStatus",
                table: "ValidationIssues");
        }
    }
}
