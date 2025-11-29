using GooglePlacesScraper.Interfaces;
using GooglePlacesScraper.Models;
using Microsoft.AspNetCore.Mvc;

namespace GooglePlacesScraper.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewScraper _reviewScraper;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(IReviewScraper reviewScraper, ILogger<ReviewsController> logger)
    {
        _reviewScraper = reviewScraper;
        _logger = logger;
    }

    [HttpGet("search")]
    public async Task<ActionResult<Company>> SearchCompany(
        [FromQuery] string companyName, 
        [FromQuery] string? location = null)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return BadRequest("Company name is required");
        }

        try
        {
            var company = await _reviewScraper.SearchCompanyAsync(companyName, location);
            
            if (company == null)
            {
                return NotFound($"Company '{companyName}' not found");
            }

            return Ok(company);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for company: {CompanyName}", companyName);
            return StatusCode(500, "Internal server error occurred while searching for company");
        }
    }

    [HttpPost("scrape")]
    public async Task<ActionResult<List<Review>>> ScrapeReviews(
        [FromBody] ScrapeReviewsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GoogleMapsUrl))
        {
            return BadRequest("Google Maps URL is required");
        }

        try
        {
            var reviews = await _reviewScraper.ScrapeReviewsAsync(request.GoogleMapsUrl, request.Options);
            return Ok(reviews);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping reviews from URL: {Url}", request.GoogleMapsUrl);
            return StatusCode(500, "Internal server error occurred while scraping reviews");
        }
    }

    [HttpPost("scrape-company")]
    public async Task<ActionResult<Company>> ScrapeCompanyWithReviews(
        [FromBody] ScrapeCompanyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return BadRequest("Company name is required");
        }

        try
        {
            var company = await _reviewScraper.ScrapeCompanyWithReviewsAsync(
                request.CompanyName, 
                request.Location, 
                request.Options);

            if (company == null)
            {
                return NotFound($"Company '{request.CompanyName}' not found");
            }

            return Ok(company);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping company with reviews: {CompanyName}", request.CompanyName);
            return StatusCode(500, "Internal server error occurred while scraping company");
        }
    }

    [HttpPost("scrape-from-url")]
    public async Task<ActionResult<Company>> ScrapeFromUrl(
        [FromBody] ScrapeFromUrlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GoogleMapsUrl))
        {
            return BadRequest("Google Maps URL is required");
        }

        try
        {
            var company = await _reviewScraper.ExtractCompanyInfoAsync(request.GoogleMapsUrl);
            if (company == null)
            {
                return NotFound("Could not extract company information from the provided URL");
            }

            var reviews = await _reviewScraper.ScrapeReviewsAsync(request.GoogleMapsUrl, request.Options);
            company.Reviews = reviews;

            return Ok(company);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping from URL: {Url}", request.GoogleMapsUrl);
            return StatusCode(500, "Internal server error occurred while scraping from URL");
        }
    }

    [HttpGet("test-scrape")]
    public async Task<ActionResult<Company>> TestScrape([FromQuery] string url, [FromQuery] int maxReviews = 10)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest("URL parameter is required");
        }

        try
        {
            var options = new ScrapingOptions 
            { 
                MaxReviews = maxReviews,
                MinRating = 1,
                IncludeBusinessResponses = true 
            };

            var company = await _reviewScraper.ExtractCompanyInfoAsync(url);
            if (company == null)
            {
                return NotFound("Could not extract company information from the provided URL");
            }

            var reviews = await _reviewScraper.ScrapeReviewsAsync(url, options);
            company.Reviews = reviews;

            return Ok(company);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in test scrape: {Url}", url);
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPost("test-scrape-post")]
    public async Task<ActionResult<Company>> TestScrapePost([FromBody] TestScrapeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest("URL is required");
        }

        try
        {
            var options = new ScrapingOptions
            {
                MaxReviews = request.MaxReviews,
                MinRating = 1,
                IncludeBusinessResponses = true
            };

            var company = await _reviewScraper.ExtractCompanyInfoAsync(request.Url);
            if (company == null)
            {
                return NotFound("Could not extract company information from the provided URL");
            }

            var reviews = await _reviewScraper.ScrapeReviewsAsync(request.Url, options);
            company.Reviews = reviews;

            return Ok(company);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in test scrape: {Url}", request.Url);
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPost("scrape-by-place-id")]
    public async Task<ActionResult<Company>> ScrapeByPlaceId([FromBody] ScrapeByPlaceIdRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlaceId))
        {
            return BadRequest("Place ID is required");
        }

        try
        {
            _logger.LogInformation("Scraping reviews for Place ID: {PlaceId}", request.PlaceId);

            var company = await _reviewScraper.ScrapeReviewsByPlaceIdAsync(request.PlaceId, request.Options);

            if (company == null)
            {
                return NotFound($"Could not find or scrape place with ID: {request.PlaceId}");
            }

            return Ok(company);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping by Place ID: {PlaceId}", request.PlaceId);
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPost("scrape-multiple-place-ids")]
    public async Task<ActionResult<List<Company>>> ScrapeMultiplePlaceIds([FromBody] ScrapeMultiplePlaceIdsRequest request)
    {
        if (request.PlaceIds == null || request.PlaceIds.Count == 0)
        {
            return BadRequest("At least one Place ID is required");
        }

        try
        {
            _logger.LogInformation("Scraping {Count} Place IDs in parallel", request.PlaceIds.Count);

            var tasks = request.PlaceIds.Select(async placeId =>
            {
                try
                {
                    using var scope = HttpContext.RequestServices.CreateScope();
                    var scraper = scope.ServiceProvider.GetRequiredService<IReviewScraper>();

                    var googleMapsUrl = $"https://www.google.com/maps/place/?q=place_id:{placeId}";
                    var company = await scraper.ExtractCompanyInfoAsync(googleMapsUrl);

                    if (company != null)
                    {
                        var reviews = await scraper.ScrapeReviewsAsync(googleMapsUrl, request.Options);
                        company.Reviews = reviews;
                    }

                    return company;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scraping Place ID: {PlaceId}", placeId);
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);
            var companies = results.Where(c => c != null).ToList()!;

            _logger.LogInformation("Successfully scraped {Count} out of {Total} places", companies.Count, request.PlaceIds.Count);

            return Ok(companies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping multiple Place IDs");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}

public class ScrapeReviewsRequest
{
    public string GoogleMapsUrl { get; set; } = string.Empty;
    public ScrapingOptions? Options { get; set; }
}

public class ScrapeCompanyRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string? Location { get; set; }
    public ScrapingOptions? Options { get; set; }
}

public class ScrapeFromUrlRequest
{
    public string GoogleMapsUrl { get; set; } = string.Empty;
    public ScrapingOptions? Options { get; set; }
}

public class TestScrapeRequest
{
    public string Url { get; set; } = string.Empty;
    public int MaxReviews { get; set; } = 10;
}

public class ScrapeByPlaceIdRequest
{
    public string PlaceId { get; set; } = string.Empty;
    public ScrapingOptions? Options { get; set; }
}

public class ScrapeMultiplePlaceIdsRequest
{
    public List<string> PlaceIds { get; set; } = new();
    public ScrapingOptions? Options { get; set; }
}