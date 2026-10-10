using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailMerge.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionalBusinessFooter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "business_footer",
                table: "workspaces",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "business_footer",
                table: "campaigns",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "include_business_footer",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "use_workspace_business_footer",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "business_footer",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "business_footer",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "include_business_footer",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "use_workspace_business_footer",
                table: "campaigns");
        }
    }
}
