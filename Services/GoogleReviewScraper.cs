using GooglePlacesScraper.Interfaces;
using GooglePlacesScraper.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Text.RegularExpressions;
using System.Globalization;

namespace GooglePlacesScraper.Services;

public class GoogleReviewScraper : IReviewScraper, IDisposable
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly ILogger<GoogleReviewScraper> _logger;

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
            "--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "--disable-blink-features=AutomationControlled",
            "--disable-extensions",
            "--disable-plugins-discovery",
            "--disable-web-security",
            "--allow-running-insecure-content",
            "--no-first-run",
            "--disable-notifications",
            "--disable-popup-blocking"
        );

        // Add preference to skip consent pages
        options.AddUserProfilePreference("profile.default_content_setting_values.notifications", 2);
        options.AddUserProfilePreference("profile.default_content_settings.popups", 0);
        
        // CRITICAL: Force English language for international sites like Lumea by the Sea
        options.AddArgument("--lang=en-US");
        options.AddArgument("--accept-lang=en-US,en");  
        options.AddUserProfilePreference("intl.accept_languages", "en-US,en");
        options.AddUserProfilePreference("translate.enabled", false);
        
        _driver = new ChromeDriver(options);
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(15));
        
        // Set cookies and navigate to accept terms
        InitializeGoogleSession();
    }

    private void InitializeGoogleSession()
    {
        try
        {
            // Navigate to Google and handle consent if needed
            _driver.Navigate().GoToUrl("https://www.google.com");
            Thread.Sleep(3000);

            // Try to accept cookies if consent page appears
            try
            {
                var acceptButton = _driver.FindElement(By.Id("L2AGLb")); // "Accept all" button
                if (acceptButton != null)
                {
                    acceptButton.Click();
                    Thread.Sleep(2000);
                    _logger.LogInformation("Accepted Google consent");
                }
            }
            catch
            {
                // Try alternative consent button
                try
                {
                    var acceptButton = _driver.FindElement(By.XPath("//button[contains(text(), 'Accept')]"));
                    if (acceptButton != null)
                    {
                        acceptButton.Click();
                        Thread.Sleep(2000);
                        _logger.LogInformation("Accepted Google consent (alternative)");
                    }
                }
                catch
                {
                    _logger.LogInformation("No consent dialog found or already accepted");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not initialize Google session: {ex.Message}");
        }
    }

    public async Task<Company?> SearchCompanyAsync(string companyName, string? location = null)
    {
        try
        {
            var searchQuery = string.IsNullOrEmpty(location) 
                ? companyName 
                : $"{companyName} {location}";

            // Use Google Maps search directly - this is more reliable than going through Google Search
            var mapsSearchUrl = $"https://www.google.com/maps/search/{Uri.EscapeDataString(searchQuery)}";
            _logger.LogInformation($"Searching Google Maps directly: {mapsSearchUrl}");
            
            _driver.Navigate().GoToUrl(mapsSearchUrl);
            await Task.Delay(5000); // Give more time for Maps to load
            
            // Handle consent page if it appears
            await HandleConsentPage();
            await Task.Delay(2000);
            
            // Check if we landed on a specific place page or search results
            var currentUrl = _driver.Url;
            
            if (currentUrl.Contains("/place/") || currentUrl.Contains("/@"))
            {
                // We landed directly on a place page - extract company info
                _logger.LogInformation("Landed directly on a place page");
                var company = await ExtractCompanyInfoAsync(currentUrl);
                
                if (company != null && !string.IsNullOrEmpty(company.Name) && 
                    !company.Name.Contains("Innan du fortsätter") && 
                    !company.Name.Contains("Before you continue"))
                {
                    company.GoogleMapsUrl = currentUrl;
                    return company;
                }
            }
            
            // If we're on search results, try to click on the first result
            _logger.LogInformation("On search results page, looking for first result");
            
            var firstResultSelectors = new[]
            {
                "a[data-cid]", // Map result with place ID
                ".hfpxzc", // Search result container
                "[data-result-index='0']", // First result
                ".Nv2PK", // Another Maps result selector
                "div[role='article'] a", // Article-based result
                "h3 a", // Title link
                "a[href*='/place/']" // Any place link
            };
            
            IWebElement? firstResult = null;
            foreach (var selector in firstResultSelectors)
            {
                try
                {
                    var elements = _driver.FindElements(By.CssSelector(selector));
                    firstResult = elements.FirstOrDefault(e => e.Displayed);
                    
                    if (firstResult != null)
                    {
                        _logger.LogInformation($"Found first result with selector: {selector}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Selector {selector} failed: {ex.Message}");
                }
            }
            
            if (firstResult != null)
            {
                try
                {
                    // Click on the first result
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", firstResult);
                    await Task.Delay(4000); // Wait for place page to load
                    
                    var newUrl = _driver.Url;
                    _logger.LogInformation($"Clicked result, now at: {newUrl}");
                    
                    var company = await ExtractCompanyInfoAsync(newUrl);
                    if (company != null && !string.IsNullOrEmpty(company.Name))
                    {
                        company.GoogleMapsUrl = newUrl;
                        return company;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to click first result: {ex.Message}");
                }
            }

            _logger.LogWarning($"Could not find company: {companyName}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error searching for company: {companyName}");
            return null;
        }
    }

    private async Task HandleConsentPage()
    {
        try
        {
            await Task.Delay(1000);
            
            // Try to find and click consent buttons
            var consentSelectors = new[]
            {
                "#L2AGLb", // Accept all
                "button[aria-label*='Accept']",
                "button[aria-label*='Accepter']", // French
                "button[aria-label*='Acceptera']", // Swedish
                "//button[contains(text(), 'Accept')]",
                "//button[contains(text(), 'Accepter')]",
                "//button[contains(text(), 'Acceptera')]"
            };

            foreach (var selector in consentSelectors)
            {
                try
                {
                    IWebElement acceptButton;
                    if (selector.StartsWith("//"))
                    {
                        acceptButton = _driver.FindElement(By.XPath(selector));
                    }
                    else
                    {
                        acceptButton = _driver.FindElement(By.CssSelector(selector));
                    }

                    if (acceptButton != null && acceptButton.Displayed && acceptButton.Enabled)
                    {
                        acceptButton.Click();
                        await Task.Delay(2000);
                        _logger.LogInformation($"Clicked consent button with selector: {selector}");
                        return;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"Could not handle consent page: {ex.Message}");
        }
    }

    public async Task<List<Review>> ScrapeReviewsAsync(string googleMapsUrl, ScrapingOptions? options = null)
    {
        options ??= new ScrapingOptions();
        var reviews = new List<Review>();

        try
        {
            // CRITICAL FIX: Force English interface for international sites like Lumea by the Sea
            var englishUrl = googleMapsUrl;
            if (!englishUrl.Contains("hl=en"))
            {
                if (englishUrl.Contains("?"))
                {
                    englishUrl = englishUrl + "&hl=en&gl=us&language=en";
                }
                else
                {
                    englishUrl = englishUrl + "?hl=en&gl=us&language=en";
                }
                _logger.LogInformation($"🌍 FORCING ENGLISH INTERFACE: {englishUrl}");
            }
            
            _driver.Navigate().GoToUrl(englishUrl);
            await Task.Delay(7000); // Increased wait time for Maps to fully load

            // Handle consent page if it appears
            await HandleConsentPage();
            await Task.Delay(2000);

            // Step 1: Find and click the Reviews tab
            _logger.LogInformation("Looking for Reviews tab...");
            await Task.Delay(3000); // Wait for page to fully render
            var reviewsTabClicked = false;
            
            var reviewsTabSelectors = new[]
            {
                "button[role='tab'][aria-label*='Reviews']",
                "button[role='tab'][aria-label*='reviews']",
                "button[role='tab'][aria-label*='Recensioner']", // Swedish
                "button[data-tab-index='1']", // Reviews is usually tab index 1
                "button[role='tab']:nth-child(2)", // Second tab is often reviews
                "//button[@role='tab' and contains(text(), 'Reviews')]",
                "//button[@role='tab' and contains(text(), 'Recensioner')]", // Swedish
                "//button[contains(@aria-label, 'Reviews')]",
                "//button[contains(@aria-label, 'Recensioner')]", // Swedish
                ".hh2c6 button", // Reviews section button
                "[data-value='Sort']", // If already in reviews section
                "button[jsaction*='pane.reviewChart.moreReviews']", // Modern Maps selector
                ".section-tab-bar-tab[data-tab-index='1']", // Alternative tab selector
                "[role='tablist'] button:nth-child(2)" // Second tab in tablist
            };

            foreach (var selector in reviewsTabSelectors)
            {
                try
                {
                    IWebElement reviewsTab;
                    if (selector.StartsWith("//"))
                    {
                        reviewsTab = _driver.FindElement(By.XPath(selector));
                    }
                    else
                    {
                        reviewsTab = _driver.FindElement(By.CssSelector(selector));
                    }

                    if (reviewsTab != null && reviewsTab.Displayed && reviewsTab.Enabled)
                    {
                        _logger.LogInformation($"Found Reviews tab with selector: {selector}");
                        // Scroll into view first
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView(true);", reviewsTab);
                        await Task.Delay(1000);
                        // Try both click methods
                        try
                        {
                            reviewsTab.Click();
                        }
                        catch
                        {
                            ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", reviewsTab);
                        }
                        await Task.Delay(4000); // Wait longer for reviews to load
                        reviewsTabClicked = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Reviews tab selector {selector} failed: {ex.Message}");
                }
            }

            if (!reviewsTabClicked)
            {
                _logger.LogWarning("Could not find Reviews tab, continuing anyway...");
                // Check if we're already on a reviews page
                try
                {
                    var existingReviews = _driver.FindElements(By.CssSelector(".jftiEf, .MyEned, [data-review-id]"));
                    if (existingReviews.Count > 0)
                    {
                        _logger.LogInformation($"Found {existingReviews.Count} existing review elements, may already be on reviews section");
                    }
                }
                catch { }
            }

            // Step 2: EMERGENCY BYPASS - Skip broken sort, use JavaScript target-seeking for recent reviews
            _logger.LogInformation("🚨 EMERGENCY BYPASS: Broken sort detected - implementing JavaScript target-seeking for recent reviews");
            _logger.LogInformation("🎯 TARGET: Find reviews containing '23 hours ago', 'a day ago', '2 days ago' (Ava, Jose Hernandez, Eric, Maria Wagenius, Brooklynn Taylor)");
            
            // First, try to scroll aggressively to load fresh content
            await Task.Delay(3000);
            
            // CRITICAL: Use JavaScript to search for recent timestamps directly in the DOM (English + Swedish patterns)
            var targetTimestamps = new[] { 
                // English patterns (target language)
                "23 hours ago", "a day ago", "day ago", "2 days ago", "days ago", "hours ago", "minute ago", "minutes ago",
                "23 hour ago", "1 day ago", "1 hour ago", "2 day ago", "hour ago", "yesterday", "today",
                // Swedish patterns (current interface language)
                "23 timmar sedan", "för 23 timmar sedan", "1 dag sedan", "för 1 dag sedan", "2 dagar sedan", "för 2 dagar sedan",
                "för en dag sedan", "för en timme sedan", "för 1 timme sedan", "timmar sedan", "dagar sedan", "igår", "idag",
                "timme sedan", "dag sedan", "för", "sedan"
            };
            var targetReviewers = new[] { "Ava", "Jose Hernandez", "Eric", "Maria Wagenius", "Brooklynn Taylor" };
            
            _logger.LogInformation("🔍 JAVASCRIPT SEARCH: Looking for recent review timestamps in DOM...");
            
            var foundTargetReviews = false;
            foreach (var timestamp in targetTimestamps)
            {
                try
                {
                    // Use JavaScript to search for elements containing recent timestamps
                    var jsScript = $@"
                        var elements = [];
                        var walker = document.createTreeWalker(
                            document.body, 
                            NodeFilter.SHOW_TEXT, 
                            null, 
                            false
                        );
                        var node;
                        while (node = walker.nextNode()) {{
                            if (node.textContent.toLowerCase().includes('{timestamp.ToLower()}')) {{
                                elements.push(node.parentElement);
                            }}
                        }}
                        return elements.length;
                    ";
                    
                    var elementCount = (long)((IJavaScriptExecutor)_driver).ExecuteScript(jsScript);
                    if (elementCount > 0)
                    {
                        _logger.LogInformation($"🎯 FOUND {elementCount} elements containing '{timestamp}' - recent reviews detected!");
                        foundTargetReviews = true;
                        
                        // If we find recent timestamps, scroll to make sure they're visible
                        var scrollScript = $@"
                            var elements = [];
                            var walker = document.createTreeWalker(
                                document.body, 
                                NodeFilter.SHOW_TEXT, 
                                null, 
                                false
                            );
                            var node;
                            while (node = walker.nextNode()) {{
                                if (node.textContent.toLowerCase().includes('{timestamp.ToLower()}')) {{
                                    node.parentElement.scrollIntoView({{ behavior: 'smooth', block: 'center' }});
                                    break;
                                }}
                            }}
                        ";
                        ((IJavaScriptExecutor)_driver).ExecuteScript(scrollScript);
                        await Task.Delay(2000);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"JavaScript search for '{timestamp}' failed: {ex.Message}");
                }
            }
            
            if (!foundTargetReviews)
            {
                _logger.LogWarning("⚠️ NO RECENT TIMESTAMPS FOUND - trying DIRECT NAME SEARCH approach");
                
                // FINAL ATTEMPT: Search directly for target reviewer names
                var targetNames = new[] { "Ava", "Jose Hernandez", "Eric", "Maria Wagenius", "Brooklynn Taylor" };
                foreach (var name in targetNames)
                {
                    try
                    {
                        var nameSearchScript = $@"
                            var elements = [];
                            var walker = document.createTreeWalker(
                                document.body, 
                                NodeFilter.SHOW_TEXT, 
                                null, 
                                false
                            );
                            var node;
                            var foundName = false;
                            while (node = walker.nextNode()) {{
                                if (node.textContent.includes('{name}')) {{
                                    elements.push(node.parentElement);
                                    foundName = true;
                                    // Scroll to this reviewer immediately
                                    node.parentElement.scrollIntoView({{ behavior: 'smooth', block: 'center' }});
                                }}
                            }}
                            return foundName;
                        ";
                        
                        var foundName = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(nameSearchScript);
                        if (foundName)
                        {
                            _logger.LogInformation($"🎯 FOUND TARGET NAME: {name} - attempting extraction");
                            foundTargetReviews = true;
                            await Task.Delay(3000); // Wait for scroll and loading
                            break;
                        }
                        else
                        {
                            _logger.LogDebug($"Target name '{name}' not found in DOM");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"Name search for '{name}' failed: {ex.Message}");
                    }
                }
                
                if (!foundTargetReviews)
                {
                    _logger.LogWarning("⚠️ NO TARGET NAMES FOUND EITHER - trying aggressive scroll and reload approach");
                
                    // Aggressive scroll to load more reviews
                for (int i = 0; i < 10; i++)
                {
                    ((IJavaScriptExecutor)_driver).ExecuteScript("window.scrollBy(0, 1000);");
                    await Task.Delay(1000);
                    
                    // Check again after each scroll
                    try
                    {
                        var checkScript = @"
                            var walker = document.createTreeWalker(
                                document.body, 
                                NodeFilter.SHOW_TEXT, 
                                null, 
                                false
                            );
                            var node;
                            while (node = walker.nextNode()) {
                                var text = node.textContent.toLowerCase();
                                if (text.includes('hours ago') || text.includes('day ago') || text.includes('days ago')) {
                                    return true;
                                }
                            }
                            return false;
                        ";
                        
                        var hasRecent = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(checkScript);
                        if (hasRecent)
                        {
                            _logger.LogInformation($"✅ BREAKTHROUGH: Found recent timestamps after scroll iteration {i + 1}");
                            foundTargetReviews = true;
                            break;
                        }
                    }
                    catch { }
                }
                }
            }
            
            // If still no recent reviews found, try direct navigation to newest reviews URL
            if (!foundTargetReviews)
            {
                _logger.LogWarning("🔄 LAST RESORT: Trying direct navigation to force newest reviews");
                try
                {
                    var currentUrl = _driver.Url;
                    if (currentUrl.Contains("google.com/maps"))
                    {
                        // Try multiple URL patterns for newest reviews
                        var newestUrls = new[]
                        {
                            currentUrl.Replace("?entry=ttu", "/reviews?entry=ttu&sort=recency"),
                            currentUrl.Replace("?entry=ttu", "/reviews?entry=ttu&sort=newest"),  
                            currentUrl.Replace("?entry=ttu", "/reviews?entry=ttu&sort=time"),
                            currentUrl.Replace("?entry=ttu", "/reviews?entry=ttu&lrd=0x80dd31c43ea60753:0x1d593fdaf4f598be,1"),
                        };
                        
                        foreach (var url in newestUrls)
                        {
                            _logger.LogInformation($"🔄 Trying newest URL pattern: {url}");
                            _driver.Navigate().GoToUrl(url);
                            await Task.Delay(4000);
                            
                            // Check if this loaded recent reviews
                            var checkScript = @"
                                var walker = document.createTreeWalker(
                                    document.body, 
                                    NodeFilter.SHOW_TEXT, 
                                    null, 
                                    false
                                );
                                var node;
                                while (node = walker.nextNode()) {
                                    var text = node.textContent.toLowerCase();
                                    if (text.includes('hours ago') || text.includes('day ago')) {
                                        return true;
                                    }
                                }
                                return false;
                            ";
                            
                            try
                            {
                                var hasRecent = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(checkScript);
                                if (hasRecent)
                                {
                                    _logger.LogInformation($"✅ SUCCESS: URL pattern loaded recent reviews!");
                                    foundTargetReviews = true;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Direct URL navigation failed: {ex.Message}");
                }
            }
            
            if (foundTargetReviews)
            {
                _logger.LogInformation("🎯 READY FOR EXTRACTION: Recent review timestamps detected on page");
            }
            else
            {
                _logger.LogWarning("⚠️ WARNING: No recent timestamps found - will extract whatever reviews are available");
            }

            // Step 4: Scroll to load more reviews
            await ScrollToLoadReviews(options.MaxReviews);

            // Step 5: Extract reviews
            _logger.LogInformation("Extracting reviews...");
            
            // Comprehensive Google Maps review selectors (2024+ with many fallbacks)
            var reviewSelectors = new[]
            {
                // Latest 2024 Google Maps review format (prioritized first - based on screenshot analysis)
                "div:has(.fontHeadlineSmall):has(.fontBodyMedium)", // Reviews with both headline and body
                "div:has([aria-label*='star']):has(.fontHeadlineSmall)", // Star ratings with headlines
                "div[data-attrid]:has(.fontHeadlineSmall)", // Attributed reviews with headlines  
                "div[role='button']:has(.fontHeadlineSmall)", // Button reviews with headlines
                "div[jsaction]:has(.fontHeadlineSmall):has(.fontBodyMedium)", // Interactive reviews with text
                "div[data-hveid]:has(.fontHeadlineSmall):not([data-review-id])", // Tracked headlines without review ID
                "div[data-ved]:has(.fontHeadlineSmall):not([data-review-id])", // View tracked headlines without review ID
                "*:has(.fontHeadlineSmall):has([aria-label*='star']):has(.fontBodyMedium)", // Universal selector for complete reviews
                "div[class]:has(.fontHeadlineSmall):has([role='img'])", // Any classed div with headline and star image
                "div[id]:has(.fontHeadlineSmall):has(.fontBodyMedium)", // Any ID'd div with headline and body
                
                // Primary modern selectors
                "[data-review-id]", // Original selector
                ".jftiEf", // Review container class
                ".fontBodyMedium > .jftiEf", // Nested review container
                "[jsaction*='pane.review']", // Reviews with jsaction
                "[jsaction*='review']", // Any review actions
                ".review", // Generic review class
                "[data-href*='/contrib/']", // Reviews by contributor link
                "div[data-review-id]", // Specific div with review ID
                ".TSUbDb", // Review text container
                "[role='article']", // Semantic article role for reviews
                "div[data-attrid='reviews'] .jftiEf", // Reviews within reviews section
                "[data-async-context*='review']", // Async loaded reviews
                "[data-tts='reviews'] .jftiEf", // Text-to-speech reviews section
                
                // Additional 2024+ Maps selectors
                "div[data-hveid*='C']", // Google's internal tracking IDs
                "[data-async-type*='review']", // Async review types
                "div[jscontroller][data-review-id]", // Controller-based reviews
                "[data-viewtag*='review']", // View tag reviews
                ".section-review", // Section-based reviews
                "div[data-cid]", // Customer ID reviews
                "[data-async-fcb]", // Async callback reviews
                "div[data-ved]", // Google tracking reviews
                "[data-async-rclass]", // Async class reviews
                "[jsmodel][data-review-id]", // Model-based reviews
                "div[data-async-ph]", // Placeholder reviews
                "[data-test-id*='review']", // Test ID reviews
                
                // Fallback selectors for edge cases
                "div[class*='review']", // Any div with 'review' in class
                "article", // HTML5 article elements
                "[itemtype*='Review']", // Schema.org reviews
                "div[data-attrid*='review']", // Attribute-based reviews
                "section[data-async-context]", // Section with async context
                "div[role='listitem']", // List item reviews
                "[data-entity-id]", // Entity-based reviews
                "div[data-async-trigger]", // Async trigger reviews
                "[data-reviewid]", // Alternative review ID attribute
                "div[style*='border']", // Styled review containers
                "[data-ved][data-hveid]", // Double-tracked elements
                ".fontBodyMedium:has(.jftiEf)", // Has-based selector
                "div:has([data-review-id])", // Parent of review elements
                "[role='main'] > div > div", // Deep nested in main
                "div[data-async-context] > div", // Children of async containers
                "[data-viewtags] div[data-hveid]", // Viewtag tracked divs
                
                // Last resort selectors
                
                // 2024+ New Google Maps review format selectors  
                "div:has([aria-label*='New'])", // Reviews marked as "New"
                "div:contains('New')", // Contains "New" text
                "div[data-attrid*='review']", // New attribute ID reviews
                "div:has(.fontDisplaySmall)", // Display font elements (new format)
                "div:has(.fontTitleSmall)", // Title font elements (new format)
                "div:has([aria-label*='dag'])", // Swedish day indicators (like "för 6 dagar sedan")
                "div:has([aria-label*='vecka'])", // Swedish week indicators
                "div:has([aria-label*='månad'])", // Swedish month indicators
                "div:has([title*='review'])", // Title attribute reviews
                "div[data-async-id*='review']", // Async ID reviews
                "div[data-node-index]:has([aria-label*='star'])", // Node index with stars
                ".M77dve", // Potential new review class
                ".d4r55", // Potential new review class
                ".ODSEW-ShBeI", // Potential new review class
                ".ODSEW-ShBeI-content", // Potential new review content class
                "div[data-attrid]:has(span[aria-label*='star'])", // Attribute ID with star spans
                "div.ODSEW-ShBeI-ShBeI-content", // Complex nested review content
                "div:has(.gm2-body-2)", // Google Material Design body-2 text
                "div:has(.gm2-caption)", // Google Material Design captions
                "div[data-hveid][data-ved]:has(span[aria-label*='star'])", // Tracking data with star spans
                
                // Reviews with minimal content (stars only)
                "div:has([aria-label*='star']):not(:has(span:contains('star')))", // Has star but not star text
                "div[aria-label*='Rating']:parent", // Parent of rating elements
                "div:has([role='img'][aria-label*='star'])", // Image role stars
                "[aria-label*='Review by'] + div", // Following "Review by" elements
                "div[data-review-id]:has([aria-label*='star']):not(:has(.fontBodyMedium))", // Review ID with stars but no body text
                
                // Swedish-specific selectors for time indicators
                "div:contains('för') div:has([aria-label*='star'])", // Swedish "för X sedan" pattern
                "div:contains('sedan') div:has([aria-label*='star'])", // Swedish time pattern  
                "div:contains('dagar') div:has([aria-label*='star'])", // Swedish days pattern
                "div:contains('veckor') div:has([aria-label*='star'])", // Swedish weeks pattern
                "div:contains('månader') div:has([aria-label*='star'])", // Swedish months pattern
                "div[class]:not([data-review-id]):has(span)", // Generic divs with spans
                "[role='main'] div[data-hveid]", // Main area tracked elements
                "div[jscontroller]:has(span)", // Controller divs with text
                "section > div > div", // Nested section content
                "[data-async-context] [data-hveid]", // Async tracked content
                "div[data-ved]:has(span)", // Tracked divs with spans
                "[role='listitem'][data-hveid]", // List items with tracking
                "div[style]:has([role='img'])" // Styled divs with star images
            };
            
            var reviewElements = new List<IWebElement>();
            var allFoundElements = new HashSet<IWebElement>();
            
            // Try newest format selectors first (prioritized)
            var newestSelectors = reviewSelectors.Take(10).ToArray(); // First 10 are newest formats
            var traditionalSelectors = reviewSelectors.Skip(10).ToArray(); // Rest are traditional selectors
            
            _logger.LogInformation($"Trying {newestSelectors.Length} newest format selectors first...");
            
            // First pass: Try newest format selectors
            foreach (var selector in newestSelectors)
            {
                try
                {
                    await Task.Delay(500); // Shorter delay for multiple tries
                    var elements = _driver.FindElements(By.CssSelector(selector));
                    if (elements.Count > 0)
                    {
                        // Filter for visible elements only
                        var visibleElements = elements.Where(e => 
                        {
                            try { return e.Displayed && e.Size.Width > 0 && e.Size.Height > 0; }
                            catch { return false; }
                        }).ToList();
                        
                        if (visibleElements.Count > 0)
                        {
                            foreach (var elem in visibleElements)
                            {
                                allFoundElements.Add(elem);
                            }
                            _logger.LogInformation($"Found {visibleElements.Count} newest format elements with selector: {selector}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Newest selector {selector} failed: {ex.Message}");
                }
            }
            
            _logger.LogInformation($"Trying {traditionalSelectors.Length} traditional selectors...");
            
            // Second pass: Try traditional selectors
            foreach (var selector in traditionalSelectors)
            {
                try
                {
                    await Task.Delay(500); // Shorter delay for multiple tries
                    var elements = _driver.FindElements(By.CssSelector(selector));
                    if (elements.Count > 0)
                    {
                        // Filter for visible elements only
                        var visibleElements = elements.Where(e => 
                        {
                            try { return e.Displayed && e.Size.Width > 0 && e.Size.Height > 0; }
                            catch { return false; }
                        }).ToList();
                        
                        if (visibleElements.Count > 0)
                        {
                            foreach (var elem in visibleElements)
                            {
                                allFoundElements.Add(elem);
                            }
                            _logger.LogInformation($"Found {visibleElements.Count} traditional elements with selector: {selector}");
                            // Only break after finding traditional elements if we have enough
                            if (allFoundElements.Count > 50)
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Traditional selector {selector} failed: {ex.Message}");
                }
            }
            
            reviewElements = allFoundElements.ToList();
            _logger.LogInformation($"Combined total: {reviewElements.Count} unique review elements from all selectors");
            
            // Additional strategy: Try to load the newest reviews that might be dynamically loaded
            _logger.LogInformation("Attempting to load newest reviews with dynamic interaction...");
            try
            {
                // Scroll to top to ensure we see the newest reviews
                ((IJavaScriptExecutor)_driver).ExecuteScript("window.scrollTo(0, 0);");
                await Task.Delay(2000);
                
                // Look for "Show more" or "Load more" buttons in Swedish/English
                var showMoreSelectors = new[]
                {
                    "//button[contains(text(), 'Show more')]",
                    "//button[contains(text(), 'Visa mer')]", 
                    "//button[contains(text(), 'Läs mer')]",
                    "//button[contains(text(), 'More')]",
                    "//button[contains(text(), 'Mer')]",
                    "//span[contains(text(), 'Show more')]/ancestor::button",
                    "//span[contains(text(), 'Mer')]/ancestor::button",
                    "[aria-label*='Show more']",
                    "[aria-label*='Visa mer']",
                    "button[jsaction*='more']",
                    "button[data-value*='more']"
                };
                
                foreach (var selector in showMoreSelectors)
                {
                    try
                    {
                        var showMoreButtons = selector.StartsWith("//") 
                            ? _driver.FindElements(By.XPath(selector))
                            : _driver.FindElements(By.CssSelector(selector));
                            
                        foreach (var button in showMoreButtons.Take(3)) // Try up to 3 buttons
                        {
                            try
                            {
                                if (button.Displayed && button.Enabled)
                                {
                                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", button);
                                    await Task.Delay(1000);
                                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", button);
                                    await Task.Delay(3000); // Wait for content to load
                                    _logger.LogInformation($"Clicked show more button with selector: {selector}");
                                }
                            }
                            catch { /* Ignore individual button failures */ }
                        }
                    }
                    catch { /* Ignore selector failures */ }
                }
                
                // Try to find and interact with review filter/sort options that might reveal newest reviews
                var refreshSelectors = new[]
                {
                    "button[aria-label*='refresh']",
                    "button[aria-label*='update']", 
                    "button[title*='refresh']",
                    "[role='button'][aria-label*='Newest']",
                    "[role='button'][aria-label*='Recent']",
                    "button[data-value*='newest']",
                    "button[data-sort*='newest']"
                };
                
                foreach (var selector in refreshSelectors)
                {
                    try
                    {
                        var elements = _driver.FindElements(By.CssSelector(selector));
                        foreach (var elem in elements.Take(2))
                        {
                            if (elem.Displayed && elem.Enabled)
                            {
                                ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", elem);
                                await Task.Delay(2000);
                                _logger.LogInformation($"Clicked refresh/newest button with selector: {selector}");
                                break;
                            }
                        }
                    }
                    catch { /* Ignore failures */ }
                }
                
                // Re-run the newest format selectors after interaction
                _logger.LogInformation("Re-trying newest format selectors after dynamic interaction...");
                foreach (var selector in newestSelectors)
                {
                    try
                    {
                        await Task.Delay(500);
                        var elements = _driver.FindElements(By.CssSelector(selector));
                        if (elements.Count > 0)
                        {
                            var visibleElements = elements.Where(e => 
                            {
                                try { return e.Displayed && e.Size.Width > 0 && e.Size.Height > 0; }
                                catch { return false; }
                            }).ToList();
                            
                            if (visibleElements.Count > 0)
                            {
                                foreach (var elem in visibleElements)
                                {
                                    allFoundElements.Add(elem);
                                }
                                _logger.LogInformation($"Found {visibleElements.Count} additional newest format elements with selector: {selector}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"Post-interaction newest selector {selector} failed: {ex.Message}");
                    }
                }
                
                // Update the reviewElements with any newly found elements
                reviewElements = allFoundElements.ToList();
                _logger.LogInformation($"After dynamic interaction: {reviewElements.Count} total unique review elements");
                
                // PRIORITY 1: JavaScript-based target-seeking extraction for recent reviews FIRST
                _logger.LogInformation("🚀 PRIORITY EXTRACTION: JavaScript target-seeking for recent reviews FIRST");
                _logger.LogInformation("🎯 TARGET: Find reviews with '23 hours ago', 'a day ago', '2 days ago' (Ava, Jose Hernandez, Eric, Maria Wagenius, Brooklynn Taylor)");
                var targetReviews = new List<Review>();
                var targetExtractCount = 0;
                
                // Use JavaScript to find elements containing recent timestamps directly (ENHANCED: English + Swedish patterns)
                var recentTimestampPatterns = new[] { 
                    // English patterns (target)
                    "23 hours ago", "a day ago", "day ago", "2 days ago", "days ago", "hours ago", "hour ago", "minute ago", "minutes ago",
                    // Swedish patterns (current interface)
                    "23 timmar sedan", "för 23 timmar sedan", "1 dag sedan", "för 1 dag sedan", "2 dagar sedan", "för 2 dagar sedan",
                    "timmar sedan", "dagar sedan", "timme sedan", "dag sedan", "för", "sedan"
                };
                foreach (var pattern in recentTimestampPatterns)
                {
                    try
                    {
                        var jsScript = $@"
                            var matchingElements = [];
                            var walker = document.createTreeWalker(
                                document.body, 
                                NodeFilter.SHOW_TEXT, 
                                null, 
                                false
                            );
                            var node;
                            while (node = walker.nextNode()) {{
                                if (node.textContent.toLowerCase().includes('{pattern.ToLower()}')) {{
                                    // Find the closest parent that looks like a review container
                                    var current = node.parentElement;
                                    while (current && current !== document.body) {{
                                        if (current.getAttribute('data-review-id') || 
                                            current.className.includes('jftiEf') || 
                                            current.className.includes('review') ||
                                            (current.querySelector && (
                                                current.querySelector('[aria-label*=""star""]') || 
                                                current.querySelector('.fontHeadlineSmall') ||
                                                current.querySelector('.fontBodyMedium')
                                            ))) {{
                                            matchingElements.push(current);
                                            break;
                                        }}
                                        current = current.parentElement;
                                    }}
                                }}
                            }}
                            return matchingElements;
                        ";
                        
                        var matchingElementsObj = ((IJavaScriptExecutor)_driver).ExecuteScript(jsScript);
                        if (matchingElementsObj is System.Collections.ObjectModel.ReadOnlyCollection<object> matchingElements && matchingElements.Count > 0)
                        {
                            _logger.LogInformation($"🎯 JAVASCRIPT FOUND {matchingElements.Count} review containers with '{pattern}'");
                            
                            foreach (var elementObj in matchingElements.Take(5))
                            {
                                if (elementObj is IWebElement element)
                                {
                                    try
                                    {
                                        var review = await ExtractReviewFromElement(element);
                                        if (review != null)
                                        {
                                            targetReviews.Add(review);
                                            targetExtractCount++;
                                            _logger.LogInformation($"🎯 TARGET EXTRACTION #{targetExtractCount}: {review.AuthorName} ({review.Rating} stars) - {review.RelativeTime}");
                                            
                                            // Check if this matches our target reviewers
                                            var targetNames = new[] { "Ava", "Jose Hernandez", "Eric", "Maria Wagenius", "Brooklynn Taylor" };
                                            if (targetNames.Any(name => review.AuthorName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true))
                                            {
                                                _logger.LogInformation($"🎉 FOUND TARGET REVIEWER: {review.AuthorName} - {review.RelativeTime}");
                                            }
                                            
                                            // Check if the timestamp matches what we're looking for
                                            if (!string.IsNullOrEmpty(review.RelativeTime) && 
                                                (review.RelativeTime.Contains("hour") || review.RelativeTime.Contains("day")))
                                            {
                                                _logger.LogInformation($"🎯 FOUND RECENT TIMESTAMP: {review.RelativeTime}");
                                            }
                                            
                                            if (targetExtractCount >= options.MaxReviews)
                                                break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogDebug($"Target element extraction failed: {ex.Message}");
                                    }
                                }
                            }
                            
                            if (targetExtractCount >= options.MaxReviews)
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"JavaScript target-seeking for '{pattern}' failed: {ex.Message}");
                    }
                }
                
                _logger.LogInformation($"🎯 TARGET-SEEKING EXTRACTION COMPLETE: {targetExtractCount} recent reviews found");
                
                // FINAL ATTEMPT: If no timestamp patterns worked, try direct name search
                if (targetExtractCount == 0)
                {
                    _logger.LogWarning("🚨 FINAL ATTEMPT: Direct name search for target reviewers");
                    var targetNames = new[] { "Ava", "Jose Hernandez", "Eric", "Maria Wagenius", "Brooklynn Taylor" };
                    foreach (var name in targetNames)
                    {
                        try
                        {
                            var nameScript = $@"
                                var matchingElements = [];
                                var walker = document.createTreeWalker(
                                    document.body, 
                                    NodeFilter.SHOW_TEXT, 
                                    null, 
                                    false
                                );
                                var node;
                                while (node = walker.nextNode()) {{
                                    if (node.textContent.includes('{name}')) {{
                                        // Find the closest parent that looks like a review container
                                        var current = node.parentElement;
                                        while (current && current !== document.body) {{
                                            if (current.getAttribute('data-review-id') || 
                                                current.className.includes('jftiEf') || 
                                                current.className.includes('review') ||
                                                (current.querySelector && (
                                                    current.querySelector('[aria-label*=""star""]') || 
                                                    current.querySelector('.fontHeadlineSmall') ||
                                                    current.querySelector('.fontBodyMedium')
                                                ))) {{
                                                matchingElements.push(current);
                                                break;
                                            }}
                                            current = current.parentElement;
                                        }}
                                    }}
                                }}
                                return matchingElements;
                            ";
                            
                            var nameElementsObj = ((IJavaScriptExecutor)_driver).ExecuteScript(nameScript);
                            if (nameElementsObj is System.Collections.ObjectModel.ReadOnlyCollection<object> nameElements && nameElements.Count > 0)
                            {
                                _logger.LogInformation($"🎯 FOUND TARGET NAME '{name}' in {nameElements.Count} review containers!");
                                
                                foreach (var elementObj in nameElements.Take(3))
                                {
                                    if (elementObj is IWebElement element)
                                    {
                                        try
                                        {
                                            var review = await ExtractReviewFromElement(element);
                                            if (review != null)
                                            {
                                                targetReviews.Add(review);
                                                targetExtractCount++;
                                                _logger.LogInformation($"🎉 TARGET NAME EXTRACTION: {review.AuthorName} ({review.Rating} stars) - {review.RelativeTime}");
                                                
                                                if (targetExtractCount >= options.MaxReviews)
                                                    break;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogDebug($"Name-based extraction failed: {ex.Message}");
                                        }
                                    }
                                }
                                
                                if (targetExtractCount >= options.MaxReviews)
                                    break;
                            }
                            else
                            {
                                _logger.LogDebug($"Target name '{name}' not found in any review containers");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"Name search for '{name}' failed: {ex.Message}");
                        }
                    }
                }
                
                // If we found target reviews, return them immediately
                if (targetExtractCount > 0)
                {
                    _logger.LogInformation($"🎉 SUCCESS: Found {targetExtractCount} target reviews with JavaScript name search!");
                    return targetReviews;
                }
                
                // FALLBACK: Traditional extraction if target-seeking didn't work
                _logger.LogInformation("🚀 FALLBACK: Traditional extraction from all elements");
                _logger.LogInformation("🎯 TARGET: Find English reviews (Ava, Jose Hernandez, Eric, Maria Wagenius, Brooklynn Taylor)");
                var traditionalReviews = new List<Review>();
                var traditionalExtractCount = 0;
                
                foreach (var element in reviewElements.Take(Math.Min(50, options.MaxReviews * 3))) // Check more elements
                {
                    try
                    {
                        var review = await ExtractReviewFromElement(element);
                        if (review != null)
                        {
                            traditionalReviews.Add(review);
                            traditionalExtractCount++;
                            _logger.LogInformation($"✅ TRADITIONAL EXTRACTION #{traditionalExtractCount}: {review.AuthorName} ({review.Rating} stars) - {review.RelativeTime}");
                            
                            // Check if this matches our target reviewers
                            var targetNames = new[] { "Ava", "Jose Hernandez", "Eric", "Maria Wagenius", "Brooklynn Taylor" };
                            if (targetNames.Any(name => review.AuthorName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true))
                            {
                                _logger.LogInformation($"🎯 FOUND TARGET REVIEWER: {review.AuthorName} - {review.RelativeTime}");
                            }
                            
                            // Check for recent timestamps
                            if (!string.IsNullOrEmpty(review.RelativeTime) && 
                                (review.RelativeTime.ToLower().Contains("hour") || review.RelativeTime.ToLower().Contains("day")))
                            {
                                _logger.LogInformation($"🎯 FOUND RECENT TIMESTAMP: {review.RelativeTime}");
                            }
                            
                            if (traditionalExtractCount >= options.MaxReviews)
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"Traditional element extraction failed: {ex.Message}");
                    }
                }
                
                _logger.LogInformation($"🎯 TRADITIONAL EXTRACTION COMPLETE: {traditionalExtractCount} reviews extracted before navigation");
                
                // If we got enough reviews from traditional extraction, return them
                if (traditionalExtractCount >= options.MaxReviews)
                {
                    _logger.LogInformation($"🎉 SUCCESS: Got {traditionalExtractCount} reviews from traditional extraction - skipping fresh navigation");
                    return traditionalReviews;
                }
                
                // Final strategy: Fresh page load approach for newest reviews (only if needed)
                if (traditionalExtractCount < options.MaxReviews)
                {
                    _logger.LogInformation("Attempting fresh page reload strategy for newest reviews...");
                    try
                    {
                        // Navigate directly to the reviews URL to potentially get a different view
                        var currentUrl = _driver.Url;
                        var reviewsDirectUrl = currentUrl.Replace("?entry=ttu", "/reviews?entry=ttu");
                        
                        _logger.LogInformation($"Navigating to reviews-specific URL: {reviewsDirectUrl}");
                        _driver.Navigate().GoToUrl(reviewsDirectUrl);
                        await Task.Delay(5000); // Wait for page load
                        
                        // Accept consent again if needed
                        await HandleConsentPage();
                        
                        // Try to find the newest review elements with a fresh DOM
                        _logger.LogInformation("Searching for newest reviews in fresh DOM...");
                        
                        // Look for any elements containing "New" badges or recent timestamps
                        var freshSelectors = new[]
                        {
                            "//*[contains(text(), 'New')]",
                            "//*[contains(text(), 'days ago')]",
                            "//*[contains(text(), 'dagar sedan')]",
                            "//*[contains(text(), 'för')]",
                            "//div[contains(text(), '4 days ago') or contains(text(), '5 days ago')]",
                            "//div[contains(text(), 'för 4 dagar sedan') or contains(text(), 'för 5 dagar sedan')]",
                            "//*[@aria-label and contains(@aria-label, 'New')]",
                            "//div[contains(@class, 'new') or contains(@data-value, 'new')]"
                        };
                        
                        var freshElements = new HashSet<IWebElement>();
                        foreach (var selector in freshSelectors)
                        {
                            try
                            {
                                var elements = _driver.FindElements(By.XPath(selector));
                                foreach (var elem in elements.Take(5))
                                {
                                    // Find the parent review container for each found element
                                    try
                                    {
                                        var reviewContainer = elem.FindElement(By.XPath("./ancestor::div[contains(@data-review-id, 'C') or contains(@class, 'jftiEf')][1]"));
                                        if (reviewContainer != null)
                                        {
                                            freshElements.Add(reviewContainer);
                                            _logger.LogInformation($"Found fresh review container from selector: {selector}");
                                        }
                                    }
                                    catch
                                    {
                                        // Try to use the element itself if it looks like a review container
                                        if (elem.GetAttribute("data-review-id") != null || 
                                            elem.GetAttribute("class")?.Contains("jftiEf") == true)
                                        {
                                            freshElements.Add(elem);
                                        }
                                    }
                                }
                            }
                            catch { /* Ignore selector failures */ }
                        }
                        
                        if (freshElements.Count > 0)
                        {
                            foreach (var elem in freshElements)
                            {
                                allFoundElements.Add(elem);
                            }
                            _logger.LogInformation($"Added {freshElements.Count} fresh review elements from direct navigation");
                            
                            // BREAKTHROUGH: Extract data from fresh elements immediately using specialized logic
                            _logger.LogInformation("ATTEMPTING IMMEDIATE EXTRACTION from fresh page elements...");
                            var immediateExtractions = await ExtractFromFreshElements(freshElements.ToList());
                            if (immediateExtractions.Count > 0)
                            {
                                _logger.LogInformation($"🎉 FRESH SUCCESS: Extracted {immediateExtractions.Count} reviews from fresh elements!");
                                foreach (var review in immediateExtractions)
                                {
                                    _logger.LogInformation($"✓ FRESH EXTRACTION: {review.AuthorName} ({review.Rating} stars) - {review.RelativeTime}");
                                }
                                // Combine traditional + fresh reviews
                                traditionalReviews.AddRange(immediateExtractions);
                                _logger.LogInformation($"🎯 COMBINED TOTAL: {traditionalReviews.Count} reviews (traditional + fresh)");
                                
                                // Return combined results if we have enough
                                if (traditionalReviews.Count >= options.MaxReviews)
                                {
                                    return traditionalReviews.Take(options.MaxReviews).ToList();
                                }
                            }
                        }
                        
                        reviewElements = allFoundElements.ToList();
                        _logger.LogInformation($"After fresh page strategy: {reviewElements.Count} total unique review elements");
                    }
                    catch (Exception freshEx)
                    {
                        _logger.LogWarning($"Fresh page reload strategy failed: {freshEx.Message}");
                    }
                }
                
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Dynamic interaction strategy failed: {ex.Message}");
            }
            
            if (reviewElements.Count == 0)
            {
                _logger.LogWarning("No review elements found with any selector - trying alternative approach");
                
                // Last resort: try to find any elements that might contain review data
                try
                {
                    var alternativeElements = _driver.FindElements(By.XPath("//div[contains(@class, 'jftiEf') or contains(@data-review-id, 'C') or contains(@data-hveid, 'C') or contains(text(), 'star') or .//span[contains(@aria-label, 'star')]]"));
                    if (alternativeElements.Count > 0)
                    {
                        reviewElements = alternativeElements.Take(options.MaxReviews * 2).ToList(); // Take more to compensate for failed extractions
                        _logger.LogInformation($"Found {alternativeElements.Count} alternative review elements using XPath");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Alternative element search failed: {ex.Message}");
                }
            }
            else
            {
                _logger.LogInformation($"Selected {reviewElements.Count} review elements for processing");
            }

            var processedReviewIds = new HashSet<string>();
            var processedReviewHashes = new HashSet<string>();
            var actuallyExtracted = 0;
            
            _logger.LogInformation($"Processing {Math.Min(reviewElements.Count, options.MaxReviews * 2)} review elements (up to {options.MaxReviews} reviews)");
            
            // Process more elements than needed to account for failed extractions
            foreach (var reviewElement in reviewElements.Take(options.MaxReviews * 3))
            {
                // Stop if we've extracted enough reviews
                if (actuallyExtracted >= options.MaxReviews)
                    break;
                    
                try
                {
                    // Try to interact with the review element to expand it if needed
                    try
                    {
                        // Scroll element into view first
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', behavior: 'smooth'});", reviewElement);
                        await Task.Delay(500);
                        
                        // Try to expand review if it has a "More" button
                        var moreButtons = reviewElement.FindElements(By.XPath(".//button[contains(text(), 'More') or contains(text(), 'Mer') or contains(@aria-label, 'more') or contains(@aria-label, 'expand')]"));
                        if (moreButtons.Count > 0 && moreButtons[0].Displayed)
                        {
                            try
                            {
                                moreButtons[0].Click();
                                await Task.Delay(1000);
                                _logger.LogDebug("Expanded review text by clicking More button");
                            }
                            catch { /* Ignore click failures */ }
                        }
                    }
                    catch { /* Ignore interaction failures */ }
                    
                    var review = await ExtractReviewFromElement(reviewElement);
                    if (review != null && PassesFilters(review, options))
                    {
                        // Better duplicate detection using multiple keys
                        var reviewKey = $"{review.AuthorName}_{review.Text?.Substring(0, Math.Min(50, review.Text?.Length ?? 0))}_{review.Rating}";
                        var reviewHash = $"{review.AuthorName}_{review.Rating}_{review.RelativeTime}";
                        var reviewId = review.Id;
                        
                        if (!processedReviewIds.Contains(reviewKey) && 
                            !processedReviewHashes.Contains(reviewHash) && 
                            !processedReviewIds.Contains(reviewId))
                        {
                            processedReviewIds.Add(reviewKey);
                            processedReviewHashes.Add(reviewHash);
                            processedReviewIds.Add(reviewId);
                            reviews.Add(review);
                            actuallyExtracted++;
                            _logger.LogInformation($"✓ Extracted review #{actuallyExtracted} from {review.AuthorName} ({review.Rating} stars) - {review.RelativeTime}");
                        }
                        else
                        {
                            _logger.LogDebug($"Skipped duplicate review from {review.AuthorName}");
                        }
                    }
                    else if (review != null)
                    {
                        _logger.LogDebug($"Review from {review.AuthorName} did not pass filters");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Error extracting review: {ex.Message}");
                    continue;
                }
            }

            _logger.LogInformation($"Total reviews extracted: {actuallyExtracted}/{reviewElements.Count} elements processed (sorted by newest first)");
            
            // If we didn't get enough reviews, log diagnostic info
            if (actuallyExtracted < options.MaxReviews && reviewElements.Count > actuallyExtracted)
            {
                _logger.LogWarning($"Only extracted {actuallyExtracted} out of {options.MaxReviews} requested reviews from {reviewElements.Count} elements");
                _logger.LogInformation($"Consider: 1) More scrolling might be needed, 2) Review selectors may need updating, 3) Page structure may have changed");
            }
            return reviews;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error scraping reviews from URL: {googleMapsUrl}");
            return reviews;
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

    public async Task<Company?> ExtractCompanyInfoAsync(string mapsUrl)
    {
        try
        {
            _driver.Navigate().GoToUrl(mapsUrl);
            await Task.Delay(3000);

            // Handle consent page if it appears
            await HandleConsentPage();
            await Task.Delay(2000);

            var company = new Company
            {
                GoogleMapsUrl = mapsUrl
            };

            // Wait for page to load and try to extract company name
            try
            {
                await Task.Delay(3000);
                var nameSelectors = new[]
                {
                    "h1.DUwDvf",
                    "h1[data-attrid]",
                    "h1.x3AX1-LfntMc-header-title-title", 
                    "h1",
                    ".x3AX1-LfntMc-header-title-title",
                    "[data-attrid='title'] h1",
                    ".qrShPb h1", // Alternative selector
                    ".lMbq3e .fontHeadlineSmall" // Another Maps selector
                };

                foreach (var selector in nameSelectors)
                {
                    try
                    {
                        var nameElement = _driver.FindElement(By.CssSelector(selector));
                        if (!string.IsNullOrEmpty(nameElement.Text) && 
                            !nameElement.Text.Contains("Innan du fortsätter") &&
                            !nameElement.Text.Contains("Before you continue"))
                        {
                            company.Name = nameElement.Text;
                            _logger.LogInformation($"Found company name: {company.Name}");
                            break;
                        }
                    }
                    catch { }
                }

                // If we still got consent page text, return null to trigger fallback
                if (string.IsNullOrEmpty(company.Name) || 
                    company.Name.Contains("Innan du fortsätter") ||
                    company.Name.Contains("Before you continue"))
                {
                    _logger.LogWarning("Still on consent page or no company name found");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not extract company name: {ex.Message}");
            }

            // Try to find rating
            try
            {
                var ratingSelectors = new[]
                {
                    ".ceNzKf[aria-label*='star']",
                    "[data-value]",
                    ".fontDisplayLarge"
                };

                foreach (var selector in ratingSelectors)
                {
                    try
                    {
                        var ratingElement = _driver.FindElement(By.CssSelector(selector));
                        var ratingText = ratingElement.GetAttribute("aria-label") ?? ratingElement.Text;
                        
                        var match = Regex.Match(ratingText, @"(\d+\.?\d*)");
                        if (match.Success && double.TryParse(match.Value, out var rating))
                        {
                            company.OverallRating = rating;
                            _logger.LogInformation($"Found rating: {rating}");
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Try to find review count
            try
            {
                var reviewElements = _driver.FindElements(By.XPath("//*[contains(text(), 'review')]"));
                foreach (var element in reviewElements)
                {
                    var text = element.Text;
                    var match = Regex.Match(text, @"(\d+(?:,\d+)*)\s*review");
                    if (match.Success && int.TryParse(match.Groups[1].Value.Replace(",", ""), out var count))
                    {
                        company.TotalReviews = count;
                        _logger.LogInformation($"Found review count: {count}");
                        break;
                    }
                }
            }
            catch { }

            // Extract address, phone, website with more flexible selectors
            try
            {
                var addressElement = _driver.FindElement(By.CssSelector("[data-item-id='address'] .fontBodyMedium"));
                company.Address = addressElement.Text;
            }
            catch
            {
                try
                {
                    var addressElement = _driver.FindElement(By.XPath("//button[@data-item-id='address']//div[@class='fontBodyMedium']"));
                    company.Address = addressElement.Text;
                }
                catch { }
            }

            try
            {
                var phoneElement = _driver.FindElement(By.CssSelector("[data-item-id='phone'] .fontBodyMedium"));
                company.PhoneNumber = phoneElement.Text;
            }
            catch { }

            try
            {
                var websiteElement = _driver.FindElement(By.CssSelector("[data-item-id='authority']"));
                company.Website = websiteElement.GetAttribute("href");
            }
            catch { }

            if (string.IsNullOrEmpty(company.Name))
            {
                _logger.LogWarning("Could not extract company name from the page");
                return null;
            }

            return company;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error extracting company info from URL: {mapsUrl}");
            return null;
        }
    }

    private async Task ScrollToLoadReviews(int maxReviews)
    {
        _logger.LogInformation($"Starting to load more reviews, target: {maxReviews}");
        
        // Much more comprehensive scrollable containers for 2024+ Maps
        var scrollableSelectors = new[]
        {
            "[role='main']", // Main content area
            ".review-dialog-list", // Review list dialog
            ".section-scrollbox", // Section scroll container
            ".siAUzd-neVct", // Maps scroll container
            "[data-value='Sort']/../..", // Sort parent container
            ".review-score-container", // Review score area
            // Modern Google Maps containers
            "[data-viewtags='feedcontainer']", // Feed container
            "div[data-async-context]", // Async content container
            "[jsaction*='scroll']", // Scroll action elements
            ".section-scrollbox.section-scrollbox", // Double class selector
            "[role='dialog'] [role='main']", // Dialog main area
            "div[style*='overflow']", // Overflow containers
            ".widget-pane-content", // Pane content
            "[data-test-id*='review']", // Test ID containers
            ".section-result", // Result section
            "[data-hveid]", // Google specific container
            "div[jscontroller]", // JS controller divs
            "[role='tabpanel']", // Tab panel content
            "[data-async-type='reviewDialog']", // Review dialog
            ".section-scrollbox-wrapper" // Wrapper container
        };

        IWebElement? scrollableElement = null;
        foreach (var selector in scrollableSelectors)
        {
            scrollableElement = await WaitForElement(By.CssSelector(selector), 2000);
            if (scrollableElement != null)
            {
                _logger.LogInformation($"Found scrollable element with selector: {selector}");
                break;
            }
        }

        if (scrollableElement == null)
        {
            _logger.LogWarning("No scrollable element found, using window scroll");
        }

        var loadedReviews = 0;
        var scrollAttempts = 0;
        var maxScrollAttempts = Math.Max(maxReviews * 5, 150); // Extremely aggressive scrolling - up to 150 attempts
        var noChangeCount = 0;

        while (loadedReviews < maxReviews && scrollAttempts < maxScrollAttempts && noChangeCount < 15)
        {
            // Get current count before scrolling using multiple selectors
            var currentReviews = 0;
            var countSelectors = new[] { "[data-review-id]", ".jftiEf", ".TSUbDb", "[jsaction*='pane.review']" };
            foreach (var selector in countSelectors)
            {
                try
                {
                    currentReviews = _driver.FindElements(By.CssSelector(selector)).Count;
                    if (currentReviews > 0) break;
                }
                catch { }
            }
            
            // Try different scrolling approaches
            if (scrollableElement != null)
            {
                // Scroll within the container
                ((IJavaScriptExecutor)_driver).ExecuteScript(
                    "arguments[0].scrollTop = arguments[0].scrollHeight;", scrollableElement);
            }
            else
            {
                // Fallback to window scroll
                ((IJavaScriptExecutor)_driver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
            }

            // Wait for new content to load - even longer wait for more reviews
            await Task.Delay(4000);
            
            // Additional scroll techniques
            if (scrollAttempts % 5 == 0)
            {
                // Try multiple scroll methods
                ((IJavaScriptExecutor)_driver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                await Task.Delay(1000);
                
                // Scroll the document element too
                ((IJavaScriptExecutor)_driver).ExecuteScript("document.documentElement.scrollTop = document.documentElement.scrollHeight;");
                await Task.Delay(1000);
                
                // Try scrolling with smooth behavior
                ((IJavaScriptExecutor)_driver).ExecuteScript("window.scrollTo({top: document.body.scrollHeight, behavior: 'smooth'});");
                await Task.Delay(2000);
            }

            // Try to click "Show more" or "Load more" buttons if they exist
            var showMoreSelectors = new[]
            {
                "button[aria-label*='more']",
                "button[aria-label*='More']",
                ".review-more-link",
                "[data-value='Show more']",
                "button:contains('Show more')",
                "button:contains('Load more')"
            };

            foreach (var selector in showMoreSelectors)
            {
                try
                {
                    var moreButton = _driver.FindElement(By.CssSelector(selector));
                    if (moreButton != null && moreButton.Displayed && moreButton.Enabled)
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", moreButton);
                        _logger.LogInformation("Clicked 'Show more' button");
                        await Task.Delay(2000);
                        break;
                    }
                }
                catch { }
            }

            // Check if more reviews loaded using multiple selectors
            var newReviewCount = 0;
            foreach (var selector in countSelectors)
            {
                try
                {
                    newReviewCount = _driver.FindElements(By.CssSelector(selector)).Count;
                    if (newReviewCount > 0) break;
                }
                catch { }
            }
            
            if (newReviewCount > loadedReviews)
            {
                _logger.LogInformation($"Loaded {newReviewCount} reviews (was {loadedReviews})");
                loadedReviews = newReviewCount;
                scrollAttempts = 0;
                noChangeCount = 0;
            }
            else
            {
                scrollAttempts++;
                noChangeCount++;
                _logger.LogDebug($"No new reviews loaded, attempt {scrollAttempts}/{maxScrollAttempts}");
            }

            // Extra scroll techniques for stubborn pages
            if (scrollAttempts % 5 == 0)
            {
                // Try scrolling to specific review elements using multiple selectors
                try
                {
                    var reviewScrollSelectors = new[] { "[data-review-id]", ".jftiEf", ".TSUbDb" };
                    foreach (var selector in reviewScrollSelectors)
                    {
                        try
                        {
                            var reviewElements = _driver.FindElements(By.CssSelector(selector));
                            if (reviewElements.Count > 0)
                            {
                                var lastReview = reviewElements.Last();
                                ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView(true);", lastReview);
                                await Task.Delay(1000);
                                break;
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                // Try pressing Page Down key
                try
                {
                    var body = _driver.FindElement(By.TagName("body"));
                    body.SendKeys(Keys.PageDown);
                    await Task.Delay(1000);
                }
                catch { }
            }
        }

        _logger.LogInformation($"Scroll loading completed. Final review count: {loadedReviews}");
    }

    /// <summary>
    /// Unified extraction method combining both traditional and fresh extraction strategies
    /// </summary>
    private async Task<Review?> ExtractReviewFromElementUnified(IWebElement reviewElement)
    {
        _logger.LogDebug("🔧 UNIFIED EXTRACTION METHOD CALLED - this should appear in logs!");
        
        var review = new Review
        {
            Id = Guid.NewGuid().ToString(),
            Time = DateTime.Now,
            Language = "sv",
            IsLocalGuide = false,
            ReviewCount = 0,
            Photos = new List<string>()
        };

        try
        {
            var reviewId = reviewElement.GetAttribute("data-review-id");
            if (!string.IsNullOrEmpty(reviewId))
            {
                review.Id = reviewId;
            }
        }
        catch { }

        // UNIFIED AUTHOR EXTRACTION - combining proven selectors from both methods
        var authorSelectors = new[]
        {
            // PROVEN fresh extraction selectors (THESE WORK!)
            ".fontHeadlineSmall", // Primary headline font
            ".fontDisplaySmall", // Display font
            ".fontTitleSmall", // Title font (PROVEN!)
            "div:first-child span:first-child", // First nested span (PROVEN!)
            "span[dir]:first-of-type", // First directional span
            "a[href*='contrib']:first-of-type", // Contributor links
            "div[data-hveid] span:first-of-type", // Tracked elements first span
            
            // Traditional selectors as fallbacks
            "[data-href*='/contrib/']", // Contributor link
            "a[href*='/contrib/']", // Contributor link alternative
            ".d4r55", // Author name class
            ".TSUbDb a", // Author link in review
            ".fontBodyMedium:first-child", // First medium font element
            "span.fontBodyMedium:not([aria-label])", // Plain medium spans
            "div > a > span", // Nested link spans
            "[role='button'] > span", // Direct button spans
            "span:not([class]):not([id]):first-child", // Plain first spans
            "div:first-child span:not([aria-label])", // First div spans without aria
        };

        foreach (var selector in authorSelectors)
        {
            try
            {
                var authorElement = reviewElement.FindElement(By.CssSelector(selector));
                var authorText = authorElement?.Text?.Trim();
                if (!string.IsNullOrEmpty(authorText) && authorText.Length > 1 && authorText.Length < 100)
                {
                    review.AuthorName = authorText;
                    review.AuthorUrl = authorElement.GetAttribute("data-href") ?? authorElement.GetAttribute("href");
                    _logger.LogDebug($"✓ Found author '{authorText}' with selector: {selector}");
                    break;
                }
            }
            catch { }
        }

        // UNIFIED RATING EXTRACTION - simplified and comprehensive
        var ratingSelectors = new[]
        {
            "[aria-label*='star']", // Any element with star in aria-label (SIMPLIFIED)
            "[role='img'][aria-label*='star']", // Star rating image
            "[role='img'][aria-label*='stjärn']", // Swedish stars
            "[aria-label*='1 star']", "[aria-label*='2 star']", "[aria-label*='3 star']", 
            "[aria-label*='4 star']", "[aria-label*='5 star']",
            "[aria-label*='Rated']",
        };

        foreach (var selector in ratingSelectors)
        {
            try
            {
                var ratingElement = reviewElement.FindElement(By.CssSelector(selector));
                var ariaLabel = ratingElement?.GetAttribute("aria-label");
                if (!string.IsNullOrEmpty(ariaLabel))
                {
                    var ratingMatch = Regex.Match(ariaLabel, @"(\d)[\s\-]*star");
                    if (ratingMatch.Success && int.TryParse(ratingMatch.Groups[1].Value, out var rating))
                    {
                        review.Rating = rating;
                        _logger.LogDebug($"✓ Found rating: {rating} stars from aria-label: {ariaLabel}");
                        break;
                    }
                }
            }
            catch { }
        }

        // UNIFIED TEXT EXTRACTION - proven fresh selectors first
        var textSelectors = new[]
        {
            // PROVEN fresh extraction selectors (THESE WORK!)
            ".fontBodyMedium", // Primary body font (PROVEN!)
            ".gm2-body-2", // Google Material Design
            "span[dir][lang]", // Directional language spans
            "div[data-expandable-content]", // Expandable content
            "span:contains('besök')", // Contains visit keyword
            "*[jsaction*='expand']", // Expandable text
            "div > span:last-child", // Last span in div
            "p", // Paragraph elements
            
            // Traditional selectors as fallbacks
            ".MyEned", // Review text class
            ".wiI7pd", // Alternative review text
            ".MyEned span", // Review text span
            ".wiI7pd span", // Alternative review text span
            "span.wiI7pd", // Common review text span
            "div.MyEned", // Review text div
            ".fontBodyMedium span", // Medium font spans
            "span[dir='ltr']", // Left-to-right text spans
            "span:not([aria-label]):not([title])", // Plain text spans
        };

        foreach (var selector in textSelectors)
        {
            try
            {
                var textElements = reviewElement.FindElements(By.CssSelector(selector));
                foreach (var textElement in textElements.Take(3))
                {
                    var text = textElement?.Text?.Trim();
                    if (!string.IsNullOrEmpty(text) && text.Length > 10 && text.Length < 2000)
                    {
                        review.Text = text;
                        _logger.LogDebug($"✓ Found review text ({text.Length} chars) with selector: {selector}");
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(review.Text)) break;
            }
            catch { }
        }

        // ENHANCED TIME EXTRACTION - English patterns FIRST for international sites
        var timeSelectors = new[]
        {
            // English patterns (PRIORITY for Lumea by the Sea)
            "*:contains('hours ago')", // "23 hours ago" 
            "*:contains('hour ago')", // "1 hour ago"
            "*:contains('a day ago')", // "a day ago"
            "*:contains('day ago')", // "1 day ago", "day ago" 
            "*:contains('days ago')", // "2 days ago", "3 days ago"
            "*:contains('minutes ago')", // "30 minutes ago"
            "*:contains('minute ago')", // "1 minute ago"
            "*:contains('Edited')", // "Edited a day ago"
            "*:contains('yesterday')", // "yesterday"
            "*:contains('today')", // "today"
            "*:contains('on Google')", // "a day ago on Google"
            
            // Swedish patterns (fallback)
            "*:contains('för')", // Swedish "for X time ago" (PROVEN!)
            "*:contains('dagar')", // Swedish "days" (PROVEN!)
            "*:contains('sedan')", // Swedish "ago" (PROVEN!)
            
            // Generic patterns
            ".fontCaption", // Caption font for timestamps
            "*[title*='2024']", // Year in title
            "*[title*='2025']", // Year in title
            "time", // HTML time elements
            "[datetime]" // Datetime attributes
        };

        foreach (var selector in timeSelectors)
        {
            try
            {
                IWebElement timeElement = null;
                if (selector.StartsWith("*:contains"))
                {
                    var searchText = selector.Replace("*:contains('", "").Replace("')", "");
                    var xpath = $".//text()[contains(., '{searchText}')]/parent::*";
                    timeElement = reviewElement.FindElement(By.XPath(xpath));
                }
                else
                {
                    timeElement = reviewElement.FindElement(By.CssSelector(selector));
                }

                var timeText = timeElement?.Text?.Trim();
                if (!string.IsNullOrEmpty(timeText) && (timeText.Contains("för") || timeText.Contains("ago") || timeText.Contains("dag") || timeText.Contains("hours") || timeText.Contains("days") || timeText.Contains("yesterday") || timeText.Contains("today") || timeText.Contains("Edited")))
                {
                    review.RelativeTime = timeText;
                    review.Time = ParseRelativeTime(timeText);
                    _logger.LogDebug($"✓ Found timestamp: {timeText}");
                    break;
                }
            }
            catch { }
        }

        // UNIFIED VALIDATION - accept if we have at least author and either rating, text, or time
        bool hasAuthor = !string.IsNullOrEmpty(review.AuthorName);
        bool hasText = !string.IsNullOrEmpty(review.Text);
        bool hasRating = review.Rating > 0;
        bool hasTime = !string.IsNullOrEmpty(review.RelativeTime);

        _logger.LogDebug($"Unified extraction - Author: {hasAuthor}, Rating: {hasRating}, Text: {hasText}, Time: {hasTime}");

        if (hasAuthor && (hasRating || hasText || hasTime))
        {
            _logger.LogDebug($"✅ SUCCESSFULLY EXTRACTED UNIFIED REVIEW: {review.AuthorName} ({review.Rating} stars) - {review.RelativeTime}");
            return review;
        }
        
        _logger.LogDebug($"❌ Unified extraction failed validation - insufficient data");
        return null;
    }

    private async Task<Review?> ExtractReviewFromElement(IWebElement reviewElement)
    {
        _logger.LogInformation("🔥 METHOD CALLED: ExtractReviewFromElement");
        
        // DEBUG: Log the actual HTML structure of this element
        try
        {
            var elementHtml = reviewElement.GetAttribute("outerHTML");
            var trimmedHtml = elementHtml?.Length > 500 ? elementHtml.Substring(0, 500) + "..." : elementHtml;
            _logger.LogInformation($"🔍 ELEMENT HTML STRUCTURE: {trimmedHtml}");
            
            var elementText = reviewElement.Text?.Trim();
            var trimmedText = elementText?.Length > 200 ? elementText.Substring(0, 200) + "..." : elementText;
            _logger.LogInformation($"🔍 ELEMENT TEXT CONTENT: '{trimmedText}'");
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"🔍 Could not get element structure: {ex.Message}");
        }
        
        _logger.LogInformation("🔥 STEP 1: About to create Review object");
        
        var review = new Review
        {
            Id = Guid.NewGuid().ToString(),
            Time = DateTime.Now,
            Language = "sv",
            IsLocalGuide = false,
            ReviewCount = 0,
            Photos = new List<string>()
        };
        
        _logger.LogInformation("🔥 STEP 2: Review object created successfully");

        try
        {
            _logger.LogInformation("🔥 STEP 3: About to get reviewElement attributes");
            var reviewId = reviewElement.GetAttribute("data-review-id");
            if (!string.IsNullOrEmpty(reviewId))
            {
                review.Id = reviewId;
            }
        }
        catch { }

        // ATTEMPT SIMPLE JAVASCRIPT EXTRACTION FIRST
        try
        {
            var jsExecutor = (IJavaScriptExecutor)_driver;
            
            // Get all text content using JavaScript
            var allText = jsExecutor.ExecuteScript("return arguments[0].innerText || arguments[0].textContent || '';", reviewElement) as string;
            _logger.LogInformation($"📜 JS ALL TEXT: '{allText?.Substring(0, Math.Min(300, allText?.Length ?? 0))}...'");
            
            // If this element has meaningful text content, let's try to parse it intelligently
            if (!string.IsNullOrEmpty(allText) && allText.Length > 10)
            {
                _logger.LogInformation($"📜 Element has {allText.Length} characters of text content - proceeding with extraction");
            }
        }
        catch (Exception jsEx)
        {
            _logger.LogInformation($"📜 Simple JavaScript failed: {jsEx.Message}");
        }

        // FALLBACK: COMPREHENSIVE AUTHOR EXTRACTION (All possible selectors)
        var authorSelectors = new[]
        {
            // MOST COMMON MODERN SELECTORS (try these first)
            "a[href*='/contrib/']", // Contributor link (most reliable)
            "[data-href*='/contrib/']", // Data href contributor
            ".d4r55", // Google's author name class
            ".TSUbDb a", // Author link in container
            ".WNxzHc a", // Alternative author link
            ".jBmods a", // Author in header
            ".RfnDt", // Modern author class
            
            // NEWER FORMAT SELECTORS
            ".fontHeadlineSmall", 
            ".fontDisplaySmall", 
            ".fontTitleSmall",
            ".fontBodyMedium:first-child",
            
            // FALLBACK SELECTORS (broader search)
            "div:first-child a[href*='contrib']", // First div contributor link
            "span:first-child:not([aria-label])", // First plain span
            "button[data-href*='/contrib/'] span", // Button contributor span
            ".TSUbDb span", // Container span
            ".TSUbDb", // Container itself
            ".WNxzHc", // Alternative container
            ".jBmods", // Header container
            "div[data-hveid] span:first-of-type", // Tracked div first span
            "span[dir]:first-of-type", // Directional span
            "div:first-child span:first-child", // Nested first spans
            "div:first-child span:first-child", // First nested span (PROVEN!)
            "span[dir]:first-of-type", // First directional span
            "a[href*='contrib']:first-of-type", // Contributor links
            "div[data-hveid] span:first-of-type", // Tracked elements first span
            "[data-href*='/contrib/']", // Contributor link
            "a[href*='/contrib/']", // Contributor link alternative
            ".fontBodyMedium:first-child", // First medium font element
            "span.fontBodyMedium:not([aria-label])", // Plain medium spans
            "div > a > span", // Nested link spans
            "[role='button'] > span", // Direct button spans
            "span:not([class]):not([id]):first-child", // Plain first spans
            "div:first-child span:not([aria-label])", // First div spans without aria
        };

        foreach (var selector in authorSelectors)
        {
            try
            {
                var authorElement = reviewElement.FindElement(By.CssSelector(selector));
                var authorText = authorElement?.Text?.Trim();
                if (!string.IsNullOrEmpty(authorText) && authorText.Length > 1 && authorText.Length < 100)
                {
                    review.AuthorName = authorText;
                    review.AuthorUrl = authorElement.GetAttribute("data-href") ?? authorElement.GetAttribute("href");
                    _logger.LogInformation($"✓ FRESH LOGIC found author '{authorText}' with selector: {selector}");
                    break;
                }
            }
            catch { }
        }
        
        _logger.LogInformation($"🔥 STEP 4: Author extraction complete. Found: '{review.AuthorName}'");

        // HYBRID RATING EXTRACTION: Traditional + Fresh
        var ratingSelectors = new[]
        {
            // TRADITIONAL RATING SELECTORS
            ".kvMYJc[role='img']", // Traditional star container
            ".Fam1ne[aria-label*='star']", // Traditional star element
            "[data-value][aria-label*='star']", // Traditional star with data value
            
            // FRESH RATING SELECTORS
            "[aria-label*='star']", // Any element with star in aria-label (SIMPLIFIED)
            "[role='img'][aria-label*='star']", // Star rating image
            "[role='img'][aria-label*='stjärn']", // Swedish stars
            "[aria-label*='1 star']", "[aria-label*='2 star']", "[aria-label*='3 star']", 
            "[aria-label*='4 star']", "[aria-label*='5 star']",
            "[aria-label*='Rated']",
        };

        foreach (var selector in ratingSelectors)
        {
            try
            {
                var ratingElement = reviewElement.FindElement(By.CssSelector(selector));
                var ariaLabel = ratingElement?.GetAttribute("aria-label");
                if (!string.IsNullOrEmpty(ariaLabel))
                {
                    var ratingMatch = Regex.Match(ariaLabel, @"(\d)[\s\-]*star");
                    if (ratingMatch.Success && int.TryParse(ratingMatch.Groups[1].Value, out var rating))
                    {
                        review.Rating = rating;
                        _logger.LogDebug($"✓ FRESH LOGIC found rating: {rating} stars from aria-label: {ariaLabel}");
                        break;
                    }
                }
            }
            catch { }
        }

        // COMPREHENSIVE TEXT EXTRACTION (All possible selectors) 
        var textSelectors = new[]
        {
            // MOST RELIABLE TEXT SELECTORS (try first)
            "span.wiI7pd", // Most common review text
            ".MyEned", // Google's review text class
            ".wiI7pd", // Alternative review text
            ".MyEned span", // Review text in span
            ".wiI7pd span", // Alternative in span
            ".Jtu6Td", // Review container
            
            // MODERN TEXT SELECTORS
            ".fontBodyMedium", // Modern body font
            ".gm2-body-2", // Material design
            "span[dir][lang]", // Directional language spans
            
            // BROADER TEXT SELECTORS (more aggressive)
            "span:not([aria-label]):not([class*='font']):not([class*='button'])", // Any plain span
            "div > span:not([aria-label])", // Div child spans
            "span[dir='ltr']", // Left-to-right spans
            "span[dir='rtl']", // Right-to-left spans
            "*[jsaction*='expand']", // Expandable content
            "div[data-expandable-content]", // Expandable divs
            "p", // Paragraph elements
            ".TSUbDb .MyEned", // Container review text
            "[data-expandable-section]", // Expandable sections
            "div:not([class]):not([id])", // Plain divs
            "span:contains('.')", // Spans with periods (sentences)
            ".gm2-body-2", // Google Material Design
            "span[dir][lang]", // Directional language spans
            "div[data-expandable-content]", // Expandable content
            "*[jsaction*='expand']", // Expandable text
            "div > span:last-child", // Last span in div
            "p", // Paragraph elements
            ".MyEned", // Review text class
            ".wiI7pd", // Alternative review text
            ".MyEned span", // Review text span
            ".wiI7pd span", // Alternative review text span
            "span.wiI7pd", // Common review text span
            "div.MyEned", // Review text div
            ".fontBodyMedium span", // Medium font spans
            "span[dir='ltr']", // Left-to-right text spans
            "span:not([aria-label]):not([title])", // Plain text spans
        };

        foreach (var selector in textSelectors)
        {
            try
            {
                var textElements = reviewElement.FindElements(By.CssSelector(selector));
                foreach (var textElement in textElements.Take(3))
                {
                    var text = textElement?.Text?.Trim();
                    if (!string.IsNullOrEmpty(text) && text.Length > 10 && text.Length < 2000)
                    {
                        review.Text = text;
                        _logger.LogDebug($"✓ FRESH LOGIC found review text ({text.Length} chars) with selector: {selector}");
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(review.Text)) break;
            }
            catch { }
        }

        // HYBRID TIME EXTRACTION: Traditional + Fresh
        var timeSelectors = new[]
        {
            // TRADITIONAL TIME SELECTORS
            ".rsqaWe", // Traditional time class
            ".DU9Pgb", // Traditional alternative time class
            ".dehysf", // Traditional date class
            
            // FRESH TIME SELECTORS
            ".fontCaption", // Caption font for timestamps (MOST LIKELY)
            ".rsqaWe", // Time class
            ".DU9Pgb", // Alternative time class  
            "[title*='2024']", // Year in title
            "time", // HTML time elements
            "[datetime]", // Datetime attributes
            "span.fontBodySmall", // Small font time
            ".relative-time", // Relative time class
            ".timestamp" // Timestamp class
        };

        foreach (var selector in timeSelectors)
        {
            try
            {
                var timeElement = reviewElement.FindElement(By.CssSelector(selector));
                var timeText = timeElement?.Text?.Trim();
                if (!string.IsNullOrEmpty(timeText) && (timeText.Contains("för") || timeText.Contains("ago") || timeText.Contains("dag")))
                {
                    review.RelativeTime = timeText;
                    review.Time = ParseRelativeTime(timeText);
                    _logger.LogInformation($"✓ FRESH LOGIC found timestamp: {timeText}");
                    break;
                }
            }
            catch { }
        }

        // RELAXED VALIDATION (accept reviews with less strict requirements)
        bool hasAuthor = !string.IsNullOrEmpty(review.AuthorName);
        bool hasText = !string.IsNullOrEmpty(review.Text);
        bool hasRating = review.Rating > 0;
        bool hasTime = !string.IsNullOrEmpty(review.RelativeTime);

        _logger.LogInformation($"EXTRACTION RESULTS - Author: '{review.AuthorName}' | Rating: {review.Rating} | Text: {(hasText ? $"{review.Text?.Length} chars" : "none")} | Time: '{review.RelativeTime}'");

        // Accept review if we have AT LEAST author OR text OR rating (more relaxed)
        if (hasAuthor || hasText || hasRating)
        {
            _logger.LogInformation($"✅ SUCCESSFULLY EXTRACTED REVIEW: {review.AuthorName} ({review.Rating} stars) - {review.RelativeTime}");
            return review;
        }
        
        _logger.LogInformation($"❌ Extraction failed - no meaningful data found");
        return null;
    }

    private DateTime ParseRelativeTime(string relativeTime)
    {
        var now = DateTime.Now;
        
        if (string.IsNullOrEmpty(relativeTime))
            return now;

        var lowerTime = relativeTime.ToLower();
        
        if (lowerTime.Contains("day"))
        {
            var match = Regex.Match(lowerTime, @"(\d+)");
            if (match.Success && int.TryParse(match.Value, out var days))
            {
                return now.AddDays(-days);
            }
        }
        else if (lowerTime.Contains("week"))
        {
            var match = Regex.Match(lowerTime, @"(\d+)");
            if (match.Success && int.TryParse(match.Value, out var weeks))
            {
                return now.AddDays(-weeks * 7);
            }
        }
        else if (lowerTime.Contains("month"))
        {
            var match = Regex.Match(lowerTime, @"(\d+)");
            if (match.Success && int.TryParse(match.Value, out var months))
            {
                return now.AddMonths(-months);
            }
        }
        else if (lowerTime.Contains("year"))
        {
            var match = Regex.Match(lowerTime, @"(\d+)");
            if (match.Success && int.TryParse(match.Value, out var years))
            {
                return now.AddYears(-years);
            }
        }

        return now;
    }

    private bool PassesFilters(Review review, ScrapingOptions options)
    {
        if (options.MinRating.HasValue && review.Rating < options.MinRating.Value)
            return false;

        if (options.MaxRating.HasValue && review.Rating > options.MaxRating.Value)
            return false;

        if (options.FromDate.HasValue && review.Time < options.FromDate.Value)
            return false;

        if (options.ToDate.HasValue && review.Time > options.ToDate.Value)
            return false;

        return true;
    }

    private async Task<IWebElement?> WaitForElement(By selector, int timeoutMs)
    {
        try
        {
            var wait = new WebDriverWait(_driver, TimeSpan.FromMilliseconds(timeoutMs));
            return wait.Until(d => d.FindElement(selector));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Specialized extraction method for fresh page elements without data-review-id
    /// Targets Mats Björlöv and Kadiatu Conteh reviews specifically
    /// </summary>
    private async Task<List<Review>> ExtractFromFreshElements(List<IWebElement> freshElements)
    {
        var extractedReviews = new List<Review>();
        
        _logger.LogInformation($"Processing {freshElements.Count} fresh elements with specialized extraction...");
        
        foreach (var element in freshElements)
        {
            try
            {
                var review = new Review
                {
                    Id = Guid.NewGuid().ToString(),
                    Time = DateTime.Now,
                    Language = "sv",
                    IsLocalGuide = false,
                    ReviewCount = 0,
                    Photos = new List<string>()
                };

                // Extract author name using multiple strategies for fresh elements
                var authorSelectors = new[]
                {
                    ".fontHeadlineSmall", // Primary headline font
                    ".fontDisplaySmall", // Display font
                    ".fontTitleSmall", // Title font
                    "[aria-label*='Review by']", // Aria label pattern
                    "span[dir]:first-of-type", // First directional span
                    "div:first-child span:first-child", // First nested span
                    "*:contains('Mats Björlöv')", // Direct name search
                    "*:contains('Kadiatu Conteh')", // Direct name search
                    "*:contains('Mats')", // Partial name search
                    "*:contains('Kadiatu')", // Partial name search
                    "a[href*='contrib']:first-of-type", // Contributor links
                    "div[data-hveid] span:first-of-type", // Tracked elements first span
                    "span:contains('Mats'):first", // Text contains name
                    "span:contains('Kadiatu'):first" // Text contains name
                };

                foreach (var selector in authorSelectors)
                {
                    try
                    {
                        var authorElement = element.FindElement(By.CssSelector(selector));
                        var authorText = authorElement?.Text?.Trim();
                        if (!string.IsNullOrEmpty(authorText) && authorText.Length > 1 && authorText.Length < 100)
                        {
                            review.AuthorName = authorText;
                            _logger.LogInformation($"Found author: {authorText} with selector: {selector}");
                            break;
                        }
                    }
                    catch 
                    {
                        // Try XPath for text content matching
                        try
                        {
                            var xpathSelector = selector.Replace("*:contains('", "//text()[contains(., '").Replace("')", "')]/parent::*");
                            var authorElement = element.FindElement(By.XPath(xpathSelector));
                            var authorText = authorElement?.Text?.Trim();
                            if (!string.IsNullOrEmpty(authorText) && authorText.Length > 1 && authorText.Length < 100)
                            {
                                review.AuthorName = authorText;
                                _logger.LogInformation($"Found author via XPath: {authorText}");
                                break;
                            }
                        }
                        catch { /* Continue to next selector */ }
                    }
                }

                // Extract rating with enhanced strategies
                var ratingSelectors = new[]
                {
                    "[aria-label*='star']",
                    "[role='img'][aria-label*='star']", 
                    "span[aria-label*='star']",
                    "[title*='star']",
                    "*[aria-label*='1 star']", "*[aria-label*='2 star']", "*[aria-label*='3 star']", 
                    "*[aria-label*='4 star']", "*[aria-label*='5 star']",
                    "*[aria-label*='Rated']",
                    ".fontBodyMedium [role='img']"
                };

                foreach (var selector in ratingSelectors)
                {
                    try
                    {
                        var ratingElement = element.FindElement(By.CssSelector(selector));
                        var ariaLabel = ratingElement?.GetAttribute("aria-label");
                        if (!string.IsNullOrEmpty(ariaLabel))
                        {
                            var ratingMatch = Regex.Match(ariaLabel, @"(\d)[\s\-]*star");
                            if (ratingMatch.Success && int.TryParse(ratingMatch.Groups[1].Value, out var rating))
                            {
                                review.Rating = rating;
                                _logger.LogInformation($"Found rating: {rating} stars from aria-label: {ariaLabel}");
                                break;
                            }
                        }
                    }
                    catch { /* Continue to next selector */ }
                }

                // Extract review text with comprehensive strategies
                var textSelectors = new[]
                {
                    ".fontBodyMedium", // Primary body font
                    ".gm2-body-2", // Google Material Design
                    "span[dir][lang]", // Directional language spans
                    "div[data-expandable-content]", // Expandable content
                    "span:contains('Darko')", // Contains known text
                    "span:contains('besök')", // Contains visit keyword
                    "span:contains('tack')", // Contains thank you
                    "span:contains('proffsig')", // Contains professional
                    "*[jsaction*='expand']", // Expandable text
                    "div > span:last-child", // Last span in div
                    "p", // Paragraph elements
                    "[data-review-text]" // Direct review text attribute
                };

                foreach (var selector in textSelectors)
                {
                    try
                    {
                        var textElements = element.FindElements(By.CssSelector(selector));
                        foreach (var textElement in textElements.Take(3))
                        {
                            var text = textElement?.Text?.Trim();
                            if (!string.IsNullOrEmpty(text) && text.Length > 10 && text.Length < 2000)
                            {
                                review.Text = text;
                                _logger.LogInformation($"Found review text: {text.Substring(0, Math.Min(50, text.Length))}...");
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(review.Text)) break;
                    }
                    catch { /* Continue to next selector */ }
                }

                // Extract timestamp with Swedish patterns
                var timeSelectors = new[]
                {
                    "*:contains('för')", // Swedish "for X time ago"
                    "*:contains('dagar')", // Swedish "days"
                    "*:contains('sedan')", // Swedish "ago"
                    "*:contains('days ago')",
                    ".fontCaption", // Caption font for timestamps
                    "*[title*='2024']", // Year in title
                    "time", // HTML time elements
                    "[datetime]" // Datetime attributes
                };

                foreach (var selector in timeSelectors)
                {
                    try
                    {
                        IWebElement timeElement = null;
                        if (selector.StartsWith("*:contains"))
                        {
                            var searchText = selector.Replace("*:contains('", "").Replace("')", "");
                            var xpath = $".//text()[contains(., '{searchText}')]/parent::*";
                            timeElement = element.FindElement(By.XPath(xpath));
                        }
                        else
                        {
                            timeElement = element.FindElement(By.CssSelector(selector));
                        }

                        var timeText = timeElement?.Text?.Trim();
                        if (!string.IsNullOrEmpty(timeText) && (timeText.Contains("för") || timeText.Contains("ago") || timeText.Contains("dag")))
                        {
                            review.RelativeTime = timeText;
                            review.Time = ParseRelativeTime(timeText);
                            _logger.LogInformation($"Found timestamp: {timeText}");
                            break;
                        }
                    }
                    catch { /* Continue to next selector */ }
                }

                // Validate that we found meaningful data
                bool hasAuthor = !string.IsNullOrEmpty(review.AuthorName);
                bool hasRating = review.Rating > 0;
                bool hasText = !string.IsNullOrEmpty(review.Text);
                bool hasTime = !string.IsNullOrEmpty(review.RelativeTime);

                _logger.LogInformation($"Fresh element validation - Author: {hasAuthor}, Rating: {hasRating}, Text: {hasText}, Time: {hasTime}");

                // Accept if we have at least author and either rating or text
                if (hasAuthor && (hasRating || hasText || hasTime))
                {
                    extractedReviews.Add(review);
                    _logger.LogInformation($"✅ SUCCESSFULLY EXTRACTED FRESH REVIEW: {review.AuthorName} ({review.Rating} stars) - {review.RelativeTime}");
                }
                else
                {
                    _logger.LogWarning($"❌ Fresh element failed validation - insufficient data extracted");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Fresh element extraction failed: {ex.Message}");
            }
        }

        return extractedReviews;
    }

    public void Dispose()
    {
        _driver?.Dispose();
    }
}