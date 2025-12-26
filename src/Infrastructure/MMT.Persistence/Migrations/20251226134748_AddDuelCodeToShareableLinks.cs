using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMT.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDuelCodeToShareableLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Duels_Users_OpponentId",
                table: "Duels");

            migrationBuilder.AlterColumn<int>(
                name: "OpponentId",
                table: "Duels",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "DuelCode",
                table: "Duels",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_Duels_Users_OpponentId",
                table: "Duels",
                column: "OpponentId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Duels_Users_OpponentId",
                table: "Duels");

            migrationBuilder.DropColumn(
                name: "DuelCode",
                table: "Duels");

            migrationBuilder.AlterColumn<int>(
                name: "OpponentId",
                table: "Duels",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Duels_Users_OpponentId",
                table: "Duels",
                column: "OpponentId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
