namespace GooglePlacesScraper.Models;

public class Company
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string GoogleMapsUrl { get; set; } = string.Empty;
    public string PlaceId { get; set; } = string.Empty;
    public double? OverallRating { get; set; }
    public int? TotalReviews { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<string> OpeningHours { get; set; } = new();
    public string PriceLevel { get; set; } = string.Empty;
    public List<Review> Reviews { get; set; } = new();
}