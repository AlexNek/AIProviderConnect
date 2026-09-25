using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScraperTool.Data.Migrations;

/// <summary>
/// Drops the 4 configuration tables that have been moved to local JSON files:
/// - DecisionGuidelines
/// - FieldDefinitions
/// - InvestigationWorkflows
/// - UrlIntelligenceRuleEntries
/// </summary>
public partial class DropConfigTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DecisionGuidelines");
        migrationBuilder.DropTable(name: "FieldDefinitions");
        migrationBuilder.DropTable(name: "InvestigationWorkflows");
        migrationBuilder.DropTable(name: "UrlIntelligenceRuleEntries");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DecisionGuidelines",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                FieldName = table.Column<string>(type: "TEXT", nullable: true),
                Guideline = table.Column<string>(type: "TEXT", nullable: false),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Priority = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_DecisionGuidelines", x => x.Id); });

        migrationBuilder.CreateIndex(
            name: "IX_DecisionGuidelines_Priority_Id",
            table: "DecisionGuidelines",
            columns: new[] { "Priority", "Id" });
    }
}
