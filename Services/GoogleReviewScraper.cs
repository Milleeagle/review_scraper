using GooglePlacesScraper.Interfaces;
using GooglePlacesScraper.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Text.RegularExpressions;

namespace GooglePlacesScraper.Services;

public class GoogleReviewScraper : IReviewScraper, IDisposable
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly ILogger<GoogleReviewScraper> _logger;
    private bool _disposed = false;

    public GoogleReviewScraper(ILogger<GoogleReviewScraper> logger)
    {
        _logger = logger;

        var options = new ChromeOptions();
        options.AddArguments(
            "--headless",
            "--no-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--window-size=1920,1080",
            "--disable-blink-features=AutomationControlled",
            "--lang=en-US",
            // Safe performance optimizations
            "--disable-extensions",
            "--disable-plugins"
        );

        _driver = new ChromeDriver(options);
        _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(15);
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
    }

    public async Task<List<Review>> ScrapeReviewsAsync(string googleMapsUrl, ScrapingOptions? options = null)
    {
        options ??= new ScrapingOptions();
        var reviews = new List<Review>();

        try
        {
            _logger.LogInformation("Navigating to: {Url}", googleMapsUrl);
            _driver.Navigate().GoToUrl(googleMapsUrl);

            // Wait for page body to be present
            try
            {
                _wait.Until(d => d.FindElement(By.TagName("body")));
            }
            catch { }
            await Task.Delay(1500); // Balanced: fast but reliable

            // Handle consent if it appears
            try
            {
                // Try multiple consent button selectors
                var consentSelectors = new[]
                {
                    "//button[contains(., 'Accept')]",
                    "//button[contains(., 'Reject')]",
                    "//button[contains(., 'Godkänn')]", // Swedish Accept
                    "//button[contains(., 'Avvisa')]", // Swedish Reject
                    "//button[@aria-label='Accept all']",
                    "//button[@aria-label='Reject all']",
                    "//form[@action]//button[2]", // Usually the second button in consent forms
                    "//button[contains(@class, 'VfPpkd-LgbsSe')]" // Material button class
                };

                foreach (var selector in consentSelectors)
                {
                    try
                    {
                        var button = _driver.FindElement(By.XPath(selector));
                        if (button != null && button.Displayed)
                        {
                            button.Click();
                            await Task.Delay(1000); // Balanced wait
                            _logger.LogInformation("Clicked consent button with selector: {Selector}", selector);
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not handle consent dialog: {Message}", ex.Message);
            }

            // Click Reviews tab
            await ClickReviewsTab();
            await Task.Delay(1200); // Balanced wait

            // Scroll to load reviews
            await ScrollReviews(options.MaxReviews);

            // Extract reviews
            reviews = ExtractAllReviews(options.MaxReviews);

            _logger.LogInformation("Extracted {Count} reviews", reviews.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping reviews");
        }

        return reviews;
    }

    private async Task ClickReviewsTab()
    {
        try
        {
            // Try to find and click Reviews tab
            var selectors = new[]
            {
                "button[aria-label*='Review']",
                "button[role='tab'][aria-label*='Review']",
                "button[data-tab-index='1']"
            };

            foreach (var selector in selectors)
            {
                try
                {
                    var tab = _driver.FindElement(By.CssSelector(selector));
                    if (tab.Displayed)
                    {
                        tab.Click();
                        _logger.LogInformation("Clicked Reviews tab");
                        return;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not click Reviews tab: {Message}", ex.Message);
        }
    }

    private async Task ScrollReviews(int targetCount)
    {
        try
        {
            // Find scrollable container
            var scrollableDiv = _driver.FindElement(By.CssSelector("div[role='main']"));

            // Optimize scrolling - fewer scrolls with minimal delay
            int scrollCount = Math.Min((targetCount / 5) + 1, 4); // Cap at 4 scrolls for speed
            for (int i = 0; i < scrollCount; i++)
            {
                ((IJavaScriptExecutor)_driver).ExecuteScript(
                    "arguments[0].scrollBy(0, 1000);", scrollableDiv);
                await Task.Delay(200); // Reduced from 300ms
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error scrolling: {Message}", ex.Message);
        }
    }

    private List<Review> ExtractAllReviews(int maxReviews)
    {
        var reviews = new List<Review>();
        var uniqueReviews = new HashSet<string>();

        try
        {
            // Try multiple selectors to find review elements
            var selectors = new[]
            {
                "div[data-review-id]",
                ".jftiEf",
                "div.jftiEf",
                "[jsaction*='review']",
                "div[data-review-id], .jftiEf"
            };

            IReadOnlyCollection<IWebElement> reviewElements = new List<IWebElement>();

            foreach (var selector in selectors)
            {
                try
                {
                    reviewElements = _driver.FindElements(By.CssSelector(selector));
                    if (reviewElements.Count > 0)
                    {
                        _logger.LogInformation("Found {Count} review elements with selector: {Selector}", reviewElements.Count, selector);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Selector {Selector} failed: {Message}", selector, ex.Message);
                }
            }

            if (reviewElements.Count == 0)
            {
                _logger.LogWarning("No review elements found with any selector");

                // Save screenshot for debugging
                try
                {
                    var screenshot = ((ITakesScreenshot)_driver).GetScreenshot();
                    var screenshotPath = Path.Combine("/tmp", $"debug_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    screenshot.SaveAsFile(screenshotPath);
                    _logger.LogWarning("Screenshot saved to: {Path}", screenshotPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not save screenshot: {Message}", ex.Message);
                }

                // Log page source for debugging
                var pageSource = _driver.PageSource;
                _logger.LogDebug("Page contains 'review': {Contains}", pageSource.Contains("review", StringComparison.OrdinalIgnoreCase));
                _logger.LogDebug("Page contains 'jftiEf': {Contains}", pageSource.Contains("jftiEf"));
                _logger.LogDebug("Page title: {Title}", _driver.Title);
                _logger.LogDebug("Current URL: {Url}", _driver.Url);

                return reviews;
            }

            foreach (var element in reviewElements.Take(maxReviews * 2))
            {
                try
                {
                    var review = ExtractReview(element);
                    if (review != null)
                    {
                        // Use combination of author + text as unique key
                        var uniqueKey = $"{review.AuthorName}_{review.Text}";
                        if (uniqueReviews.Add(uniqueKey) && !string.IsNullOrEmpty(review.AuthorName))
                        {
                            reviews.Add(review);
                            _logger.LogInformation("Extracted review from: {Author}", review.AuthorName);
                            if (reviews.Count >= maxReviews)
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error extracting review: {Message}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding review elements");
        }

        return reviews;
    }

    private Review? ExtractReview(IWebElement element)
    {
        try
        {
            var review = new Review
            {
                Id = element.GetAttribute("data-review-id") ?? Guid.NewGuid().ToString()
            };

            // Extract author name from .d4r55
            try
            {
                var authorElement = element.FindElement(By.CssSelector(".d4r55"));
                review.AuthorName = authorElement.Text.Trim();
            }
            catch { }

            // Extract rating from aria-label
            try
            {
                var ratingElement = element.FindElement(By.CssSelector(".kvMYJc"));
                var ariaLabel = ratingElement.GetAttribute("aria-label");

                // Try multiple patterns for different languages
                var patterns = new[]
                {
                    @"(\d+)\s*star", // English: "5 stars"
                    @"(\d+)\s*stjärn", // Swedish: "5 stjärnor"
                    @"(\d+)\s*étoile", // French: "5 étoiles"
                    @"(\d+)\s*Stern" // German: "5 Sterne"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(ariaLabel, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        review.Rating = int.Parse(match.Groups[1].Value);
                        break;
                    }
                }
            }
            catch { }

            // Extract review text from .wiI7pd
            try
            {
                var textElement = element.FindElement(By.CssSelector(".wiI7pd"));
                review.Text = textElement.Text.Trim();
            }
            catch { }

            // Extract relative time from .rsqaWe
            try
            {
                var timeElement = element.FindElement(By.CssSelector(".rsqaWe"));
                review.RelativeTime = timeElement.Text.Trim();
            }
            catch { }

            // Extract business response if exists
            try
            {
                var responseElement = element.FindElement(By.CssSelector(".CDe7pd .wiI7pd"));
                review.BusinessResponse = responseElement.Text.Trim();
            }
            catch { }

            review.Time = DateTime.Now;
            review.Language = "en";

            return review;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to extract review: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<Company?> SearchCompanyAsync(string companyName, string? location = null)
    {
        try
        {
            var searchQuery = string.IsNullOrEmpty(location)
                ? companyName
                : $"{companyName} {location}";

            var searchUrl = $"https://www.google.com/maps/search/{Uri.EscapeDataString(searchQuery)}";
            _driver.Navigate().GoToUrl(searchUrl);
            await Task.Delay(3000);

            var company = new Company
            {
                Name = companyName,
                GoogleMapsUrl = _driver.Url
            };

            return company;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for company");
            return null;
        }
    }

    public async Task<Company?> ScrapeCompanyWithReviewsAsync(string companyName, string? location = null, ScrapingOptions? options = null)
    {
        var company = await SearchCompanyAsync(companyName, location);
        if (company == null) return null;

        var reviews = await ScrapeReviewsAsync(company.GoogleMapsUrl, options);
        company.Reviews = reviews;

        return company;
    }

    public async Task<Company?> ExtractCompanyInfoAsync(string googleMapsUrl)
    {
        try
        {
            _driver.Navigate().GoToUrl(googleMapsUrl);
            await Task.Delay(2000);

            var company = new Company
            {
                GoogleMapsUrl = googleMapsUrl
            };

            // Extract company name
            try
            {
                var nameElement = _driver.FindElement(By.CssSelector("h1.DUwDvf, h1.fontHeadlineLarge"));
                company.Name = nameElement.Text.Trim();
            }
            catch { }

            // Extract address
            try
            {
                var addressElement = _driver.FindElement(By.CssSelector("button[data-item-id*='address'], .rogA2c"));
                company.Address = addressElement.Text.Trim();
            }
            catch { }

            // Extract phone
            try
            {
                var phoneElement = _driver.FindElement(By.CssSelector("button[data-item-id*='phone']"));
                company.PhoneNumber = phoneElement.Text.Trim();
            }
            catch { }

            // Extract website
            try
            {
                var websiteElement = _driver.FindElement(By.CssSelector("a[data-item-id*='authority']"));
                company.Website = websiteElement.GetAttribute("href");
            }
            catch { }

            // Extract rating
            try
            {
                var ratingElement = _driver.FindElement(By.CssSelector(".F7nice span[aria-hidden='true']"));
                if (double.TryParse(ratingElement.Text, out var rating))
                {
                    company.OverallRating = rating;
                }
            }
            catch { }

            return company;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting company info");
            return null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _driver?.Quit();
            _driver?.Dispose();
            _disposed = true;
        }
    }
}
