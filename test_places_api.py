#!/usr/bin/env python3
"""
Google Places API test script for Lumea by the Sea hotel reviews
"""

import requests
import json
import os
from typing import Dict, List, Optional

class GooglePlacesAPI:
    def __init__(self, api_key: str):
        self.api_key = api_key
        self.base_url = "https://maps.googleapis.com/maps/api/place"
    
    def find_place(self, query: str) -> Optional[str]:
        """Find a place by name and return its place_id"""
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
            
            if data['status'] == 'OK' and data['candidates']:
                place = data['candidates'][0]
                print(f"Found place: {place.get('name')} - {place.get('formatted_address')}")
                return place['place_id']
            else:
                print(f"No places found. Status: {data['status']}")
                return None
                
        except requests.RequestException as e:
            print(f"Error finding place: {e}")
            return None
    
    def get_place_details(self, place_id: str) -> Optional[Dict]:
        """Get detailed information about a place including reviews sorted by newest"""
        url = f"{self.base_url}/details/json"
        params = {
            'place_id': place_id,
            'fields': 'name,formatted_address,rating,user_ratings_total,reviews',
            'reviews_sort': 'newest',  # Sort reviews by newest first
            'key': self.api_key
        }
        
        try:
            response = requests.get(url, params=params)
            response.raise_for_status()
            data = response.json()
            
            if data['status'] == 'OK':
                return data['result']
            else:
                print(f"Error getting place details. Status: {data['status']}")
                return None
                
        except requests.RequestException as e:
            print(f"Error getting place details: {e}")
            return None
    
    def get_reviews(self, place_name: str) -> List[Dict]:
        """Get reviews for a place by name"""
        print(f"🔍 Searching for: {place_name}")
        
        # Find the place
        place_id = self.find_place(place_name)
        if not place_id:
            return []
        
        # Get place details with reviews
        place_details = self.get_place_details(place_id)
        if not place_details:
            return []
        
        # Extract and format reviews
        reviews = place_details.get('reviews', [])
        print(f"📝 Found {len(reviews)} reviews from Google Places API")
        print(f"⭐ Overall rating: {place_details.get('rating', 'N/A')}/5")
        print(f"📊 Total ratings: {place_details.get('user_ratings_total', 'N/A')}")
        
        return reviews

def format_review(review: Dict, index: int) -> str:
    """Format a single review for display"""
    author = review.get('author_name', 'Anonymous')
    rating = review.get('rating', 0)
    text = review.get('text', '')
    time = review.get('relative_time_description', 'Unknown time')
    
    # Truncate long reviews
    if len(text) > 200:
        text = text[:200] + "..."
    
    return f"""
📝 Review #{index + 1}:
👤 Author: {author}
⭐ Rating: {rating}/5 stars
🕐 Time: {time}
💬 Review: {text}
{"=" * 50}"""

def main():
    # Use the provided API key
    api_key = "AIzaSyD7c3dDzJ9LeU915YCri4fGvgQGALCBauo"
    
    # Initialize API client
    places_api = GooglePlacesAPI(api_key)
    
    # Test with multiple search variations for Lumea by the Sea hotel
    search_terms = [
        "Lumea by the Sea Long Beach California",
        "Lumea by the Sea Long Beach",
        "Lumea by the Sea hotel",
        "Lumea by the Sea 33.767505,-118.184170"  # Coordinates from the URL
    ]
    
    print("🏨 Testing Google Places API for Lumea by the Sea hotel")
    print("=" * 60)
    
    # Try each search term until we find the place
    for i, place_name in enumerate(search_terms, 1):
        print(f"\n🔍 Attempt {i}: Searching for '{place_name}'")
        try:
            reviews = places_api.get_reviews(place_name)
            
            if reviews:
                print(f"\n🎉 SUCCESS: Retrieved {len(reviews)} reviews from Google Places API")
                print("📅 Reviews sorted by NEWEST first (using reviews_sort=newest parameter)")
                print("=" * 60)
                
                for j, review in enumerate(reviews):
                    print(format_review(review, j))
                return  # Exit after successful retrieval
                    
        except Exception as e:
            print(f"❌ Error with search term '{place_name}': {e}")
            continue
    
    # If we get here, none of the search terms worked
    print("\n❌ No reviews found via Google Places API with any search term")
    print("Possible issues:")
    print("- Places API not enabled for this API key")
    print("- API key restrictions (HTTP referrers, IP restrictions)")
    print("- The place doesn't exist in Google Places database")
    print("- Need to enable 'Places API' in Google Cloud Console")
    print("\n🔧 To fix:")
    print("1. Go to Google Cloud Console: https://console.cloud.google.com/")
    print("2. Select your project")
    print("3. Go to 'APIs & Services' > 'Library'")
    print("4. Search for 'Places API' and enable it")
    print("5. Check API key restrictions in 'APIs & Services' > 'Credentials'")

if __name__ == "__main__":
    main()