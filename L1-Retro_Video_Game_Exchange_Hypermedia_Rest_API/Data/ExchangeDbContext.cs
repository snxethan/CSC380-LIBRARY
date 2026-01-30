using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Models;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Data
{
    public class ExchangeDbContext : DbContext
    {
        public ExchangeDbContext(DbContextOptions<ExchangeDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Game> Games { get; set; }
        public DbSet<TradeOffer> TradeOffers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TradeOffer>()
                .Property(o => o.Status)
                .HasConversion<string>();

            modelBuilder.Entity<TradeOffer>()
                .HasOne(o => o.RequestedGame)
                .WithMany(g => g.RequestedInOffers)
                .HasForeignKey(o => o.RequestedGameId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TradeOffer>()
                .HasOne(o => o.OfferedGame)
                .WithMany(g => g.OfferedInOffers)
                .HasForeignKey(o => o.OfferedGameId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TradeOffer>()
                .HasOne(o => o.RequesterUser)
                .WithMany(u => u.SentOffers)
                .HasForeignKey(o => o.RequesterUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TradeOffer>()
                .HasOne(o => o.OwnerUser)
                .WithMany(u => u.ReceivedOffers)
                .HasForeignKey(o => o.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
