using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailMerge.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipientFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "filter",
                table: "campaigns",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "filter",
                table: "campaigns");
        }
    }
}
