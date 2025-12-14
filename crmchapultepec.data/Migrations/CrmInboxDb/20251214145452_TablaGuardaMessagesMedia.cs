using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace crmchapultepec.data.Migrations.CrmInboxDb
{
    /// <inheritdoc />
    public partial class TablaGuardaMessagesMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmMessageMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FileLength = table.Column<long>(type: "bigint", nullable: true),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    MediaUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DirectPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MediaKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileSha256 = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileEncSha256 = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MediaKeyTimestamp = table.Column<long>(type: "bigint", nullable: true),
                    ThumbnailBase64 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmMessageMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmMessageMedia_CrmMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "CrmMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmMessageMedia_MessageId",
                table: "CrmMessageMedia",
                column: "MessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmMessageMedia");
        }
    }
}
