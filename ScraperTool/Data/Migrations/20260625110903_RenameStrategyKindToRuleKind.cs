using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScraperTool.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameStrategyKindToRuleKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StrategyKind",
                table: "UrlFixStrategies",
                newName: "RuleKind");

            migrationBuilder.RenameIndex(
                name: "IX_UrlFixStrategies_StrategyKind",
                table: "UrlFixStrategies",
                newName: "IX_UrlFixStrategies_RuleKind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RuleKind",
                table: "UrlFixStrategies",
                newName: "StrategyKind");

            migrationBuilder.RenameIndex(
                name: "IX_UrlFixStrategies_RuleKind",
                table: "UrlFixStrategies",
                newName: "IX_UrlFixStrategies_StrategyKind");
        }
    }
}
