using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Kroeg.Server.Models;

namespace Kroeg.Server.Migrations
{
    [DbContext(typeof(APContext))]
    [Migration("20170630223001_AddIsPublic")]
    partial class AddIsPublic
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .HasAnnotation("ProductVersion", "1.1.1");

            modelBuilder.Entity("Kroeg.Server.Models.APEntity", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<bool>("IsOwner");

                    b.Property<string>("SerializedData")
                        .HasColumnType("jsonb");

                    b.Property<string>("Type");

                    b.Property<DateTime>("Updated");

                    b.HasKey("Id");

                    b.ToTable("Entities");
                });

            modelBuilder.Entity("Kroeg.Server.Models.APUser", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int>("AccessFailedCount");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken();

                    b.Property<string>("Email")
                        .HasMaxLength(256);

                    b.Property<bool>("EmailConfirmed");

                    b.Property<bool>("LockoutEnabled");

                    b.Property<DateTimeOffset?>("LockoutEnd");

                    b.Property<string>("NormalizedEmail")
                        .HasMaxLength(256);

                    b.Property<string>("NormalizedUserName")
                        .HasMaxLength(256);

                    b.Property<string>("PasswordHash");

                    b.Property<string>("PhoneNumber");

                    b.Property<bool>("PhoneNumberConfirmed");

                    b.Property<string>("SecurityStamp");

                    b.Property<bool>("TwoFactorEnabled");

                    b.Property<string>("UserName")
                        .HasMaxLength(256);

                    b.HasKey("Id");

                    b.HasIndex("NormalizedEmail")
                        .HasName("EmailIndex");

                    b.HasIndex("NormalizedUserName")
                        .IsUnique()
                        .HasName("UserNameIndex");

                    b.ToTable("AspNetUsers");
                });

            modelBuilder.Entity("Kroeg.Server.Models.CollectionItem", b =>
                {
                    b.Property<int>("CollectionItemId")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("CollectionId");

                    b.Property<string>("ElementId");

                    b.Property<bool>("IsPublic");

                    b.HasKey("CollectionItemId");

                    b.HasIndex("CollectionId");

                    b.HasIndex("ElementId");

                    b.ToTable("CollectionItems");
                });

            modelBuilder.Entity("Kroeg.Server.Models.EventQueueItem", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Action");

                    b.Property<DateTime>("Added");

                    b.Property<int>("AttemptCount");

                    b.Property<string>("Data");

                    b.Property<DateTime>("NextAttempt");

                    b.HasKey("Id");

                    b.ToTable("EventQueue");
                });

            modelBuilder.Entity("Kroeg.Server.Models.SalmonKey", b =>
                {
                    b.Property<int>("SalmonKeyId")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("EntityId");

                    b.Property<string>("PrivateKey");

                    b.HasKey("SalmonKeyId");

                    b.HasIndex("EntityId");

                    b.ToTable("SalmonKeys");
                });

            modelBuilder.Entity("Kroeg.Server.Models.UserActorPermission", b =>
                {
                    b.Property<int>("UserActorPermissionId")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("ActorId");

                    b.Property<bool>("IsAdmin");

                    b.Property<string>("UserId");

                    b.HasKey("UserActorPermissionId");

                    b.HasIndex("ActorId");

                    b.HasIndex("UserId");

                    b.ToTable("UserActorPermissions");
                });

            modelBuilder.Entity("Kroeg.Server.Models.WebSubClient", b =>
                {
                    b.Property<int>("WebSubClientId")
                        .ValueGeneratedOnAdd();

                    b.Property<DateTime>("Expiry");

                    b.Property<string>("ForUserId");

                    b.Property<string>("Secret");

                    b.Property<string>("TargetUserId");

                    b.Property<string>("Topic");

                    b.HasKey("WebSubClientId");

                    b.HasIndex("ForUserId");

                    b.HasIndex("TargetUserId");

                    b.ToTable("WebSubClients");
                });

            modelBuilder.Entity("Kroeg.Server.Models.WebsubSubscription", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Callback");

                    b.Property<DateTime>("Expiry");

                    b.Property<string>("Secret");

                    b.Property<string>("UserId");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("WebsubSubscriptions");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityRole", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken();

                    b.Property<string>("Name")
                        .HasMaxLength(256);

                    b.Property<string>("NormalizedName")
                        .HasMaxLength(256);

                    b.HasKey("Id");

                    b.HasIndex("NormalizedName")
                        .IsUnique()
                        .HasName("RoleNameIndex");

                    b.ToTable("AspNetRoles");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityRoleClaim<string>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("ClaimType");

                    b.Property<string>("ClaimValue");

                    b.Property<string>("RoleId")
                        .IsRequired();

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetRoleClaims");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityUserClaim<string>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("ClaimType");

                    b.Property<string>("ClaimValue");

                    b.Property<string>("UserId")
                        .IsRequired();

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserClaims");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityUserLogin<string>", b =>
                {
                    b.Property<string>("LoginProvider");

                    b.Property<string>("ProviderKey");

                    b.Property<string>("ProviderDisplayName");

                    b.Property<string>("UserId")
                        .IsRequired();

                    b.HasKey("LoginProvider", "ProviderKey");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserLogins");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityUserRole<string>", b =>
                {
                    b.Property<string>("UserId");

                    b.Property<string>("RoleId");

                    b.HasKey("UserId", "RoleId");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetUserRoles");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityUserToken<string>", b =>
                {
                    b.Property<string>("UserId");

                    b.Property<string>("LoginProvider");

                    b.Property<string>("Name");

                    b.Property<string>("Value");

                    b.HasKey("UserId", "LoginProvider", "Name");

                    b.ToTable("AspNetUserTokens");
                });

            modelBuilder.Entity("Kroeg.Server.Models.CollectionItem", b =>
                {
                    b.HasOne("Kroeg.Server.Models.APEntity", "Collection")
                        .WithMany()
                        .HasForeignKey("CollectionId");

                    b.HasOne("Kroeg.Server.Models.APEntity", "Element")
                        .WithMany()
                        .HasForeignKey("ElementId");
                });

            modelBuilder.Entity("Kroeg.Server.Models.SalmonKey", b =>
                {
                    b.HasOne("Kroeg.Server.Models.APEntity", "Entity")
                        .WithMany()
                        .HasForeignKey("EntityId");
                });

            modelBuilder.Entity("Kroeg.Server.Models.UserActorPermission", b =>
                {
                    b.HasOne("Kroeg.Server.Models.APEntity", "Actor")
                        .WithMany()
                        .HasForeignKey("ActorId");

                    b.HasOne("Kroeg.Server.Models.APUser", "User")
                        .WithMany()
                        .HasForeignKey("UserId");
                });

            modelBuilder.Entity("Kroeg.Server.Models.WebSubClient", b =>
                {
                    b.HasOne("Kroeg.Server.Models.APEntity", "ForUser")
                        .WithMany()
                        .HasForeignKey("ForUserId");

                    b.HasOne("Kroeg.Server.Models.APEntity", "TargetUser")
                        .WithMany()
                        .HasForeignKey("TargetUserId");
                });

            modelBuilder.Entity("Kroeg.Server.Models.WebsubSubscription", b =>
                {
                    b.HasOne("Kroeg.Server.Models.APEntity", "User")
                        .WithMany()
                        .HasForeignKey("UserId");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityRoleClaim<string>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityRole")
                        .WithMany("Claims")
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityUserClaim<string>", b =>
                {
                    b.HasOne("Kroeg.Server.Models.APUser")
                        .WithMany("Claims")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityUserLogin<string>", b =>
                {
                    b.HasOne("Kroeg.Server.Models.APUser")
                        .WithMany("Logins")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityUserRole<string>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityRole")
                        .WithMany("Users")
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Kroeg.Server.Models.APUser")
                        .WithMany("Roles")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });
        }
    }
}
