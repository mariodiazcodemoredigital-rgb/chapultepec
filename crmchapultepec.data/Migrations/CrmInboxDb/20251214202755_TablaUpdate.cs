using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace crmchapultepec.data.Migrations.CrmInboxDb
{
    /// <inheritdoc />
    public partial class TablaUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CrmMessageMedia_CrmMessages_MessageId",
                table: "CrmMessageMedia");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CrmMessageMedia",
                table: "CrmMessageMedia");

            migrationBuilder.RenameTable(
                name: "CrmMessageMedia",
                newName: "CrmMessageMedias");

            migrationBuilder.RenameIndex(
                name: "IX_CrmMessageMedia_MessageId",
                table: "CrmMessageMedias",
                newName: "IX_CrmMessageMedias_MessageId");

            migrationBuilder.AddColumn<int>(
                name: "statustest",
                table: "CrmMessageMedias",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_CrmMessageMedias",
                table: "CrmMessageMedias",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CrmMessageMedias_CrmMessages_MessageId",
                table: "CrmMessageMedias",
                column: "MessageId",
                principalTable: "CrmMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CrmMessageMedias_CrmMessages_MessageId",
                table: "CrmMessageMedias");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CrmMessageMedias",
                table: "CrmMessageMedias");

            migrationBuilder.DropColumn(
                name: "statustest",
                table: "CrmMessageMedias");

            migrationBuilder.RenameTable(
                name: "CrmMessageMedias",
                newName: "CrmMessageMedia");

            migrationBuilder.RenameIndex(
                name: "IX_CrmMessageMedias_MessageId",
                table: "CrmMessageMedia",
                newName: "IX_CrmMessageMedia_MessageId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CrmMessageMedia",
                table: "CrmMessageMedia",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CrmMessageMedia_CrmMessages_MessageId",
                table: "CrmMessageMedia",
                column: "MessageId",
                principalTable: "CrmMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
