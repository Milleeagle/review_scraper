#!/usr/bin/env python3
"""
Google Places API (NEW) test script for Lumea by the Sea hotel reviews
Uses the new Places API format that Google recommends
"""

import requests
import json
from typing import Dict, List, Optional

class GooglePlacesNewAPI:
    def __init__(self, api_key: str):
        self.api_key = api_key
        self.base_url = "https://places.googleapis.com/v1/places"
        self.headers = {
            'Content-Type': 'application/json',
            'X-Goog-Api-Key': api_key,
            'X-Goog-FieldMask': 'places.id,places.displayName,places.formattedAddress,places.rating,places.userRatingCount,places.reviews'
        }
    
    def search_place(self, query: str) -> Optional[str]:
        """Search for a place using the new Places API and return place ID"""
        url = f"{self.base_url}:searchText"
        
        payload = {
            "textQuery": query,
            "maxResultCount": 1
        }
        
        try:
            response = requests.post(url, headers=self.headers, json=payload)
            response.raise_for_status()
            data = response.json()
            
            if 'places' in data and data['places']:
                place = data['places'][0]
                name = place.get('displayName', {}).get('text', 'Unknown')
                address = place.get('formattedAddress', 'Unknown address')
                print(f"Found place: {name} - {address}")
                return place['id']
            else:
                print(f"No places found for query: {query}")
                return None
                
        except requests.RequestException as e:
            print(f"Error searching for place: {e}")
            if hasattr(e, 'response') and e.response:
                print(f"Response: {e.response.text}")
            return None
    
    def get_place_details(self, place_id: str) -> Optional[Dict]:
        """Get detailed information about a place including reviews"""
        url = f"{self.base_url}/{place_id}"
        
        # Use field mask to specify we want reviews
        headers = self.headers.copy()
        headers['X-Goog-FieldMask'] = 'displayName,formattedAddress,rating,userRatingCount,reviews'
        
        try:
            response = requests.get(url, headers=headers)
            response.raise_for_status()
            data = response.json()
            
            return data
                
        except requests.RequestException as e:
            print(f"Error getting place details: {e}")
            if hasattr(e, 'response') and e.response:
                print(f"Response: {e.response.text}")
            return None
    
    def get_reviews(self, place_name: str) -> List[Dict]:
        """Get reviews for a place by name using NEW Places API"""
        print(f"🔍 Searching for: {place_name}")
        
        # Search for the place
        place_id = self.search_place(place_name)
        if not place_id:
            return []
        
        # Get place details with reviews
        place_details = self.get_place_details(place_id)
        if not place_details:
            return []
        
        # Extract and format reviews
        reviews = place_details.get('reviews', [])
        print(f"📝 Found {len(reviews)} reviews from Google Places API (New)")
        
        if 'rating' in place_details:
            print(f"⭐ Overall rating: {place_details['rating']}/5")
        if 'userRatingCount' in place_details:
            print(f"📊 Total ratings: {place_details['userRatingCount']}")
        
        return reviews

def format_new_review(review: Dict, index: int) -> str:
    """Format a single review from the new Places API for display"""
    author = review.get('authorAttribution', {}).get('displayName', 'Anonymous')
    rating = review.get('rating', 0)
    text = review.get('text', {}).get('text', 'No text available')
    
    # Get relative time
    time_info = review.get('relativePublishTimeDescription', 'Unknown time')
    
    # Truncate long reviews
    if len(text) > 300:
        text = text[:300] + "..."
    
    return f"""
📝 Review #{index + 1}:
👤 Author: {author}
⭐ Rating: {rating}/5 stars
🕐 Time: {time_info}
💬 Review: {text}
{"=" * 50}"""

def main():
    # Use the provided API key
    api_key = "AIzaSyD7c3dDzJ9LeU915YCri4fGvgQGALCBauo"
    
    # Initialize API client
    places_api = GooglePlacesNewAPI(api_key)
    
    # Test with multiple search variations for Lumea by the Sea hotel
    search_terms = [
        "Lumea by the Sea Long Beach California",
        "Lumea by the Sea Long Beach",
        "Lumea by the Sea hotel California",
        "Lumea by the Sea 218 E 3rd St Long Beach CA"  # Specific address
    ]
    
    print("🏨 Testing Google Places API (NEW) for Lumea by the Sea hotel")
    print("=" * 60)
    
    # Try each search term until we find the place
    for i, place_name in enumerate(search_terms, 1):
        print(f"\n🔍 Attempt {i}: Searching for '{place_name}'")
        try:
            reviews = places_api.get_reviews(place_name)
            
            if reviews:
                print(f"\n🎉 SUCCESS: Retrieved {len(reviews)} reviews from Google Places API (New)")
                print("📅 These are the most relevant/recent reviews according to Google")
                print("=" * 60)
                
                for j, review in enumerate(reviews):
                    print(format_new_review(review, j))
                return  # Exit after successful retrieval
                    
        except Exception as e:
            print(f"❌ Error with search term '{place_name}': {e}")
            continue
    
    # If we get here, none of the search terms worked
    print("\n❌ No reviews found via Google Places API (New) with any search term")
    print("Possible issues:")
    print("- Places API (New) not enabled for this API key")
    print("- API key restrictions (HTTP referrers, IP restrictions)")
    print("- The place doesn't exist in Google Places database")
    print("- Billing not set up for the Google Cloud project")
    print("\n🔧 To fix:")
    print("1. Go to Google Cloud Console: https://console.cloud.google.com/")
    print("2. Select your project")
    print("3. Go to 'APIs & Services' > 'Library'")
    print("4. Search for 'Places API (New)' and enable it")
    print("5. Make sure billing is enabled for your project")
    print("6. Check API key restrictions in 'APIs & Services' > 'Credentials'")

if __name__ == "__main__":
    main()