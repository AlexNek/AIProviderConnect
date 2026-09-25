using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScraperTool.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUrlFixStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UrlFixStrategies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StrategyKind = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UrlFixStrategies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UrlFixStrategies_StrategyKind",
                table: "UrlFixStrategies",
                column: "StrategyKind",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UrlFixStrategies");
        }
    }
}
