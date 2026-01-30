namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Name { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
        public string StreetAddress { get; set; } = default!;

        // Navigation: one user owns many games
        public ICollection<Game> Games { get; set; } = new List<Game>();
        public ICollection<TradeOffer> SentOffers { get; set; } = new List<TradeOffer>();
        public ICollection<TradeOffer> ReceivedOffers { get; set; } = new List<TradeOffer>();
    }
}
