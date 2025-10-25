using OpenQA.Selenium;

namespace GooglePlacesScraper.Services;

public interface IChromeDriverPool
{
    /// <summary>
    /// Get a Chrome driver from the pool. Creates a new one if none available.
    /// </summary>
    IWebDriver RentDriver();

    /// <summary>
    /// Return a Chrome driver to the pool for reuse.
    /// </summary>
    void ReturnDriver(IWebDriver driver);

    /// <summary>
    /// Remove and dispose a driver (e.g., if it's in a bad state).
    /// </summary>
    void DisposeDriver(IWebDriver driver);
}
