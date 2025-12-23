using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace crmchapultepec.data.Migrations
{
    /// <inheritdoc />
    public partial class crmadmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CRMEquipo",
                columns: table => new
                {
                    EquipoId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CRMEquipo", x => x.EquipoId);
                });

            migrationBuilder.CreateTable(
                name: "CRMUsuario",
                columns: table => new
                {
                    UsuarioId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CRMUsuario", x => x.UsuarioId);
                });

            migrationBuilder.CreateTable(
                name: "CRMEquipoUsuario",
                columns: table => new
                {
                    EquipoUsuarioId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipoId = table.Column<int>(type: "int", nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CRMEquipoUsuario", x => x.EquipoUsuarioId);
                    table.ForeignKey(
                        name: "FK_CRMEquipoUsuario_CRMEquipo_EquipoId",
                        column: x => x.EquipoId,
                        principalTable: "CRMEquipo",
                        principalColumn: "EquipoId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CRMEquipoUsuario_CRMUsuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "CRMUsuario",
                        principalColumn: "UsuarioId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CRMEquipoUsuario_EquipoId",
                table: "CRMEquipoUsuario",
                column: "EquipoId");

            migrationBuilder.CreateIndex(
                name: "IX_CRMEquipoUsuario_UsuarioId",
                table: "CRMEquipoUsuario",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CRMEquipoUsuario");

            migrationBuilder.DropTable(
                name: "CRMEquipo");

            migrationBuilder.DropTable(
                name: "CRMUsuario");
        }
    }
}
