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
            "--headless=new",
            "--no-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--window-size=1920,1080",
            "--disable-blink-features=AutomationControlled",
            "--lang=en-US",
            // Enhanced stealth
            "--disable-extensions",
            "--disable-plugins",
            "--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "--disable-infobars",
            "--disable-notifications",
            "--disable-popup-blocking"
        );

        // Add experimental options for better stealth
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);

        _driver = new ChromeDriver(options);
        _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(20);
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(15));

        // Execute script to remove webdriver flag
        ((IJavaScriptExecutor)_driver).ExecuteScript("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})");
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
            await Task.Delay(1500);

            // Handle consent if it appears
            await HandleConsent();

            // Wait for place details to load
            await Task.Delay(2000);

            // Click Reviews tab
            await ClickReviewsTab();
            await Task.Delay(1500);

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

    private async Task HandleConsent()
    {
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
                        await Task.Delay(1000);
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
    }

    private async Task ClickReviewsTab()
    {
        try
        {
            // Try to find and click Reviews tab using multiple strategies
            var selectors = new[]
            {
                "button[aria-label*='eview']", // Works for "Reviews" in English
                "button[aria-label*='ecension']", // Swedish "Recensioner"
                "button[role='tab'][aria-label*='eview']",
                "button[data-tab-index='1']",
                "//button[contains(@aria-label, 'review')]",
                "//button[contains(@aria-label, 'Review')]",
                "button.hh2c6"
            };

            bool foundTab = false;
            foreach (var selector in selectors)
            {
                try
                {
                    IWebElement? tab = null;

                    if (selector.StartsWith("//"))
                    {
                        tab = _driver.FindElement(By.XPath(selector));
                    }
                    else
                    {
                        tab = _driver.FindElement(By.CssSelector(selector));
                    }

                    if (tab != null && tab.Displayed)
                    {
                        // Scroll element into view first
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView(true);", tab);
                        await Task.Delay(500);

                        tab.Click();
                        _logger.LogInformation("Clicked Reviews tab with selector: {Selector}", selector);
                        foundTab = true;
                        await Task.Delay(2000);

                        // Sorting can cause issues, skip for now
                        // await SortReviewsByNewest();
                        return;
                    }
                }
                catch { }
            }

            if (!foundTab)
            {
                _logger.LogWarning("Could not find Reviews tab with any selector");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not click Reviews tab: {Message}", ex.Message);
        }
    }

    private async Task SortReviewsByNewest()
    {
        try
        {
            // Click the sort button
            var sortSelectors = new[]
            {
                "button[aria-label*='Sort']",
                "button[aria-label*='Sortera']", // Swedish
                "button.g88MCb"
            };

            IWebElement? sortButton = null;
            foreach (var selector in sortSelectors)
            {
                try
                {
                    sortButton = _driver.FindElement(By.CssSelector(selector));
                    if (sortButton != null && sortButton.Displayed)
                    {
                        sortButton.Click();
                        _logger.LogInformation("Clicked sort button");
                        await Task.Delay(1000);
                        break;
                    }
                }
                catch { }
            }

            if (sortButton != null)
            {
                // Click "Newest" option
                var newestSelectors = new[]
                {
                    "//div[@role='menuitem']//div[contains(text(), 'Newest')]",
                    "//div[@role='menuitem']//div[contains(text(), 'Nyaste')]", // Swedish
                    "//div[@role='menuitemradio' and contains(., 'Newest')]",
                    "//div[@data-index='1']" // Usually the second option
                };

                foreach (var selector in newestSelectors)
                {
                    try
                    {
                        var newestOption = _driver.FindElement(By.XPath(selector));
                        if (newestOption != null && newestOption.Displayed)
                        {
                            newestOption.Click();
                            _logger.LogInformation("Selected 'Newest' sort option");
                            await Task.Delay(2000); // Wait for re-sort
                            break;
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not sort reviews: {Message}", ex.Message);
        }
    }

    private async Task ScrollReviews(int targetCount)
    {
        try
        {
            // Try to find scrollable container with multiple selectors
            IWebElement? scrollableDiv = null;
            var containerSelectors = new[]
            {
                "div[role='main']",
                "div.m6QErb",
                "div.m6QErb.DxyBCb"
            };

            foreach (var selector in containerSelectors)
            {
                try
                {
                    scrollableDiv = _driver.FindElement(By.CssSelector(selector));
                    if (scrollableDiv != null)
                    {
                        _logger.LogInformation("Found scrollable container with selector: {Selector}", selector);
                        break;
                    }
                }
                catch { }
            }

            if (scrollableDiv == null)
            {
                _logger.LogWarning("Could not find scrollable container");
                return;
            }

            // Scroll to load more reviews - increased from original but not too aggressive
            int scrollCount = Math.Min((targetCount / 3) + 3, 15);
            for (int i = 0; i < scrollCount; i++)
            {
                ((IJavaScriptExecutor)_driver).ExecuteScript(
                    "arguments[0].scrollBy(0, 1000);", scrollableDiv);
                await Task.Delay(400);
            }

            _logger.LogInformation("Completed {Count} scroll iterations", scrollCount);
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
                "div.jftiEf"
            };

            IReadOnlyCollection<IWebElement> reviewElements = new List<IWebElement>();
            string? usedSelector = null;

            foreach (var selector in selectors)
            {
                try
                {
                    reviewElements = _driver.FindElements(By.CssSelector(selector));
                    if (reviewElements.Count > 0)
                    {
                        usedSelector = selector;
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

                // Save screenshot and page source for debugging
                try
                {
                    var screenshot = ((ITakesScreenshot)_driver).GetScreenshot();
                    var screenshotPath = Path.Combine("/tmp", $"debug_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    screenshot.SaveAsFile(screenshotPath);
                    _logger.LogWarning("Screenshot saved to: {Path}", screenshotPath);

                    // Save page HTML for analysis
                    var htmlPath = Path.Combine("/tmp", $"debug_page_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                    File.WriteAllText(htmlPath, _driver.PageSource);
                    _logger.LogWarning("Page HTML saved to: {Path}", htmlPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not save debug files: {Message}", ex.Message);
                }

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
                        var uniqueKey = $"{review.AuthorName}_{review.Text}_{review.Rating}";
                        if (uniqueReviews.Add(uniqueKey) && !string.IsNullOrEmpty(review.AuthorName))
                        {
                            reviews.Add(review);
                            _logger.LogDebug("Extracted review from: {Author} ({Rating} stars)", review.AuthorName, review.Rating);
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

            _logger.LogInformation("Successfully extracted {Count} unique reviews", reviews.Count);
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

            // Extract author name with multiple selectors
            var authorSelectors = new[] { ".d4r55", "button[aria-label]", ".WNxzHc" };
            foreach (var selector in authorSelectors)
            {
                try
                {
                    var authorElement = element.FindElement(By.CssSelector(selector));
                    var name = authorElement.Text.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        review.AuthorName = name;
                        break;
                    }
                }
                catch { }
            }

            // Extract rating from aria-label with multiple selectors
            var ratingSelectors = new[] { ".kvMYJc", "span[role='img']", ".DU9Pgb", "span[aria-label*='star']" };
            foreach (var selector in ratingSelectors)
            {
                try
                {
                    var ratingElement = element.FindElement(By.CssSelector(selector));
                    var ariaLabel = ratingElement.GetAttribute("aria-label");

                    if (!string.IsNullOrEmpty(ariaLabel))
                    {
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

                        if (review.Rating > 0) break;
                    }
                }
                catch { }
            }

            // Extract review text with multiple selectors
            var textSelectors = new[] { ".wiI7pd", ".MyEned", "span[data-expandable-section]", ".review-full-text" };
            foreach (var selector in textSelectors)
            {
                try
                {
                    var textElement = element.FindElement(By.CssSelector(selector));
                    var text = textElement.Text.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        review.Text = text;
                        break;
                    }
                }
                catch { }
            }

            // Extract relative time with multiple selectors
            var timeSelectors = new[] { ".rsqaWe", ".DU9Pgb", "span.dehysf" };
            foreach (var selector in timeSelectors)
            {
                try
                {
                    var timeElement = element.FindElement(By.CssSelector(selector));
                    var time = timeElement.Text.Trim();
                    if (!string.IsNullOrEmpty(time))
                    {
                        review.RelativeTime = time;
                        break;
                    }
                }
                catch { }
            }

            // Extract business response if exists
            try
            {
                var responseSelectors = new[] { ".CDe7pd .wiI7pd", ".owner-response" };
                foreach (var selector in responseSelectors)
                {
                    try
                    {
                        var responseElement = element.FindElement(By.CssSelector(selector));
                        var response = responseElement.Text.Trim();
                        if (!string.IsNullOrEmpty(response))
                        {
                            review.BusinessResponse = response;
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            review.Time = DateTime.Now;
            review.Language = "en";

            // Only return if we have at least author name
            if (string.IsNullOrEmpty(review.AuthorName))
            {
                return null;
            }

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
            _logger.LogInformation("Searching for: {Query}", searchQuery);
            _driver.Navigate().GoToUrl(searchUrl);

            // Wait for page load
            await Task.Delay(2000);

            // Handle consent if it appears
            await HandleConsent();

            // Wait for search results to load
            await Task.Delay(2000);

            // Try to click on the first search result
            try
            {
                var resultSelectors = new[]
                {
                    "a[href*='/maps/place/']",
                    "div.Nv2PK a",
                    "a.hfpxzc"
                };

                foreach (var selector in resultSelectors)
                {
                    try
                    {
                        var firstResult = _driver.FindElement(By.CssSelector(selector));
                        if (firstResult != null && firstResult.Displayed)
                        {
                            _logger.LogInformation("Clicking first search result");
                            firstResult.Click();
                            await Task.Delay(2000);
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not click first result: {Message}", ex.Message);
            }

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
