using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Models;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Data
{
    /// <summary>
    /// Entity Framework Core database context for the retro video game exchange.
    /// Manages three tables: <c>Users</c>, <c>Games</c>, and <c>TradeOffers</c>.
    /// </summary>
    public class ExchangeDbContext : DbContext
    {
        public ExchangeDbContext(DbContextOptions<ExchangeDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Game> Games { get; set; }

        /// <summary>
        /// Trade offers between users.  Each row represents a proposal by one
        /// user to swap their game (<c>OfferedGameId</c>) for another user's game
        /// (<c>RequestedGameId</c>).
        /// </summary>
        public DbSet<TradeOffer> TradeOffers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Store TradeOfferStatus enum as its string name ("Pending", "Accepted",
            // "Rejected") so the database is human-readable without a lookup table.
            modelBuilder.Entity<TradeOffer>()
                .Property(o => o.Status)
                .HasConversion<string>();

            // A game can appear in many offers as the RequestedGame.
            // Restrict delete: cannot delete a game that is referenced in an offer.
            modelBuilder.Entity<TradeOffer>()
                .HasOne(o => o.RequestedGame)
                .WithMany(g => g.RequestedInOffers)
                .HasForeignKey(o => o.RequestedGameId)
                .OnDelete(DeleteBehavior.Restrict);

            // A game can appear in many offers as the OfferedGame.
            modelBuilder.Entity<TradeOffer>()
                .HasOne(o => o.OfferedGame)
                .WithMany(g => g.OfferedInOffers)
                .HasForeignKey(o => o.OfferedGameId)
                .OnDelete(DeleteBehavior.Restrict);

            // A user can send many offers (as requester).
            modelBuilder.Entity<TradeOffer>()
                .HasOne(o => o.RequesterUser)
                .WithMany(u => u.SentOffers)
                .HasForeignKey(o => o.RequesterUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // A user can receive many offers (as game owner).
            modelBuilder.Entity<TradeOffer>()
                .HasOne(o => o.OwnerUser)
                .WithMany(u => u.ReceivedOffers)
                .HasForeignKey(o => o.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
