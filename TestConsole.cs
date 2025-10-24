using GooglePlacesScraper.Models;
using GooglePlacesScraper.Services;
using Microsoft.Extensions.Logging;

namespace GooglePlacesScraper;

public class TestConsole
{
    public static async Task RunTest()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<GoogleReviewScraper>();

        using var scraper = new GoogleReviewScraper(logger);

        Console.WriteLine("Testing Google Review Scraper with Aerius Ventilation Stockholm...");

        var options = new ScrapingOptions
        {
            MaxReviews = 20,
            MinRating = 1,
            MaxRating = 5,
            IncludeBusinessResponses = true
        };

        try
        {
            var company = await scraper.ScrapeCompanyWithReviewsAsync(
                "Aerius Ventilation Stockholm", 
                "Stockholm", 
                options);

            if (company != null)
            {
                Console.WriteLine($"\n=== COMPANY INFO ===");
                Console.WriteLine($"Name: {company.Name}");
                Console.WriteLine($"Address: {company.Address}");
                Console.WriteLine($"Phone: {company.PhoneNumber}");
                Console.WriteLine($"Website: {company.Website}");
                Console.WriteLine($"Overall Rating: {company.OverallRating}");
                Console.WriteLine($"Total Reviews: {company.TotalReviews}");
                Console.WriteLine($"Google Maps URL: {company.GoogleMapsUrl}");

                Console.WriteLine($"\n=== REVIEWS ({company.Reviews.Count} found) ===");
                
                foreach (var review in company.Reviews.OrderByDescending(r => r.Time))
                {
                    Console.WriteLine($"\n--- Review by {review.AuthorName} ---");
                    Console.WriteLine($"Rating: {review.Rating} stars");
                    Console.WriteLine($"Date: {review.Time:yyyy-MM-dd} ({review.RelativeTime})");
                    Console.WriteLine($"Text: {review.Text}");
                    
                    if (!string.IsNullOrEmpty(review.BusinessResponse))
                    {
                        Console.WriteLine($"Business Response: {review.BusinessResponse}");
                    }
                }
            }
            else
            {
                Console.WriteLine("Company not found!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}