using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace crmchapultepec.data.Migrations.CrmInboxDb
{
    /// <inheritdoc />
    public partial class TablesEvolutionWebhook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmThreads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ThreadId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BusinessAccountId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastMessageUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmContactDbs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ThreadRefId = table.Column<int>(type: "int", nullable: false),
                    ThreadId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    BusinessAccountId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContactKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Company = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlatformId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmContactDbs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmContactDbs_CrmThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "CrmThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ThreadRefId = table.Column<int>(type: "int", nullable: false),
                    Sender = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DirectionIn = table.Column<bool>(type: "bit", nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RawHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmMessages_CrmThreads_ThreadRefId",
                        column: x => x.ThreadRefId,
                        principalTable: "CrmThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PipelineHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ThreadRefId = table.Column<int>(type: "int", nullable: false),
                    PipelineName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StageName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineHistories_CrmThreads_ThreadRefId",
                        column: x => x.ThreadRefId,
                        principalTable: "CrmThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmContactDbs_ThreadId",
                table: "CrmContactDbs",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmMessage_ExternalId",
                table: "CrmMessages",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmMessage_RawHash",
                table: "CrmMessages",
                column: "RawHash");

            migrationBuilder.CreateIndex(
                name: "IX_CrmMessages_ThreadRefId",
                table: "CrmMessages",
                column: "ThreadRefId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmThreads_ThreadId",
                table: "CrmThreads",
                column: "ThreadId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PipelineHistories_ThreadRefId",
                table: "PipelineHistories",
                column: "ThreadRefId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmContactDbs");

            migrationBuilder.DropTable(
                name: "CrmMessages");

            migrationBuilder.DropTable(
                name: "PipelineHistories");

            migrationBuilder.DropTable(
                name: "CrmThreads");
        }
    }
}
