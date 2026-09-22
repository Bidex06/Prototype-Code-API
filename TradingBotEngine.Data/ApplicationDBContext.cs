using Microsoft.EntityFrameworkCore;
using TradingBotEngine.Data.Models;

namespace TradingBotEngine.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Trade> Trades => Set<Trade>();
        public DbSet<Signal> Signals => Set<Signal>();
        public DbSet<Position> Positions => Set<Position>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Subscription> Subscriptions => Set<Subscription>();
        public DbSet<BrokerConnection> BrokerConnections => Set<BrokerConnection>();
        public DbSet<RiskSetting> RiskSettings => Set<RiskSetting>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<TrackedSymbol> TrackedSymbols => Set<TrackedSymbol>();
        public DbSet<TradingBot> TradingBots => Set<TradingBot>();
        public DbSet<TradingBotTrackedSymbol> TradingBotTrackedSymbols => Set<TradingBotTrackedSymbol>();

        // Exchange order reconciliation
        public DbSet<ExchangeOrder> ExchangeOrders => Set<ExchangeOrder>();
        public DbSet<ExchangeFill> ExchangeFills => Set<ExchangeFill>();

public DbSet<ExchangeBalance> ExchangeBalances => Set<ExchangeBalance>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => new { u.IsActive, u.IsAutoTradeEnabled });

            modelBuilder.Entity<User>()
                .Property(u => u.Role)
                .HasDefaultValue("User");

            modelBuilder.Entity<BrokerConnection>()
                .HasIndex(b => new { b.UserId, b.BrokerName })
                .IsUnique();

            modelBuilder.Entity<BrokerConnection>()
                .HasIndex(b => new { b.UserId, b.IsActive });

            modelBuilder.Entity<BrokerConnection>()
                .Property(b => b.IsTestnet)
                .HasDefaultValue(true);

            modelBuilder.Entity<BrokerConnection>()
                .Property(b => b.IsLiveTradingEnabled)
                .HasDefaultValue(false);

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(t => t.TokenHash)
                .IsUnique();

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(t => new { t.UserId, t.ExpiresAt });

            modelBuilder.Entity<RefreshToken>()
                .HasOne(t => t.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Subscription>()
                .HasOne(s => s.User)
                .WithOne(u => u.Subscription)
                .HasForeignKey<Subscription>(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Subscription>()
                .HasIndex(s => new { s.IsActive, s.EndDate });

            modelBuilder.Entity<Trade>()
                .HasOne(t => t.User)
                .WithMany(u => u.Trades)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Trade>()
                .HasIndex(t => new { t.UserId, t.Status, t.EntryTime });

            modelBuilder.Entity<Trade>()
                .HasIndex(t => new { t.UserId, t.Symbol, t.OrderId });

            modelBuilder.Entity<Trade>()
                .HasIndex(t => new { t.UserId, t.IdempotencyKey })
                .IsUnique()
                .HasFilter("\"IdempotencyKey\" IS NOT NULL");

            modelBuilder.Entity<Position>()
                .HasOne(p => p.User)
                .WithMany(u => u.Positions)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Position>()
                .HasIndex(p => new { p.UserId, p.Status });

            modelBuilder.Entity<Signal>()
                .HasOne(s => s.User)
                .WithMany(u => u.Signals)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Signal>()
                .HasIndex(s => new { s.UserId, s.Symbol, s.GeneratedAt });

            modelBuilder.Entity<Signal>()
                .HasOne(s => s.ExecutedTrade)
                .WithMany()
                .HasForeignKey(s => s.TradeId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Payment>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Payment>()
                .HasIndex(p => new { p.UserId, p.CreatedAt });

            modelBuilder.Entity<Payment>()
                .HasIndex(p => new { p.Gateway, p.TransactionId })
                .IsUnique();

            modelBuilder.Entity<RiskSetting>()
                .HasOne(r => r.User)
                .WithOne(u => u.RiskSetting)
                .HasForeignKey<RiskSetting>(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RiskSetting>()
                .HasIndex(r => r.UserId)
                .IsUnique();

            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => new { a.UserId, a.CreatedAt });

            modelBuilder.Entity<TrackedSymbol>()
                .HasOne(s => s.User)
                .WithMany(u => u.TrackedSymbols)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TrackedSymbol>()
                .HasIndex(s => new { s.UserId, s.Exchange, s.Symbol })
                .IsUnique();

            modelBuilder.Entity<TrackedSymbol>()
                .HasIndex(s => new { s.UserId, s.IsEnabled });

            modelBuilder.Entity<TradingBot>()
                .HasOne(b => b.User)
                .WithMany(u => u.TradingBots)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TradingBot>()
                .HasOne(b => b.BrokerConnection)
                .WithMany()
                .HasForeignKey(b => b.BrokerConnectionId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<TradingBot>()
                .HasIndex(b => new { b.UserId, b.Name })
                .IsUnique();

            modelBuilder.Entity<TradingBot>()
                .HasIndex(b => new { b.UserId, b.IsEnabled, b.IsRunning });

            modelBuilder.Entity<TradingBotTrackedSymbol>()
                .HasKey(x => new { x.TradingBotId, x.TrackedSymbolId });

            modelBuilder.Entity<TradingBotTrackedSymbol>()
                .HasOne(x => x.TradingBot)
                .WithMany(b => b.TrackedSymbols)
                .HasForeignKey(x => x.TradingBotId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TradingBotTrackedSymbol>()
                .HasOne(x => x.TrackedSymbol)
                .WithMany()
                .HasForeignKey(x => x.TrackedSymbolId)
                .OnDelete(DeleteBehavior.Cascade);

            // ============================================================
            // Exchange order reconciliation
            // ============================================================

            modelBuilder.Entity<ExchangeOrder>()
                .HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExchangeOrder>()
                .HasOne(e => e.Trade)
                .WithMany()
                .HasForeignKey(e => e.TradeId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExchangeOrder>()
                .HasIndex(e => new { e.UserId, e.ExchangeOrderId })
                .IsUnique();

            modelBuilder.Entity<ExchangeOrder>()
                .HasIndex(e => new { e.UserId, e.Status });

            modelBuilder.Entity<ExchangeOrder>()
                .HasIndex(e => new { e.UserId, e.Symbol, e.Status });

            modelBuilder.Entity<ExchangeOrder>()
                .HasOne(e => e.ParentExchangeOrder)
                .WithMany()
                .HasForeignKey(e => e.ParentExchangeOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ExchangeOrder>()
                .HasIndex(e => new { e.TradeId, e.OrderRole });

            modelBuilder.Entity<ExchangeFill>()
                .HasOne(e => e.ExchangeOrder)
                .WithMany(e => e.Fills)
                .HasForeignKey(e => e.ExchangeOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExchangeFill>()
                .HasIndex(e => new { e.ExchangeOrderId, e.ExchangeFillId })
                .IsUnique();
        }
    }
}
