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
        private string? _clientId;
        private string? _clientSecret;

        private string? _accessToken = null;

        private DateTime _tokenExpiry = DateTime.MinValue;

        private readonly SemaphoreSlim _tokenSemaphore = new SemaphoreSlim(1, 1);

        private readonly HttpClient _httpClient = new HttpClient();

        private async Task EnsureValidTokenAsync()
        {
            if (DateTime.Now < _tokenExpiry)
            {
                return;
            }

            await _tokenSemaphore.WaitAsync();

            try
            {
                if (DateTime.Now >= _tokenExpiry)
                {
                    Env.Load();

                    _clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
                    _clientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET");

                    if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                    {
                        throw new InvalidOperationException("Critical error: SPOTIFY_CLIENT_ID or SPOTIFY_CLIENT_SECRET not found in environment variables!");
                    }

                    Logger.Log("[API] Fetching new Spotify Access token...");

                    var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                    var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authString);

                    request.Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "client_credentials")
                    });

                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    string jsonString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonString);
                    _accessToken = doc.RootElement.GetProperty("access_token").GetString();
                    int expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

                    _tokenExpiry = DateTime.Now.AddSeconds(expiresIn - 60);

                    Logger.Log("[API] Token successfully refreshed!");
                }
            }
            finally
            {
                _tokenSemaphore.Release();
            }
        }

        public async Task<string> SearchAsync(string query, string type, string limit)
        {
            await EnsureValidTokenAsync();

            string url = $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type={type}&limit={limit}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string details = await response.Content.ReadAsStringAsync();
                Logger.Log($"[SPOTIFY ERROR] {details}");
                throw new Exception($"Spotify API error: {response.StatusCode}");
            }

            string jsonString = await response.Content.ReadAsStringAsync();
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