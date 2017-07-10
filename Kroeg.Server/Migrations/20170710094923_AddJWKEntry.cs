using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kroeg.Server.Migrations
{
    public partial class AddJWKEntry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JsonWebKeys",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    OwnerId = table.Column<string>(nullable: false),
                    SerializedData = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JsonWebKeys", x => new { x.Id, x.OwnerId });
                    table.ForeignKey(
                        name: "FK_JsonWebKeys_Entities_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JsonWebKeys_OwnerId",
                table: "JsonWebKeys",
                column: "OwnerId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JsonWebKeys");
        }
    }
}
