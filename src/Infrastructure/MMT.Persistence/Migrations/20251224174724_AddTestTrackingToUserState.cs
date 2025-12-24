using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMT.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestTrackingToUserState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentSubjectId",
                table: "UserStates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TestQuestionsCount",
                table: "UserStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TestScore",
                table: "UserStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentSubjectId",
                table: "UserStates");

            migrationBuilder.DropColumn(
                name: "TestQuestionsCount",
                table: "UserStates");

            migrationBuilder.DropColumn(
                name: "TestScore",
                table: "UserStates");
        }
    }
}
