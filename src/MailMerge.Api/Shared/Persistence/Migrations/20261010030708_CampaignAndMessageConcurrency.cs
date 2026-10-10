using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailMerge.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignAndMessageConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "messages",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "campaigns",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "campaigns");
        }
    }
}
