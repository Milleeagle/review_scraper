using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Collections.Concurrent;

namespace GooglePlacesScraper.Services;

public class ChromeDriverPool : IChromeDriverPool, IDisposable
{
    private readonly ConcurrentBag<IWebDriver> _availableDrivers = new();
    private readonly ConcurrentDictionary<IWebDriver, DateTime> _driverLastUsed = new();
    private readonly ILogger<ChromeDriverPool> _logger;
    private readonly int _maxPoolSize;
    private readonly TimeSpan _maxDriverAge;
    private int _totalDriversCreated = 0;
    private bool _disposed = false;

    public ChromeDriverPool(ILogger<ChromeDriverPool> logger, IConfiguration configuration)
    {
        _logger = logger;
        _maxPoolSize = configuration.GetValue<int>("ChromePool:MaxSize", 10);
        _maxDriverAge = TimeSpan.FromMinutes(configuration.GetValue<int>("ChromePool:MaxAgeMinutes", 30));

        _logger.LogInformation("Chrome driver pool initialized with max size: {MaxSize}, max age: {MaxAge} minutes",
            _maxPoolSize, _maxDriverAge.TotalMinutes);
    }

    public IWebDriver RentDriver()
    {
        // Try to get an available driver from the pool
        while (_availableDrivers.TryTake(out var driver))
        {
            // Check if driver is still valid and not too old
            if (_driverLastUsed.TryGetValue(driver, out var lastUsed))
            {
                var age = DateTime.Now - lastUsed;

                if (age > _maxDriverAge)
                {
                    _logger.LogInformation("Disposing old driver (age: {Age} minutes)", age.TotalMinutes);
                    DisposeDriverInternal(driver);
                    continue;
                }

                // Verify driver is still responsive
                try
                {
                    _ = driver.CurrentWindowHandle; // Quick health check
                    _logger.LogDebug("Reusing existing Chrome driver from pool");
                    return driver;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Driver from pool is unresponsive, disposing");
                    DisposeDriverInternal(driver);
                }
            }
        }

        // No available drivers, create a new one
        return CreateNewDriver();
    }

    public void ReturnDriver(IWebDriver driver)
    {
        if (driver == null)
            return;

        try
        {
            // Clear any lingering data/sessions
            driver.Manage().Cookies.DeleteAllCookies();

            // Update last used time
            _driverLastUsed[driver] = DateTime.Now;

            // Return to pool if under max size
            if (_availableDrivers.Count < _maxPoolSize)
            {
                _availableDrivers.Add(driver);
                _logger.LogDebug("Returned Chrome driver to pool (pool size: {Size})", _availableDrivers.Count);
            }
            else
            {
                _logger.LogDebug("Pool full, disposing driver");
                DisposeDriverInternal(driver);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error returning driver to pool, disposing");
            DisposeDriverInternal(driver);
        }
    }

    public void DisposeDriver(IWebDriver driver)
    {
        DisposeDriverInternal(driver);
    }

    private IWebDriver CreateNewDriver()
    {
        var options = new ChromeOptions();
        options.AddArguments(
            "--headless",
            "--no-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--window-size=1920,1080",
            "--disable-blink-features=AutomationControlled",
            "--lang=en-US",
            "--disable-extensions",
            "--disable-plugins"
        );

        var driver = new ChromeDriver(options);
        driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(15);

        _driverLastUsed[driver] = DateTime.Now;
        _totalDriversCreated++;

        _logger.LogInformation("Created new Chrome driver (total created: {Total})", _totalDriversCreated);

        return driver;
    }

    private void DisposeDriverInternal(IWebDriver driver)
    {
        try
        {
            _driverLastUsed.TryRemove(driver, out _);
            driver.Quit();
            driver.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing Chrome driver");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing Chrome driver pool (total drivers created: {Total})", _totalDriversCreated);

        // Dispose all drivers in the pool
        while (_availableDrivers.TryTake(out var driver))
        {
            DisposeDriverInternal(driver);
        }

        _disposed = true;
    }
}
