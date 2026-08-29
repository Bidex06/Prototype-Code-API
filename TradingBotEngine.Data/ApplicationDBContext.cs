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
        }
    }
}
