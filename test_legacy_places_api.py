#!/usr/bin/env python3
"""
Google Places API (Legacy) test script for Lumea by the Sea hotel reviews
Uses the legacy Places API with reviews_sort=newest parameter
"""

import requests
import json
from typing import Dict, List, Optional

class GooglePlacesLegacyAPI:
    def __init__(self, api_key: str):
        self.api_key = api_key
        self.base_url = "https://maps.googleapis.com/maps/api/place"
    
    def find_place(self, query: str) -> Optional[str]:
        """Find a place by name and return its place_id using legacy API"""
        url = f"{self.base_url}/findplacefromtext/json"
        params = {
            'input': query,
            'inputtype': 'textquery',
            'fields': 'place_id,name,formatted_address',
            'key': self.api_key
        }
        
        try:
            response = requests.get(url, params=params)
            response.raise_for_status()
            data = response.json()
            
            print(f"Find Place API Response Status: {data.get('status', 'Unknown')}")
            
            if data['status'] == 'OK' and data['candidates']:
                place = data['candidates'][0]
                print(f"Found place: {place.get('name')} - {place.get('formatted_address')}")
                return place['place_id']
            else:
                print(f"No places found. Status: {data['status']}")
                if 'error_message' in data:
                    print(f"Error message: {data['error_message']}")
                return None
                
        except requests.RequestException as e:
            print(f"Error finding place: {e}")
            return None
    
    def get_place_details_with_newest_reviews(self, place_id: str) -> Optional[Dict]:
        """Get detailed information about a place with reviews sorted by newest"""
        url = f"{self.base_url}/details/json"
        params = {
            'place_id': place_id,
            'fields': 'name,formatted_address,rating,user_ratings_total,reviews',
            'reviews_sort': 'newest',  # CRITICAL: Sort reviews by newest first
            'key': self.api_key
        }
        
        print(f"🔥 USING LEGACY API WITH reviews_sort=newest parameter")
        print(f"Request URL: {url}")
        print(f"Parameters: {params}")
        
        try:
            response = requests.get(url, params=params)
            response.raise_for_status()
            data = response.json()
            
            print(f"Place Details API Response Status: {data.get('status', 'Unknown')}")
            
            if data['status'] == 'OK':
                return data['result']
            else:
                print(f"Error getting place details. Status: {data['status']}")
                if 'error_message' in data:
                    print(f"Error message: {data['error_message']}")
                return None
                
        except requests.RequestException as e:
            print(f"Error getting place details: {e}")
            return None
    
    def get_newest_reviews(self, place_name: str) -> List[Dict]:
        """Get newest reviews for a place by name using legacy API"""
        print(f"🔍 Searching for: {place_name}")
        print("=" * 50)
        
        # Find the place
        place_id = self.find_place(place_name)
        if not place_id:
            return []
        
        print(f"✅ Found place_id: {place_id}")
        print("=" * 50)
        
        # Get place details with newest reviews
        place_details = self.get_place_details_with_newest_reviews(place_id)
        if not place_details:
            return []
        
        # Extract and format reviews
        reviews = place_details.get('reviews', [])
        print(f"📝 Found {len(reviews)} reviews from Google Places API (Legacy)")
        print(f"⭐ Overall rating: {place_details.get('rating', 'N/A')}/5")
        print(f"📊 Total ratings: {place_details.get('user_ratings_total', 'N/A')}")
        print("🎯 Reviews should be sorted by NEWEST first")
        
        return reviews

def format_legacy_review(review: Dict, index: int) -> str:
    """Format a single review from the legacy Places API for display"""
    author = review.get('author_name', 'Anonymous')
    rating = review.get('rating', 0)
    text = review.get('text', '')
    time = review.get('relative_time_description', 'Unknown time')
    
    # Get exact timestamp if available
    timestamp = review.get('time', 'No timestamp')
    
    return f"""
📝 Review #{index + 1}: 
👤 Author: {author}
⭐ Rating: {rating}/5 stars
🕐 Relative Time: {time}
📅 Timestamp: {timestamp}
💬 Review: {text}
{"=" * 70}"""

def main():
    # Use the provided API key
    api_key = "AIzaSyAkct7KdAXfhEndXPreVHUFTXxAvqn-TuM"
    
    # Initialize legacy API client
    places_api = GooglePlacesLegacyAPI(api_key)
    
    # Test with multiple search variations for Lumea by the Sea hotel
    search_terms = [
        "Lumea by the Sea Long Beach California",
        "Lumea by the Sea Long Beach",  
        "Lumea by the Sea hotel",
        "63 Lime Ave Long Beach CA",  # Exact address from New API
        "ChIJU2Hgr8BQBH0Rvpb6h9O2JHQ"  # Try with a place ID if we know it
    ]
    
    print("🏨 Testing Google Places API (LEGACY) for Lumea by the Sea hotel")
    print("🎯 GOAL: Get NEWEST reviews first using reviews_sort=newest")
    print("=" * 70)
    
    # Try each search term until we find the place
    for i, place_name in enumerate(search_terms, 1):
        print(f"\n🔍 Attempt {i}: Searching for '{place_name}'")
        try:
            reviews = places_api.get_newest_reviews(place_name)
            
            if reviews:
                print(f"\n🎉 SUCCESS: Retrieved {len(reviews)} reviews from Legacy API")
                print("📅 Reviews sorted by NEWEST first (reviews_sort=newest)")
                print("🎯 Looking for: Ava, Jose Hernandez, Eric, Maria Wagenius, Brooklynn Taylor")
                print("=" * 70)
                
                # Check for target reviewers
                target_names = ["Ava", "Jose Hernandez", "Eric", "Maria Wagenius", "Brooklynn Taylor"]
                found_targets = []
                
                for j, review in enumerate(reviews):
                    print(format_legacy_review(review, j))
                    
                    # Check if this is one of our target reviewers
                    author = review.get('author_name', '')
                    for target in target_names:
                        if target.lower() in author.lower():
                            found_targets.append(f"{author} (matches {target})")
                
                if found_targets:
                    print("\n🎯 TARGET REVIEWERS FOUND:")
                    for target in found_targets:
                        print(f"   ✅ {target}")
                else:
                    print("\n❌ None of the target reviewers found in legacy API results")
                    print("   Looking for: Ava, Jose Hernandez, Eric, Maria Wagenius, Brooklynn Taylor")
                
                return  # Exit after successful retrieval
                    
        except Exception as e:
            print(f"❌ Error with search term '{place_name}': {e}")
            continue
    
    # If we get here, none of the search terms worked
    print("\n❌ No reviews found via Google Places API (Legacy) with any search term")
    print("Possible issues:")
    print("- Places API (Legacy) not enabled for this API key")
    print("- API key restrictions")
    print("- The place doesn't exist in Google Places database")
    print("- reviews_sort parameter not supported")
    print("\n🔧 Note:")
    print("The legacy API requires 'Places API' (not 'Places API (New)') to be enabled")

if __name__ == "__main__":
    main()