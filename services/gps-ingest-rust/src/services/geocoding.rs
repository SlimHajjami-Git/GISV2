use anyhow::Result;
use lru::LruCache;
use reqwest::Client;
use serde::Deserialize;
use std::num::NonZeroUsize;
use std::sync::Arc;
use tokio::sync::Mutex;
use tracing::{debug, warn};

#[derive(Debug, Deserialize)]
pub struct NominatimResponse {
    pub display_name: Option<String>,
    pub address: Option<NominatimAddress>,
}

#[derive(Debug, Deserialize)]
pub struct NominatimAddress {
    pub road: Option<String>,
    pub house_number: Option<String>,
    pub suburb: Option<String>,
    pub city: Option<String>,
    pub town: Option<String>,
    pub village: Option<String>,
    pub state: Option<String>,
    pub country: Option<String>,
}

pub struct GeocodingService {
    client: Client,
    base_url: String,
    cache: Arc<Mutex<LruCache<String, String>>>,
    enabled: bool,
}

impl GeocodingService {
    pub fn new(nominatim_url: Option<String>) -> Self {
        let base_url = nominatim_url.unwrap_or_else(|| "http://nominatim:8080".to_string());
        let enabled = !base_url.is_empty();
        
        Self {
            client: Client::builder()
                .timeout(std::time::Duration::from_secs(5))
                .build()
                .unwrap_or_default(),
            base_url,
            cache: Arc::new(Mutex::new(LruCache::new(NonZeroUsize::new(10000).unwrap()))),
            enabled,
        }
    }

    /// Reverse geocode coordinates to address
    /// Returns None if geocoding fails or is disabled
    pub async fn reverse_geocode(&self, lat: f64, lon: f64) -> Option<String> {
        if !self.enabled {
            return None;
        }

        // Round coordinates for cache key (4 decimal places ≈ 11m precision)
        let cache_key = format!("{:.4},{:.4}", lat, lon);
        
        // Check cache first
        {
            let mut cache = self.cache.lock().await;
            if let Some(address) = cache.get(&cache_key) {
                debug!("Geocoding cache hit for {}", cache_key);
                return Some(address.clone());
            }
        }

        // Call Nominatim
        let url = format!(
            "{}/reverse?lat={}&lon={}&format=json&zoom=18",
            self.base_url, lat, lon
        );

        match self.client.get(&url).send().await {
            Ok(response) => {
                if let Ok(data) = response.json::<NominatimResponse>().await {
                    let address = self.format_address(&data);
                    
                    // Cache the result
                    {
                        let mut cache = self.cache.lock().await;
                        cache.put(cache_key, address.clone());
                    }
                    
                    debug!("Geocoded {},{} -> {}", lat, lon, address);
                    return Some(address);
                }
            }
            Err(e) => {
                warn!("Geocoding error for {},{}: {}", lat, lon, e);
            }
        }

        None
    }

    /// Format Nominatim response to a short readable address
    fn format_address(&self, response: &NominatimResponse) -> String {
        if let Some(addr) = &response.address {
            let mut parts: Vec<String> = Vec::new();

            // Street with number
            if let Some(road) = &addr.road {
                if let Some(number) = &addr.house_number {
                    parts.push(format!("{} {}", number, road));
                } else {
                    parts.push(road.clone());
                }
            }

            // City/Town/Village
            let locality = addr.city.as_ref()
                .or(addr.town.as_ref())
                .or(addr.village.as_ref())
                .or(addr.suburb.as_ref());
            
            if let Some(loc) = locality {
                parts.push(loc.clone());
            }

            if !parts.is_empty() {
                return parts.join(", ");
            }
        }

        // Fallback to display_name (truncated)
        if let Some(display) = &response.display_name {
            let parts: Vec<&str> = display.split(',').take(3).collect();
            return parts.join(",").trim().to_string();
        }

        "Adresse inconnue".to_string()
    }

    /// Get cache statistics
    pub async fn cache_stats(&self) -> (usize, usize) {
        let cache = self.cache.lock().await;
        (cache.len(), cache.cap().get())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_format_address() {
        let service = GeocodingService::new(None);
        
        let response = NominatimResponse {
            display_name: Some("Avenue Habib Bourguiba, Centre Ville, Tunis, Tunisia".to_string()),
            address: Some(NominatimAddress {
                road: Some("Avenue Habib Bourguiba".to_string()),
                house_number: Some("10".to_string()),
                suburb: Some("Centre Ville".to_string()),
                city: Some("Tunis".to_string()),
                town: None,
                village: None,
                state: None,
                country: Some("Tunisia".to_string()),
            }),
        };

        let address = service.format_address(&response);
        assert_eq!(address, "10 Avenue Habib Bourguiba, Tunis");
    }
}
