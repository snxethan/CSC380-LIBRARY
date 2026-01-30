namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Models
{
    public class Game
    {
        public int Id { get; set; }

        public string Name { get; set; } = default!;
        public string Publisher { get; set; } = default!;
        public int Year { get; set; }
        public string System { get; set; } = default!;
        public string Condition { get; set; } = default!;
        public int? PreviousOwners { get; set; }

        // FK column that points to Users.Id
        public int OwnerId { get; set; }

        // Navigation
        public User Owner { get; set; } = default!;
        public ICollection<TradeOffer> RequestedInOffers { get; set; } = new List<TradeOffer>();
        public ICollection<TradeOffer> OfferedInOffers { get; set; } = new List<TradeOffer>();
    }
}
