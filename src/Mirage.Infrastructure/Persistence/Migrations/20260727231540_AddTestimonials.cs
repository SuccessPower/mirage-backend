using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestimonials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "testimonials",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaggedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_testimonials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_testimonials_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_testimonials_AspNetUsers_TaggedUserId",
                        column: x => x.TaggedUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "testimonial_comments",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestimonialId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_testimonial_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_testimonial_comments_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_testimonial_comments_testimonial_comments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalSchema: "mirage",
                        principalTable: "testimonial_comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_testimonial_comments_testimonials_TestimonialId",
                        column: x => x.TestimonialId,
                        principalSchema: "mirage",
                        principalTable: "testimonials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "testimonial_likes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestimonialId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_testimonial_likes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_testimonial_likes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_testimonial_likes_testimonials_TestimonialId",
                        column: x => x.TestimonialId,
                        principalSchema: "mirage",
                        principalTable: "testimonials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "testimonial_reads",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestimonialId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_testimonial_reads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_testimonial_reads_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_testimonial_reads_testimonials_TestimonialId",
                        column: x => x.TestimonialId,
                        principalSchema: "mirage",
                        principalTable: "testimonials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_testimonial_comments_AuthorUserId",
                schema: "mirage",
                table: "testimonial_comments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_testimonial_comments_ParentCommentId",
                schema: "mirage",
                table: "testimonial_comments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_testimonial_comments_TestimonialId_CreatedAt",
                schema: "mirage",
                table: "testimonial_comments",
                columns: new[] { "TestimonialId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_testimonial_likes_TestimonialId_UserId",
                schema: "mirage",
                table: "testimonial_likes",
                columns: new[] { "TestimonialId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_testimonial_likes_UserId",
                schema: "mirage",
                table: "testimonial_likes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_testimonial_reads_TestimonialId_UserId",
                schema: "mirage",
                table: "testimonial_reads",
                columns: new[] { "TestimonialId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_testimonial_reads_UserId",
                schema: "mirage",
                table: "testimonial_reads",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_testimonials_AuthorUserId",
                schema: "mirage",
                table: "testimonials",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_testimonials_CreatedAt",
                schema: "mirage",
                table: "testimonials",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_testimonials_TaggedUserId",
                schema: "mirage",
                table: "testimonials",
                column: "TaggedUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "testimonial_comments",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "testimonial_likes",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "testimonial_reads",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "testimonials",
                schema: "mirage");
        }
    }
}
