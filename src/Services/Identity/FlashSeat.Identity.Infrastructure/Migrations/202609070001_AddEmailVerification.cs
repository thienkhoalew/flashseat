using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSeat.Identity.Infrastructure.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("202609070001_AddEmailVerification")]
public partial class AddEmailVerification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsEmailVerified",
            table: "users",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "EmailVerificationCode",
            table: "users",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "EmailVerificationCodeExpiresAt",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("UPDATE users SET \"IsEmailVerified\" = TRUE;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsEmailVerified",
            table: "users");

        migrationBuilder.DropColumn(
            name: "EmailVerificationCode",
            table: "users");

        migrationBuilder.DropColumn(
            name: "EmailVerificationCodeExpiresAt",
            table: "users");
    }
}
