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
    }
}
