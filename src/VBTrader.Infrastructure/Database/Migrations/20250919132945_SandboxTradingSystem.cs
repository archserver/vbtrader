using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VBTrader.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class SandboxTradingSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historical_data_cache",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    High = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    AdjustedClose = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TimeFrame = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsPreMarket = table.Column<bool>(type: "boolean", nullable: false),
                    IsAfterHours = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historical_data_cache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sandbox_sessions",
                columns: table => new
                {
                    SandboxSessionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SessionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InitialBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WatchedSymbols = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sandbox_sessions", x => x.SandboxSessionId);
                });

            migrationBuilder.CreateTable(
                name: "sandbox_performance_snapshots",
                columns: table => new
                {
                    SnapshotId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SandboxSessionId = table.Column<int>(type: "integer", nullable: false),
                    SnapshotTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AccountBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PortfolioValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DayChange = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DayChangePercent = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    UnrealizedPnL = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RealizedPnL = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActivePositions = table.Column<int>(type: "integer", nullable: false),
                    PositionsSnapshot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sandbox_performance_snapshots", x => x.SnapshotId);
                    table.ForeignKey(
                        name: "FK_sandbox_performance_snapshots_sandbox_sessions_SandboxSessi~",
                        column: x => x.SandboxSessionId,
                        principalTable: "sandbox_sessions",
                        principalColumn: "SandboxSessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sandbox_settings",
                columns: table => new
                {
                    SettingsId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SandboxSessionId = table.Column<int>(type: "integer", nullable: false),
                    AutoAdvanceTime = table.Column<bool>(type: "boolean", nullable: false),
                    TimeAdvanceIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    SkipWeekends = table.Column<bool>(type: "boolean", nullable: false),
                    SkipHolidays = table.Column<bool>(type: "boolean", nullable: false),
                    EnableSlippage = table.Column<bool>(type: "boolean", nullable: false),
                    SlippagePercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    EnableCommissions = table.Column<bool>(type: "boolean", nullable: false),
                    CommissionPerTrade = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    MaxPositionsPerSymbol = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sandbox_settings", x => x.SettingsId);
                    table.ForeignKey(
                        name: "FK_sandbox_settings_sandbox_sessions_SandboxSessionId",
                        column: x => x.SandboxSessionId,
                        principalTable: "sandbox_sessions",
                        principalColumn: "SandboxSessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sandbox_trades",
                columns: table => new
                {
                    TradeId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SandboxSessionId = table.Column<int>(type: "integer", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Commission = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrderType = table.Column<int>(type: "integer", nullable: false),
                    LimitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    HistoricalDataTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sandbox_trades", x => x.TradeId);
                    table.ForeignKey(
                        name: "FK_sandbox_trades_sandbox_sessions_SandboxSessionId",
                        column: x => x.SandboxSessionId,
                        principalTable: "sandbox_sessions",
                        principalColumn: "SandboxSessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalDataCache_ExpiresAt",
                table: "historical_data_cache",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalDataCache_Symbol_TimeFrame_Timestamp",
                table: "historical_data_cache",
                columns: new[] { "Symbol", "TimeFrame", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalDataCache_Symbol_Timestamp",
                table: "historical_data_cache",
                columns: new[] { "Symbol", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxPerformanceSnapshots_SandboxSessionId",
                table: "sandbox_performance_snapshots",
                column: "SandboxSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxPerformanceSnapshots_SandboxSessionId_SnapshotTime",
                table: "sandbox_performance_snapshots",
                columns: new[] { "SandboxSessionId", "SnapshotTime" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_IsActive",
                table: "sandbox_sessions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_UserId",
                table: "sandbox_sessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_UserId_IsActive",
                table: "sandbox_sessions",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSettings_SandboxSessionId",
                table: "sandbox_settings",
                column: "SandboxSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxTrades_ExecutedAt",
                table: "sandbox_trades",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxTrades_SandboxSessionId",
                table: "sandbox_trades",
                column: "SandboxSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxTrades_Symbol_ExecutedAt",
                table: "sandbox_trades",
                columns: new[] { "Symbol", "ExecutedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historical_data_cache");

            migrationBuilder.DropTable(
                name: "sandbox_performance_snapshots");

            migrationBuilder.DropTable(
                name: "sandbox_settings");

            migrationBuilder.DropTable(
                name: "sandbox_trades");

            migrationBuilder.DropTable(
                name: "sandbox_sessions");
        }
    }
}
