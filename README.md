# Google Places Review Scraper API

A high-performance, production-ready ASP.NET Core Web API for scraping Google Maps reviews. Built with Selenium WebDriver for reliable web scraping without requiring Google Places API keys.

## ✨ Features

- 🚀 **High Performance**: Scrapes reviews in ~9 seconds per location
- ⚡ **Parallel Scraping**: Scrape multiple locations simultaneously (85% faster for bulk operations)
- 🌍 **Multi-language Support**: Works with Swedish, English, French, German, and more
- 📊 **Complete Data**: Extracts ratings, reviews, authors, timestamps, and business responses
- 🔄 **No API Limits**: Bypass Google Places API's 5-review limitation
- 🐳 **Docker Ready**: Easy deployment with Docker support
- 📝 **Swagger Documentation**: Interactive API documentation included

## 🎯 Performance

| Operation | Time | Speedup |
|-----------|------|---------|
| Single location | ~9s | - |
| 3 locations (parallel) | ~14s | 52% faster |
| 11 locations (parallel) | ~15s | **85% faster** |

## 🛠️ Prerequisites

- .NET 8.0 SDK
- Chrome/Chromium browser (for local development)
- Docker (optional, for containerized deployment)

## 🚀 Quick Start

### Local Development

1. **Clone the repository**
```bash
git clone https://github.com/yourusername/google-places-scraper.git
cd google-places-scraper
```

2. **Restore dependencies**
```bash
dotnet restore
```

3. **Run the application**
```bash
dotnet run --launch-profile http
```

The API will be available at `http://localhost:5291`

4. **Access Swagger UI**

Navigate to `http://localhost:5291/swagger` for interactive API documentation

### Docker Deployment

1. **Build the Docker image**
```bash
docker build -t google-places-scraper .
```

2. **Run the container**
```bash
docker run -p 5291:8080 google-places-scraper
```

Or use Docker Compose:
```bash
docker-compose up
```

## 📚 API Endpoints

### 1. Scrape Single Place by Place ID

`POST /api/reviews/scrape-by-place-id`

```json
{
  "placeId": "ChIJnytgooPRV0YRTCdMAyVMLt4",
  "options": {
    "maxReviews": 10
  }
}
```

**Response:**
```json
{
  "name": "Company Name",
  "googleMapsUrl": "https://www.google.com/maps/place/?q=place_id:...",
  "overallRating": 4.5,
  "reviews": [
    {
      "id": "...",
      "authorName": "John Doe",
      "rating": 5,
      "text": "Great service!",
      "relativeTime": "2 months ago",
      "businessResponse": "Thank you for your feedback!"
    }
  ]
}
```

### 2. Scrape Multiple Places in Parallel

`POST /api/reviews/scrape-multiple-place-ids`

```json
{
  "placeIds": [
    "ChIJnytgooPRV0YRTCdMAyVMLt4",
    "ChIJp4mZMOedX0YRictjTxs8Czg",
    "ChIJoyABwO93X0YRc8HCOeQQSiE"
  ],
  "options": {
    "maxReviews": 5
  }
}
```

**Response:** Array of company objects (same structure as single place endpoint)

### 3. Search Company and Scrape Reviews

`POST /api/reviews/scrape-company`

```json
{
  "companyName": "Searchminds",
  "location": "Stockholm",
  "options": {
    "maxReviews": 10
  }
}
```

### 4. Scrape from Google Maps URL

`POST /api/reviews/scrape-from-url`

```json
{
  "googleMapsUrl": "https://www.google.com/maps/place/...",
  "options": {
    "maxReviews": 10
  }
}
```

## 🔧 Configuration

### Scraping Options

```json
{
  "maxReviews": 10,           // Maximum number of reviews to scrape
  "minRating": 1,             // Minimum rating filter (1-5)
  "maxRating": 5,             // Maximum rating filter (1-5)
  "includeBusinessResponses": true
}
```

### Environment Variables

Create an `appsettings.Production.json` file:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

## 📖 Usage Examples

### cURL

```bash
# Single place
curl -X POST "http://localhost:5291/api/reviews/scrape-by-place-id" \
  -H "Content-Type: application/json" \
  -d '{"placeId": "ChIJnytgooPRV0YRTCdMAyVMLt4", "options": {"maxReviews": 5}}'

# Multiple places
curl -X POST "http://localhost:5291/api/reviews/scrape-multiple-place-ids" \
  -H "Content-Type: application/json" \
  -d '{"placeIds": ["ChIJ...", "ChIJ..."], "options": {"maxReviews": 5}}'
```

### Python

```python
import requests

url = "http://localhost:5291/api/reviews/scrape-by-place-id"
payload = {
    "placeId": "ChIJnytgooPRV0YRTCdMAyVMLt4",
    "options": {"maxReviews": 10}
}

response = requests.post(url, json=payload)
data = response.json()

print(f"Company: {data['name']}")
print(f"Reviews: {len(data['reviews'])}")
for review in data['reviews']:
    print(f"{review['authorName']}: {review['rating']}⭐ - {review['text'][:50]}...")
```

### JavaScript/Node.js

```javascript
const axios = require('axios');

async function scrapeReviews(placeId) {
  const response = await axios.post('http://localhost:5291/api/reviews/scrape-by-place-id', {
    placeId: placeId,
    options: { maxReviews: 10 }
  });

  return response.data;
}

scrapeReviews('ChIJnytgooPRV0YRTCdMAyVMLt4')
  .then(data => console.log(data))
  .catch(err => console.error(err));
```

### C#

```csharp
using System.Net.Http.Json;

var client = new HttpClient { BaseAddress = new Uri("http://localhost:5291") };

var request = new
{
    PlaceId = "ChIJnytgooPRV0YRTCdMAyVMLt4",
    Options = new { MaxReviews = 10 }
};

var response = await client.PostAsJsonAsync("/api/reviews/scrape-by-place-id", request);
var company = await response.Content.ReadFromJsonAsync<Company>();

Console.WriteLine($"Company: {company.Name}");
Console.WriteLine($"Reviews: {company.Reviews.Count}");
```

## 🏗️ Architecture

### Technology Stack

- **Framework**: ASP.NET Core 8.0
- **Web Scraping**: Selenium WebDriver 4.16.2
- **Browser**: Chrome/Chromium (headless)
- **API Documentation**: Swashbuckle (Swagger)

### Key Components

- **GoogleReviewScraperV2**: High-performance scraper (~350 lines, 85% smaller than original)
- **ReviewsController**: RESTful API endpoints
- **Models**: `Company`, `Review`, `ScrapingOptions`
- **Interfaces**: `IReviewScraper` for clean abstraction

### Design Decisions

1. **Scoped Service Lifetime**: Each request gets its own Chrome driver instance for parallel scraping
2. **Smart Waiting**: Uses WebDriverWait for page load detection instead of fixed delays
3. **Multi-language Consent**: Handles Google's consent dialogs in multiple languages
4. **Error Isolation**: Failed scrapes don't affect others in parallel operations
5. **Deduplication**: HashSet-based deduplication prevents duplicate reviews

## 🔒 Production Considerations

### Rate Limiting

Consider implementing rate limiting to avoid being blocked by Google:

```csharp
// Add to Program.cs
builder.Services.AddRateLimiter(options => {
    options.AddFixedWindowLimiter("api", options => {
        options.Window = TimeSpan.FromMinutes(1);
        options.PermitLimit = 10;
    });
});
```

### Monitoring

Add Application Insights or similar monitoring:

```bash
dotnet add package Microsoft.ApplicationInsights.AspNetCore
```

### Security

- Use HTTPS in production
- Implement API authentication (JWT, API Keys)
- Configure CORS appropriately
- Use secrets management for sensitive data

## 🐛 Troubleshooting

### Chrome Driver Issues

**Problem**: ChromeDriver not found
```bash
# Install ChromeDriver manually
dotnet tool install --global Selenium.WebDriver.ChromeDriver
```

**Problem**: Chrome version mismatch
- Update the `Selenium.WebDriver.ChromeDriver` NuGet package

### Memory Issues

For high-volume scraping, consider:
- Implementing request queuing
- Adding connection pooling
- Limiting parallel operations
- Properly disposing Chrome drivers

### No Reviews Extracted

- Verify the Google Maps URL or Place ID is correct
- Check if the location actually has reviews
- Review application logs for errors
- Increase wait times if page loads slowly

## 📊 Performance Optimization Tips

1. **Parallel Scraping**: Use `/scrape-multiple-place-ids` for bulk operations
2. **Adjust MaxReviews**: Lower values = faster scraping
3. **Resource Management**: Ensure proper disposal of scrapers
4. **Caching**: Consider caching results for frequently queried places
5. **Load Balancing**: Use multiple instances for very high traffic

## 📄 License

MIT License - see [LICENSE](LICENSE) file for details

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## ⚠️ Disclaimer

This tool is for educational and research purposes. Be respectful of Google's Terms of Service and implement appropriate rate limiting. The authors are not responsible for misuse of this tool.

## 📞 Support

For issues, questions, or contributions, please open an issue on GitHub.

## 🎯 Roadmap

- [ ] Add request queuing system
- [ ] Implement caching layer
- [ ] Add health check endpoints
- [ ] Support for more review platforms
- [ ] GraphQL API support
- [ ] Real-time progress updates via WebSockets

---

**Built with ❤️ using .NET 8.0 and Selenium WebDriver**
