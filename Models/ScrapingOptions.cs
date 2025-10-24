namespace GooglePlacesScraper.Models;

public class ScrapingOptions
{
    public int MaxReviews { get; set; } = 100;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? MinRating { get; set; }
    public int? MaxRating { get; set; }
    public string? Language { get; set; }
    public bool IncludePhotos { get; set; } = false;
    public bool IncludeBusinessResponses { get; set; } = true;
    public SortOrder SortBy { get; set; } = SortOrder.MostRelevant;
    public int DelayBetweenRequests { get; set; } = 2000; // milliseconds
    public int TimeoutSeconds { get; set; } = 30;
}

public enum SortOrder
{
    MostRelevant,
    Newest,
    Oldest,
    HighestRated,
    LowestRated
}