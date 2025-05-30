using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramBot.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Options_OptionId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_OptionId",
                table: "Questions");

            migrationBuilder.RenameColumn(
                name: "Timestamp",
                table: "UserResponses",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "OptionId",
                table: "Questions",
                newName: "SubjectId");

            migrationBuilder.RenameColumn(
                name: "QuestionId",
                table: "Questions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "ThirdVariant",
                table: "Options",
                newName: "OptionD");

            migrationBuilder.RenameColumn(
                name: "SecondVariant",
                table: "Options",
                newName: "OptionC");

            migrationBuilder.RenameColumn(
                name: "FourthVariant",
                table: "Options",
                newName: "OptionB");

            migrationBuilder.RenameColumn(
                name: "FirstVariant",
                table: "Options",
                newName: "OptionA");

            migrationBuilder.RenameColumn(
                name: "Answer",
                table: "Options",
                newName: "CorrectAnswer");

            migrationBuilder.RenameColumn(
                name: "OptionId",
                table: "Options",
                newName: "Id");

            migrationBuilder.AddColumn<bool>(
                name: "IsLeft",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "UserResponses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuestionId",
                table: "Options",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserResponses_QuestionId",
                table: "UserResponses",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserResponses_UserId",
                table: "UserResponses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SubjectId",
                table: "Questions",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Options_QuestionId",
                table: "Options",
                column: "QuestionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Options_Questions_QuestionId",
                table: "Options",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Subjects_SubjectId",
                table: "Questions",
                column: "SubjectId",
                principalTable: "Subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserResponses_Questions_QuestionId",
                table: "UserResponses",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserResponses_Users_UserId",
                table: "UserResponses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Options_Questions_QuestionId",
                table: "Options");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Subjects_SubjectId",
                table: "Questions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserResponses_Questions_QuestionId",
                table: "UserResponses");

            migrationBuilder.DropForeignKey(
                name: "FK_UserResponses_Users_UserId",
                table: "UserResponses");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_UserResponses_QuestionId",
                table: "UserResponses");

            migrationBuilder.DropIndex(
                name: "IX_UserResponses_UserId",
                table: "UserResponses");

            migrationBuilder.DropIndex(
                name: "IX_Questions_SubjectId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Options_QuestionId",
                table: "Options");

            migrationBuilder.DropColumn(
                name: "IsLeft",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "UserResponses");

            migrationBuilder.DropColumn(
                name: "QuestionId",
                table: "Options");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "UserResponses",
                newName: "Timestamp");

            migrationBuilder.RenameColumn(
                name: "SubjectId",
                table: "Questions",
                newName: "OptionId");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Questions",
                newName: "QuestionId");

            migrationBuilder.RenameColumn(
                name: "OptionD",
                table: "Options",
                newName: "ThirdVariant");

            migrationBuilder.RenameColumn(
                name: "OptionC",
                table: "Options",
                newName: "SecondVariant");

            migrationBuilder.RenameColumn(
                name: "OptionB",
                table: "Options",
                newName: "FourthVariant");

            migrationBuilder.RenameColumn(
                name: "OptionA",
                table: "Options",
                newName: "FirstVariant");

            migrationBuilder.RenameColumn(
                name: "CorrectAnswer",
                table: "Options",
                newName: "Answer");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Options",
                newName: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_OptionId",
                table: "Questions",
                column: "OptionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Options_OptionId",
                table: "Questions",
                column: "OptionId",
                principalTable: "Options",
                principalColumn: "OptionId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
