#!/usr/bin/env python3
"""
Simple test to check if Google API key works at all
"""

import requests

def test_api_key():
    api_key = "AIzaSyD7c3dDzJ9LeU915YCri4fGvgQGALCBauo"
    
    print("🔧 Testing API key with different Google APIs...")
    print("=" * 50)
    
    # Test 1: Try Places API (what we need)
    print("\n1️⃣ Testing Places API (Find Place)...")
    url = "https://maps.googleapis.com/maps/api/place/findplacefromtext/json"
    params = {
        'input': 'restaurant',
        'inputtype': 'textquery',
        'fields': 'place_id,name',
        'key': api_key
    }
    
    try:
        response = requests.get(url, params=params)
        data = response.json()
        print(f"   Status: {data.get('status', 'Unknown')}")
        if data.get('status') == 'OK':
            print("   ✅ Places API is working!")
        else:
            print(f"   ❌ Places API error: {data.get('error_message', 'No error message')}")
    except Exception as e:
        print(f"   ❌ Request failed: {e}")
    
    # Test 2: Try Geocoding API (simpler, often enabled by default)
    print("\n2️⃣ Testing Geocoding API...")
    url = "https://maps.googleapis.com/maps/api/geocode/json"
    params = {
        'address': 'New York',
        'key': api_key
    }
    
    try:
        response = requests.get(url, params=params)
        data = response.json()
        print(f"   Status: {data.get('status', 'Unknown')}")
        if data.get('status') == 'OK':
            print("   ✅ Geocoding API is working!")
        else:
            print(f"   ❌ Geocoding API error: {data.get('error_message', 'No error message')}")
    except Exception as e:
        print(f"   ❌ Request failed: {e}")
    
    # Test 3: Try Maps JavaScript API (basic quota check)
    print("\n3️⃣ Testing basic API key validity...")
    url = "https://maps.googleapis.com/maps/api/js"
    params = {
        'key': api_key,
        'callback': 'test'
    }
    
    try:
        response = requests.get(url, params=params, timeout=5)
        if response.status_code == 200:
            print("   ✅ API key appears valid (HTTP 200)")
        else:
            print(f"   ❌ API key issue (HTTP {response.status_code})")
    except Exception as e:
        print(f"   ⚠️ Could not test basic validity: {e}")
    
    print("\n" + "=" * 50)
    print("📋 Summary:")
    print("- If Places API shows REQUEST_DENIED: Enable Places API in Google Cloud Console")
    print("- If Geocoding API works: Your API key is valid, just need to enable Places API")
    print("- If all APIs fail: Check API key restrictions or billing account")
    print("\n🔗 Google Cloud Console: https://console.cloud.google.com/apis/library")

if __name__ == "__main__":
    test_api_key()