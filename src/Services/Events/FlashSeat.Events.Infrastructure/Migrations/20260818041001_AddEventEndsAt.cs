using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSeat.Events.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventEndsAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndsAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"events\" SET \"EndsAt\" = \"StartsAt\" + make_interval(hours => 3) WHERE \"EndsAt\" IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EndsAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndsAt",
                table: "events");
        }
    }
}
