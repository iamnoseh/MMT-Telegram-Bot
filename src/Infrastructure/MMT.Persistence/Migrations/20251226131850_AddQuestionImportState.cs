using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMT.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionImportState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImportSubjectId",
                table: "UserStates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuestionImportStep",
                table: "UserStates",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportSubjectId",
                table: "UserStates");

            migrationBuilder.DropColumn(
                name: "QuestionImportStep",
                table: "UserStates");
        }
    }
}
