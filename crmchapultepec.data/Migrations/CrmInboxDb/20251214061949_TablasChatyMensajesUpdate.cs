using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace crmchapultepec.data.Migrations.CrmInboxDb
{
    /// <inheritdoc />
    public partial class TablasChatyMensajesUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedTo",
                table: "CrmThreads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "CrmThreads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CompanyId",
                table: "CrmThreads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerDisplayName",
                table: "CrmThreads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerEmail",
                table: "CrmThreads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhone",
                table: "CrmThreads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPlatformId",
                table: "CrmThreads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastMessagePreview",
                table: "CrmThreads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MainParticipant",
                table: "CrmThreads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "CrmThreads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ThreadKey",
                table: "CrmThreads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnreadCount",
                table: "CrmThreads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ExternalTimestamp",
                table: "CrmMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaMime",
                table: "CrmMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaType",
                table: "CrmMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaUrl",
                table: "CrmMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaMessageId",
                table: "CrmMessages",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedTo",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "CustomerDisplayName",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "CustomerEmail",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "CustomerPhone",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "CustomerPlatformId",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "LastMessagePreview",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "MainParticipant",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "ThreadKey",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "UnreadCount",
                table: "CrmThreads");

            migrationBuilder.DropColumn(
                name: "ExternalTimestamp",
                table: "CrmMessages");

            migrationBuilder.DropColumn(
                name: "MediaMime",
                table: "CrmMessages");

            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "CrmMessages");

            migrationBuilder.DropColumn(
                name: "MediaUrl",
                table: "CrmMessages");

            migrationBuilder.DropColumn(
                name: "WaMessageId",
                table: "CrmMessages");
        }
    }
}
