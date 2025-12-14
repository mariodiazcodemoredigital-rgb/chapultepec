using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace crmchapultepec.data.Migrations.CrmInboxDb
{
    /// <inheritdoc />
    public partial class WebhookTableIntermediaUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerDisplayName",
                table: "EvolutionRawPayloads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhone",
                table: "EvolutionRawPayloads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Event",
                table: "EvolutionRawPayloads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FromMe",
                table: "EvolutionRawPayloads",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instance",
                table: "EvolutionRawPayloads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MessageDateUtc",
                table: "EvolutionRawPayloads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessageType",
                table: "EvolutionRawPayloads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteJid",
                table: "EvolutionRawPayloads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sender",
                table: "EvolutionRawPayloads",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerDisplayName",
                table: "EvolutionRawPayloads");

            migrationBuilder.DropColumn(
                name: "CustomerPhone",
                table: "EvolutionRawPayloads");

            migrationBuilder.DropColumn(
                name: "Event",
                table: "EvolutionRawPayloads");

            migrationBuilder.DropColumn(
                name: "FromMe",
                table: "EvolutionRawPayloads");

            migrationBuilder.DropColumn(
                name: "Instance",
                table: "EvolutionRawPayloads");

            migrationBuilder.DropColumn(
                name: "MessageDateUtc",
                table: "EvolutionRawPayloads");

            migrationBuilder.DropColumn(
                name: "MessageType",
                table: "EvolutionRawPayloads");

            migrationBuilder.DropColumn(
                name: "RemoteJid",
                table: "EvolutionRawPayloads");

            migrationBuilder.DropColumn(
                name: "Sender",
                table: "EvolutionRawPayloads");
        }
    }
}
