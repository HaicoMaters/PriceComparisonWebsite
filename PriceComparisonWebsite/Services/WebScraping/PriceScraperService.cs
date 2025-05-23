using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Threading.Tasks;
using PriceComparisonWebsite.Models;
using PriceComparisonWebsite.Services.HttpClients;
using PriceComparisonWebsite.Services.Implementations;
using PriceComparisonWebsite.Services.Interfaces;
using PriceComparisonWebsite.Services.Utilities;
using PriceComparisonWebsite.Services.Utilities.Interfaces;
using PriceComparisonWebsite.Services.WebScraping.Interfaces;

namespace PriceComparisonWebsite.Services.WebScraping
{
    /// <inheritdoc />
    public class PriceScraperService : IPriceScraperService 
    {

        private readonly IRobotsTxtChecker _robotsTxtChecker;
        private readonly IVendorService _vendorService;
        private readonly IPriceListingService _priceListingService;
        private readonly IPriceParserFactory _priceParserFactory;
        private readonly ILogger<PriceScraperService> _logger;
        private readonly IScraperRateLimiter _rateLimiter;
        private readonly IScraperHttpClient _scraperHttpClient;
        private readonly IScraperStatusService _scraperStatusService;
        private readonly IScraperLogService _scraperLogService;
        private readonly IRetryHandler _retryHandler;


        public PriceScraperService(
            IRobotsTxtChecker robotsTxtChecker,
            IVendorService vendorService,
            IPriceListingService priceListingService,
            ILogger<PriceScraperService> logger,
            IScraperRateLimiter rateLimiter,
            IScraperHttpClient scraperHttpClient,
            IPriceParserFactory priceParserFactory,
            IScraperStatusService scraperStatusService,
            IScraperLogService scraperLogService,
            IRetryHandler retryHandler
        )
        {
            _robotsTxtChecker = robotsTxtChecker;
            _logger = logger;
            _vendorService = vendorService;
            _priceListingService = priceListingService;
            _rateLimiter = rateLimiter;
            _scraperHttpClient = scraperHttpClient;
            _priceParserFactory = priceParserFactory;
            _scraperStatusService = scraperStatusService;
            _scraperLogService = scraperLogService;
            _retryHandler = retryHandler;
        }

        /// <inheritdoc />
        public async Task UpdateAllListings()
        {
            await _scraperLogService.SendLogAsync("Starting update of all listings...");

            var vendorIds = await GetVendorIdsThatSupportScraping();
            await _scraperLogService.SendLogAsync($"Found {vendorIds.Count} vendors that support scraping");

            _logger.LogInformation($"Got Vendors {vendorIds.Count} : Total");

            Dictionary<Uri, int> uris = new Dictionary<Uri, int>(); // Uri attached with price listing id

            // Get all price listings by vendor id and add those with available parsers
            foreach (int vendId in vendorIds)
            {
                var listings = await _priceListingService.GetPriceListingsByVendorId(vendId, new QueryOptions<PriceListing>());
                foreach (var listing in listings)
                {
                    if (Uri.TryCreate(listing.PurchaseUrl, UriKind.Absolute, out var uri))
                    {
                        string domain = uri.Host;

                        if (_priceParserFactory.HasParserForDomain(domain))
                        {
                            uris[uri] = listing.PriceListingId;
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Invalid URL format for listing: {listing.PriceListingId}");
                    }
                }
            }

            // Filter out each URI based on robots.txt
            try
            {
                await FilterUsingRobotsTxt(uris);
                _logger.LogInformation($"Filtered now count is {uris.Count} ");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to filter using robots.txt. Failed to update Listings.");
                return;
            }

            var tasks = new List<Task>();

            foreach (var kvp in uris)
            {
                var uri = kvp.Key;
                var listingId = kvp.Value;
                string domain = uri.Host;

                async Task ScrapeAndUpdate()
                {
                    try
                    {
                        await _scraperLogService.SendLogAsync($"Processing: {uri}");
                        _logger.LogInformation($"Processing {uri}");

                        await _retryHandler.ExecuteWithRetryAsync(async () =>
                        {
                            // Send request using HTTP client
                            var httpResponse = await _scraperHttpClient.SendRequestAsync(uri, HttpMethod.Get);
                            _logger.LogInformation($"Received response for {uri}: {httpResponse.StatusCode}");

                            // Get the correct parser
                            var parser = _priceParserFactory.GetParserForDomain(domain);

                            if (parser != null)
                            {
                                // Parse the content to extract prices
                                var (price, discountedPrice) = await parser.ParsePriceAsync(httpResponse);
                                await _scraperLogService.SendLogAsync($"Found prices for {uri}: Price: {price}, Discounted: {discountedPrice}");

                                // Update the listing with new prices
                                var listing = await _priceListingService.GetPriceListingById(listingId, new QueryOptions<PriceListing>());

                                listing.Price = price;
                                listing.DiscountedPrice = discountedPrice;
                                listing.DateListed = DateTime.Now;

                                await _priceListingService.UpdatePriceListing(listing);
                                _logger.LogInformation($"Updated listing {uri} with Price: {price} and DiscountedPrice: {discountedPrice}");

                                await _priceListingService.UpdateCheapestPrice(listing.ProductId, discountedPrice);
                            }
                            else
                            {
                                await _scraperLogService.SendLogAsync($"No parser found for {domain}");
                                _logger.LogWarning($"No parser found for {domain}");
                            }
                        }, $"Scraping {uri}");
                    }
                    catch (Exception ex)
                    {
                        await _scraperLogService.SendLogAsync($"Error processing {uri}: {ex.Message}");
                        _logger.LogError(ex, $"Failed to scrape or update listing for {uri}");
                    }
                }

                // Enqueue request through the rate limiter (one per domain at a time)
                await _rateLimiter.EnqueueRequest(ScrapeAndUpdate, domain);
            }

            await _rateLimiter.StopProcessing();
            await _scraperStatusService.UpdateLastUpdateTime();
            await _scraperLogService.SendLogAsync("Finished updating all listings.");
            _logger.LogInformation("All listings have been updated.");
        }


        /// <inheritdoc />
        public async Task<List<int>> GetVendorIdsThatSupportScraping()
        {
            var vendors = await _vendorService.GetAllVendorsAsync(new QueryOptions<Vendor>
            {
                Where = v => v.SupportsAutomaticUpdates
            });

            List<int> vendorIds = new List<int>();

            foreach (var vendor in vendors)
            {
                // Create Uri and extract the domain
                if (Uri.TryCreate(vendor.VendorUrl, UriKind.Absolute, out var uri))
                {
                    string domain = uri.Host;  // Extract domain from Uri

                    if (_priceParserFactory.HasParserForDomain(domain))
                    {
                        vendorIds.Add(vendor.VendorId);
                    }
                    else
                    {
                        // Vendor does not actually support it, change the flag
                        vendor.SupportsAutomaticUpdates = false;
                        await _vendorService.UpdateVendorAsync(vendor);
                    }
                }
                else
                {
                    // Log or handle invalid vendor URL
                    _logger.LogWarning($"Invalid URL format for vendor: {vendor.VendorUrl}");
                }
            }

            return vendorIds;
        }

        /// <inheritdoc />
        public async Task FilterUsingRobotsTxt(Dictionary<Uri, int> uris)
        {
            var keysToRemove = new List<Uri>();

            foreach (var kvp in uris)
            {
                if (!await _robotsTxtChecker.CheckRobotsTxt(kvp.Key))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                uris.Remove(key);
            }
        }

        /// <inheritdoc />
        public Task<bool> UpdateListing(int id) // This Can be added when needed
        {
            throw new NotImplementedException();
        }
    }
}