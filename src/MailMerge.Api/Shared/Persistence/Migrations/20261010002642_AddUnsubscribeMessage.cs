using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailMerge.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnsubscribeMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "unsubscribe_message",
                table: "campaigns",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "unsubscribe_message",
                table: "campaigns");
        }
    }
}
