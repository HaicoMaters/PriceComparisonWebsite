﻿using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace PriceComparisonWebsite.Models
{
	/// <summary>
	/// Represents a vendor (retailer) in the price comparison system
	/// </summary>
	public class Vendor
	{
		public Vendor()
		{
			Name = string.Empty;
			VendorUrl = string.Empty;
			VendorLogoUrl = string.Empty;
			PriceListings = new List<PriceListing>();
			SupportsAutomaticUpdates = false;
		}
		[Key]
		public int VendorId { get; set; } // PK
		
		[Required(ErrorMessage = "Vendor name is required")]
		[StringLength(30, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 30 characters")]
		public string Name { get; set; }
		
		[Required(ErrorMessage = "Vendor URL is required")]
		[Url(ErrorMessage = "Please enter a valid URL")]
		[Display(Name = "Vendor URL")]
		public string VendorUrl { get; set; }
		
		[Required(ErrorMessage = "Vendor logo URL is required")]
		[Url(ErrorMessage = "Please enter a valid URL")]
		[Display(Name = "Logo URL")]
		public string VendorLogoUrl { get; set; }

		public bool SupportsAutomaticUpdates { get; set; } // Does the vendor support WebScraping
		
		[ValidateNever]
		// Navigation Property for PriceListings
		public ICollection<PriceListing> PriceListings { get; set; } = new List<PriceListing>();  // One Vendor can have many PriceListings
	}
}
