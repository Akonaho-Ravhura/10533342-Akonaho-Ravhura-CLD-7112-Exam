using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Migrations
{
    /// <inheritdoc />
    public partial class DroneImagesInBlobStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ImageUrl",
                table: "DroneImages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "BlobName",
                table: "DroneImages",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "DroneImages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlobName",
                table: "DroneImages");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "DroneImages");

            migrationBuilder.AlterColumn<string>(
                name: "ImageUrl",
                table: "DroneImages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
