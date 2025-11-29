using GooglePlacesScraper.Models;

namespace GooglePlacesScraper.Interfaces;

public interface IReviewScraper
{
    Task<Company?> SearchCompanyAsync(string companyName, string? location = null);
    Task<List<Review>> ScrapeReviewsAsync(string googleMapsUrl, ScrapingOptions? options = null);
    Task<Company?> ScrapeCompanyWithReviewsAsync(string companyName, string? location = null, ScrapingOptions? options = null);
    Task<Company?> ExtractCompanyInfoAsync(string googleMapsUrl);
    Task<Company?> ScrapeReviewsByPlaceIdAsync(string placeId, ScrapingOptions? options = null);
}