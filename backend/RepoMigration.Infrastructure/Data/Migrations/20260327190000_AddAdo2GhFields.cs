using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RepoMigration.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdo2GhFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdoPipeline",
                table: "RepoMigration",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceConnectionId",
                table: "RepoMigration",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetRepoVisibility",
                table: "RepoMigration",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "private");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdoPipeline",
                table: "RepoMigration");

            migrationBuilder.DropColumn(
                name: "ServiceConnectionId",
                table: "RepoMigration");

            migrationBuilder.DropColumn(
                name: "TargetRepoVisibility",
                table: "RepoMigration");
        }
    }
}
