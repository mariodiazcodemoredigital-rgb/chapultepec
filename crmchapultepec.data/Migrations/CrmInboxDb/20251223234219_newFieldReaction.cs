using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace crmchapultepec.data.Migrations.CrmInboxDb
{
    /// <inheritdoc />
    public partial class newFieldReaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reaction",
                table: "CrmMessages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reaction",
                table: "CrmMessages");
        }
    }
}
