using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotNetEnv;


namespace SpotifyApiServer
{
    public class SpotifyApiClient
    {
        private readonly string _clientId;
        private readonly string _clientSecret;

        private string? _accessToken = null;
        private DateTime _tokenExpiry = DateTime.MinValue;
        private readonly object _tokenLock = new object();

        private readonly HttpClient _httpClient = new HttpClient();


        public SpotifyApiClient()
        {
            try
            {
                Env.Load();

                _clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? string.Empty;
                _clientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                {
                    throw new InvalidOperationException("Critical error: SPOTIFY_CLIENT_ID or SPOTIFY_CLIENT_SECRET not found in environment variables!");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[FATAL] Failed to initialize Spotify client: {ex.Message}"); throw;
            }
        }

        private void EnsureValidToken()
        {

            if (DateTime.Now < _tokenExpiry) return;

            lock (_tokenLock)
            {

                if (DateTime.Now >= _tokenExpiry)
                {
                    Logger.Log("[API] Fetching new Spotify Access token...");

                    var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                    var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authString);

                    request.Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "client_credentials")
                    });
                    using var response = _httpClient.Send(request);
                    response.EnsureSuccessStatusCode();

                    using var reader = new StreamReader(response.Content.ReadAsStream());
                    var jsonString = reader.ReadToEnd();

                    using var doc = JsonDocument.Parse(jsonString);

                    _accessToken = doc.RootElement.GetProperty("access_token").GetString();
                    int expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

                    _tokenExpiry = DateTime.Now.AddSeconds(expiresIn - 60);

                    Logger.Log("[API] Token successfully refreshed!");
                }
            }
        }

        public string Search(string query, string type, string limit)
        {
            EnsureValidToken();

            string url = $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type={type}&limit={limit}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            using var response = _httpClient.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                using var readerError = new StreamReader(response.Content.ReadAsStream());
                string details = readerError.ReadToEnd();
                Logger.Log($"[SPOTIFY ERROR] {details}");
                throw new Exception($"Spotify API error: {response.StatusCode}");
            }

            using var reader = new StreamReader(response.Content.ReadAsStream());
            string jsonString = reader.ReadToEnd();

            using var doc = JsonDocument.Parse(jsonString);
            string rootNode = type + "s";

            if (doc.RootElement.TryGetProperty(rootNode, out JsonElement nodeProperty))
            {
                if (nodeProperty.GetProperty("items").GetArrayLength() == 0)
                {
                    throw new KeyNotFoundException($"The requested song or album '{query}' does not exist on Spotify.");
                }
            }

            return jsonString;
        }
    }
}
