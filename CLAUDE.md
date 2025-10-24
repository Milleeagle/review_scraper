# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 8 ASP.NET Core web application that provides a robust Google reviews scraper. It can search for companies without Place IDs and scrape comprehensive review data from Google Maps, including reviews that the Google Places API may not return.

## Core Features

- **Company Search**: Find companies by name and location without requiring a Place ID
- **Comprehensive Review Scraping**: Extract more reviews than the Google Places API limit of 5
- **Advanced Filtering**: Filter reviews by rating, date range, language, and more
- **Clean API**: RESTful endpoints for easy integration with other projects
- **Selenium-based**: Uses Chrome WebDriver for reliable web scraping

## Development Commands

**Build the project:**
```bash
dotnet build
```

**Restore dependencies (required first time):**
```bash
dotnet restore
```

**Run the application:**
```bash
dotnet run
```

**Run with specific profile:**
```bash
dotnet run --launch-profile http    # Runs on http://localhost:5291
dotnet run --launch-profile https   # Runs on https://localhost:7296
```

**Clean build artifacts:**
```bash
dotnet clean
```

## API Endpoints

Once running, navigate to `/swagger` for full API documentation. Key endpoints:

- `GET /api/reviews/search?companyName={name}&location={location}` - Search for company info
- `POST /api/reviews/scrape-company` - Scrape company with all reviews
- `POST /api/reviews/scrape` - Scrape reviews from a specific Google Maps URL
- `POST /api/reviews/scrape-from-url` - Extract company info and scrape reviews from Google Maps URL
- `GET /api/reviews/test-scrape?url={url}&maxReviews={count}` - Quick test endpoint
- `POST /api/reviews/test-scrape-post` - Test scraping with POST body

## Architecture

### Core Components

- **Models**: `Review`, `Company`, `ScrapingOptions` - Data models
- **Interfaces**: `IReviewScraper` - Clean abstraction for scraping operations
- **Services**: `GoogleReviewScraper` - Main scraper implementation using Selenium
- **Controllers**: `ReviewsController` - REST API endpoints

### Key Dependencies

- **Selenium WebDriver**: Chrome automation for scraping (version 4.16.2)
- **Selenium.WebDriver.ChromeDriver**: Chrome driver for automation (version 120.0.6099.7100)
- **HtmlAgilityPack**: HTML parsing support (version 1.11.54)
- **Swashbuckle**: API documentation (Swagger) (version 6.4.0)
- **Newtonsoft.Json**: JSON serialization (version 13.0.3)
- **Microsoft.Extensions.Http**: HTTP client services (version 8.0.0)

### Scraping Strategy

1. **Direct Maps Access**: Assumes Google Maps URL is provided directly (no searching)
2. **Reviews Tab Navigation**: Finds and clicks the "Reviews" tab on the Google Maps page
3. **Sort Selection**: Clicks the "Sort" dropdown and selects "Newest" to get most recent reviews first
4. **Review Loading**: Scrolls through reviews to load more than the default visible set
5. **Data Extraction**: Parses review elements using CSS selectors and XPath
6. **Filtering**: Applies user-defined filters (rating, date, etc.)

## Usage Examples

### Programmatic Usage
```csharp
var scraper = new GoogleReviewScraper(logger);
var options = new ScrapingOptions { MaxReviews = 50, MinRating = 1 };
var company = await scraper.ScrapeCompanyWithReviewsAsync("Company Name", "Location", options);
```

### API Usage
```bash
curl -X POST "https://localhost:7296/api/reviews/scrape-company" \
  -H "Content-Type: application/json" \
  -d '{"companyName": "Aerius Ventilation", "location": "Stockholm", "options": {"maxReviews": 20}}'
```

## Important Notes

- **Chrome Driver**: Requires Chrome browser or ChromeDriver to be installed
- **Google Maps URL Required**: The system expects direct Google Maps URLs - no company search functionality
- **Reviews Sorted by Newest**: Automatically selects "Newest" from the sort dropdown to get most recent reviews first
- **Rate Limiting**: Built-in delays between requests to avoid being blocked
- **Headless Mode**: Runs Chrome in headless mode by default for server environments
- **Error Handling**: Comprehensive logging and graceful error handling for unstable web scraping
- **Scalability**: Consider implementing request queuing for high-volume usage

## Testing

Use `TestConsole.cs` for manual testing:
- Modify the test company name and location in the `RunTest()` method as needed
- Run the test with `TestConsole.RunTest()` to verify scraping functionality
- Default test case uses "Aerius Ventilation Stockholm" with 20 reviews max

## Configuration

The application uses standard ASP.NET Core configuration:
- `appsettings.json` and `appsettings.Development.json` for environment-specific settings
- Launch profiles defined in `Properties/launchSettings.json`:
  - `http`: Runs on http://localhost:5291
  - `https`: Runs on https://localhost:7296;http://localhost:5291
  - `IIS Express`: Uses ports 11282 (http) and 44331 (https)

## Request/Response Models

Key request models used by the API:
- `ScrapingOptions`: Configures scraping behavior (MaxReviews, MinRating, MaxRating, date filters, etc.)
- `ScrapeReviewsRequest`: Contains GoogleMapsUrl and ScrapingOptions
- `ScrapeCompanyRequest`: Contains CompanyName, Location, and ScrapingOptions  
- `ScrapeFromUrlRequest`: Contains GoogleMapsUrl and ScrapingOptions
- `TestScrapeRequest`: Simple test request with Url and MaxReviews

## Selenium Configuration

The `GoogleReviewScraper` uses extensive Chrome options for reliable scraping:
- Runs headless by default for server environments
- Includes anti-detection measures (`--disable-blink-features=AutomationControlled`)
- Handles Google consent pages automatically
- Uses multiple fallback strategies for element selection
- Implements smart scrolling to load more reviews than initially visible