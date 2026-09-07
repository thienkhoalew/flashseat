using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSeat.Events.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddSeatLayout : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "LayoutX",
            table: "seats",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "LayoutY",
            table: "seats",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StageShape",
            table: "events",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Proscenium");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LayoutX",
            table: "seats");

        migrationBuilder.DropColumn(
            name: "LayoutY",
            table: "seats");

        migrationBuilder.DropColumn(
            name: "StageShape",
            table: "events");
    }
}
