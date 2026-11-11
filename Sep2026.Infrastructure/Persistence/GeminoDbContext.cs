using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Sep2026.Infrastructure.Persistence.Entities;

namespace Sep2026.Infrastructure.Persistence;

public partial class GeminoDbContext : DbContext
{
    public GeminoDbContext(DbContextOptions<GeminoDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Holding> Holdings { get; set; }

    public virtual DbSet<MarketPriceSnapshot> MarketPriceSnapshots { get; set; }

    public virtual DbSet<Portfolio> Portfolios { get; set; }

    public virtual DbSet<Security> Securities { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Holding>(entity =>
        {
            entity.HasIndex(e => e.SecurityId, "IX_Holdings_SecurityId");

            entity.HasIndex(e => new { e.PortfolioId, e.SecurityId }, "UQ_Holdings_PortfolioId_SecurityId").IsUnique();

            entity.Property(e => e.HoldingId).HasDefaultValueSql("(newsequentialid())", "DF_Holdings_HoldingId");
            entity.Property(e => e.AveragePurchasePrice).HasColumnType("decimal(19, 8)");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.Portfolio).WithMany(p => p.Holdings)
                .HasForeignKey(d => d.PortfolioId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Holdings_Portfolios");

            entity.HasOne(d => d.Security).WithMany(p => p.Holdings)
                .HasForeignKey(d => d.SecurityId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Holdings_Securities");
        });

        modelBuilder.Entity<MarketPriceSnapshot>(entity =>
        {
            entity.HasKey(e => e.SnapshotId);

            entity.HasIndex(e => new { e.SecurityId, e.MarketTimestamp }, "IX_MarketPriceSnapshots_SecurityId_MarketTimestamp").IsDescending(false, true);

            entity.HasIndex(e => new { e.SecurityId, e.Provider, e.Feed, e.CurrencyType, e.MarketTimestamp }, "UQ_MarketPriceSnapshots_Observation").IsUnique();

            entity.Property(e => e.CurrencyType)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength()
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(e => e.Feed)
                .HasMaxLength(50)
                .UseCollation("Latin1_General_100_CI_AS");
            entity.Property(e => e.Price).HasColumnType("decimal(19, 8)");
            entity.Property(e => e.Provider)
                .HasMaxLength(50)
                .UseCollation("Latin1_General_100_CI_AS");
            entity.Property(e => e.ReceivedAt).HasDefaultValueSql("(todatetimeoffset(sysutcdatetime(),'+00:00'))", "DF_MarketPriceSnapshots_ReceivedAt");

            entity.HasOne(d => d.Security).WithMany(p => p.MarketPriceSnapshots)
                .HasForeignKey(d => d.SecurityId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MarketPriceSnapshots_Securities");
        });

        modelBuilder.Entity<Portfolio>(entity =>
        {
            entity.HasIndex(e => e.OwnerId, "IX_Portfolios_OwnerId");

            entity.Property(e => e.PortfolioId).HasDefaultValueSql("(newsequentialid())", "DF_Portfolios_PortfolioId");
            entity.Property(e => e.CashBalance).HasColumnType("decimal(19, 8)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(todatetimeoffset(sysutcdatetime(),'+00:00'))", "DF_Portfolios_CreatedAt");
            entity.Property(e => e.CurrencyType)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength()
                .UseCollation("Latin1_General_100_BIN2")
                .HasDefaultValue("USD", "DF_Portfolios_CurrencyType");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.Owner).WithMany(p => p.Portfolios)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Portfolios_Users");
        });

        modelBuilder.Entity<Security>(entity =>
        {
            entity.HasIndex(e => e.TickerSymbol, "UQ_Securities_TickerSymbol").IsUnique();

            entity.Property(e => e.SecurityId).HasDefaultValueSql("(newsequentialid())", "DF_Securities_SecurityId");
            entity.Property(e => e.CurrencyType)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength()
                .UseCollation("Latin1_General_100_BIN2")
                .HasDefaultValue("USD", "DF_Securities_CurrencyType");
            entity.Property(e => e.CurrentValue).HasColumnType("decimal(19, 8)");
            entity.Property(e => e.SecurityType).HasMaxLength(50);
            entity.Property(e => e.TickerSymbol)
                .HasMaxLength(32)
                .UseCollation("Latin1_General_100_CI_AS");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Email, "UQ_Users_Email").IsUnique();

            entity.Property(e => e.UserId).HasDefaultValueSql("(newsequentialid())", "DF_Users_UserId");
            entity.Property(e => e.Email)
                .HasMaxLength(254)
                .UseCollation("Latin1_General_100_CI_AS");
            entity.Property(e => e.Name).HasMaxLength(200);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
