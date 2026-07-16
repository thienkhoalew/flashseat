using FlashSeat.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FlashSeat.Identity.Infrastructure.Migrations;

[DbContext(typeof(IdentityDbContext))]
sealed partial class IdentityDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "8.0.12");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("FlashSeat.Identity.Domain.RefreshToken", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("RevokedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.HasKey("Id");
            b.HasIndex("TokenHash").IsUnique();
            b.HasIndex("UserId");
            b.ToTable("refresh_tokens");
        });

        modelBuilder.Entity("FlashSeat.Identity.Domain.User", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Email").IsRequired().HasMaxLength(254).HasColumnType("character varying(254)");
            b.Property<string>("FullName").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("PasswordHash").IsRequired().HasMaxLength(512).HasColumnType("character varying(512)");
            b.Property<UserRole>("Role").HasConversion<string>().HasMaxLength(20).HasColumnType("character varying(20)");
            b.HasKey("Id");
            b.HasIndex("Email").IsUnique();
            b.ToTable("users");
        });

        modelBuilder.Entity("FlashSeat.Identity.Domain.RefreshToken", b =>
        {
            b.HasOne("FlashSeat.Identity.Domain.User", "User")
                .WithMany("RefreshTokens")
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("FlashSeat.Identity.Domain.User", b => b.Navigation("RefreshTokens"));
#pragma warning restore 612, 618
    }
}
