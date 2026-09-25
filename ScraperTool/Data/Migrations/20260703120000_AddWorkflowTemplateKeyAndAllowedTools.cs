using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScraperTool.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowTemplateKeyAndAllowedTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedTools",
                table: "InvestigationWorkflows",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "InvestigationWorkflows",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateKey",
                table: "InvestigationWorkflows",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllowedTools", table: "InvestigationWorkflows");
            migrationBuilder.DropColumn(name: "IsEnabled", table: "InvestigationWorkflows");
            migrationBuilder.DropColumn(name: "TemplateKey", table: "InvestigationWorkflows");
        }
    }
}
