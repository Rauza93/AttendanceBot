using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AttendanceBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AttendanceDbContext))]
[Migration("202610070001_AddWebsiteConfigurations")]
public sealed class AddWebsiteConfigurations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS WebsiteConfigurations (Id INTEGER NOT NULL CONSTRAINT PK_WebsiteConfigurations PRIMARY KEY AUTOINCREMENT, ProviderType TEXT NOT NULL, Name TEXT NOT NULL, Enabled INTEGER NOT NULL, BaseUrl TEXT NOT NULL, LoginUrl TEXT NOT NULL, AttendanceUrl TEXT NOT NULL, Notes TEXT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("WebsiteConfigurations");
}
