using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScraperTool.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationMetadataEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FieldName = table.Column<string>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedCharacteristics = table.Column<string>(type: "TEXT", nullable: true),
                    NotExpected = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvestigationWorkflows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StepNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationWorkflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DecisionGuidelines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Guideline = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionGuidelines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldDefinitions_FieldName",
                table: "FieldDefinitions",
                column: "FieldName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationWorkflows_StepNumber",
                table: "InvestigationWorkflows",
                column: "StepNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DecisionGuidelines_Priority_Id",
                table: "DecisionGuidelines",
                columns: new[] { "Priority", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FieldDefinitions");
            migrationBuilder.DropTable(name: "InvestigationWorkflows");
            migrationBuilder.DropTable(name: "DecisionGuidelines");
        }
    }
}