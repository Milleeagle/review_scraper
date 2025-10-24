namespace GooglePlacesScraper.Models;

public class Review
{
    public string Id { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorUrl { get; set; } = string.Empty;
    public string ProfilePhotoUrl { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public string RelativeTime { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsLocalGuide { get; set; }
    public int ReviewCount { get; set; }
    public List<string> Photos { get; set; } = new();
    public string BusinessResponse { get; set; } = string.Empty;
    public DateTime? BusinessResponseTime { get; set; }
}