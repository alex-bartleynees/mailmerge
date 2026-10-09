using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MailMerge.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_lists",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    columns = table.Column<List<string>>(type: "text[]", nullable: false),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_lists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_uploads",
                columns: table => new
                {
                    token = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_uploads", x => x.token);
                });

            migrationBuilder.CreateTable(
                name: "opt_outs",
                columns: table => new
                {
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    email_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    added = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opt_outs", x => new { x.workspace_id, x.email_hash });
                });

            migrationBuilder.CreateTable(
                name: "pending_microsoft_sign_ins",
                columns: table => new
                {
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    user_subject = table.Column<string>(type: "text", nullable: false),
                    protected_code_verifier = table.Column<string>(type: "text", nullable: false),
                    expires = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_microsoft_sign_ins", x => x.state);
                });

            migrationBuilder.CreateTable(
                name: "privacy_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    performed_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    counts = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_privacy_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sending_accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    from_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    per_minute = table.Column<int>(type: "integer", nullable: false),
                    smtp = table.Column<string>(type: "jsonb", nullable: true),
                    protected_secrets = table.Column<byte[]>(type: "bytea", nullable: true),
                    microsoft_token_cache = table.Column<byte[]>(type: "bytea", nullable: true),
                    microsoft_account = table.Column<string>(type: "text", nullable: true),
                    microsoft_account_id = table.Column<string>(type: "text", nullable: true),
                    signed_in_by = table.Column<string>(type: "text", nullable: true),
                    next_send_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sending_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    keycloak_group = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    retention_days = table.Column<int>(type: "integer", nullable: false),
                    opt_out_hash_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    list_id = table.Column<long>(type: "bigint", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    do_not_email = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contacts_contact_lists_list_id",
                        column: x => x.list_id,
                        principalTable: "contact_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: true),
                    list_id = table.Column<long>(type: "bigint", nullable: true),
                    subject = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_html = table.Column<bool>(type: "boolean", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    unsubscribe_footer = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    summary = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaigns", x => x.id);
                    table.ForeignKey(
                        name: "fk_campaigns_contact_lists_list_id",
                        column: x => x.list_id,
                        principalTable: "contact_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_campaigns_sending_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "sending_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    campaign_id = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_attachments_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workspace_id = table.Column<long>(type: "bigint", nullable: false),
                    campaign_id = table.Column<long>(type: "bigint", nullable: false),
                    contact_id = table.Column<long>(type: "bigint", nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    provider_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_messages_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_campaign_id",
                table: "attachments",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_account_id",
                table: "campaigns",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_list_id",
                table: "campaigns",
                column: "list_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_status",
                table: "campaigns",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_workspace_id",
                table: "campaigns",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_lists_workspace_id",
                table: "contact_lists",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_list_id_email",
                table: "contacts",
                columns: new[] { "list_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_workspace_id_email",
                table: "contacts",
                columns: new[] { "workspace_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_campaign_id_email",
                table: "messages",
                columns: new[] { "campaign_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_campaign_id_status",
                table: "messages",
                columns: new[] { "campaign_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_contact_id",
                table: "messages",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_workspace_id_email",
                table: "messages",
                columns: new[] { "workspace_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_privacy_log_workspace_id_performed_at",
                table: "privacy_log",
                columns: new[] { "workspace_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sending_accounts_workspace_id",
                table: "sending_accounts",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_keycloak_group",
                table: "workspaces",
                column: "keycloak_group",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "import_uploads");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "opt_outs");

            migrationBuilder.DropTable(
                name: "pending_microsoft_sign_ins");

            migrationBuilder.DropTable(
                name: "privacy_log");

            migrationBuilder.DropTable(
                name: "workspaces");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "sending_accounts");

            migrationBuilder.DropTable(
                name: "contact_lists");
        }
    }
}
