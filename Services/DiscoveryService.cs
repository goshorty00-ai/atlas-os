using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.MediaIntelligence;

namespace AtlasAI.Services
{
    public class DiscoveryService
    {
        private static readonly HttpClient _httpClient = new();
        private readonly MediaIntelligenceAgent _agent = new();

        public async Task<DiscoveryData> GetDiscoveryDataAsync(string traktClientId, string traktToken, CancellationToken ct = default)
        {
            var tmdbKey = (IntegrationKeyStore.GetDecrypted("tmdb") ?? "").Trim();
            var lastFmKey = (IntegrationKeyStore.GetDecrypted("lastfm") ?? "").Trim();
            var igdbClientId = (IntegrationKeyStore.GetDecrypted("igdb_client_id") ?? "").Trim();
            var igdbSecret = (IntegrationKeyStore.GetDecrypted("igdb_client_secret") ?? "").Trim();
            var spotifyClientId = (IntegrationKeyStore.GetDecrypted("spotify_client_id") ?? "").Trim();
            
            try
            {
                // Get trending movies and TV shows
                var trendingTask = GetTrendingMediaAsync(tmdbKey, ct);
                
                // Get trending music
                var trendingMusicTask = GetTrendingMusicAsync(lastFmKey, spotifyClientId, ct);
                
                // Get trending games
                var trendingGamesTask = GetTrendingGamesAsync(igdbClientId, igdbSecret, ct);
                
                // Get latest trailers
                var trailersTask = GetLatestTrailersAsync(tmdbKey, ct);
                
                // Get entertainment news (movies, music, games, celebrities)
                var newsTask = GetEntertainmentNewsAsync(ct);
                
                // Get upcoming releases (movies, music, games)
                var upcomingTask = GetUpcomingReleasesAsync(tmdbKey, lastFmKey, igdbClientId, igdbSecret, ct);
                
                // Get trending celebrities (actors, musicians, gamers)
                var celebritiesTask = GetTrendingCelebritiesAsync(tmdbKey, lastFmKey, ct);
                
                // Get featured spotlight
                var featuredTask = GetFeaturedSpotlightAsync(tmdbKey, ct);

                // Get recent movies + TV for hero carousel (date-filtered, no item cap)
                var heroMovieTvTask = GetRecentMovieTvForHeroAsync(tmdbKey, ct);

                await Task.WhenAll(trendingTask, trendingMusicTask, trendingGamesTask, trailersTask, newsTask, upcomingTask, celebritiesTask, featuredTask, heroMovieTvTask);

                var trending = await trendingTask;
                trending.AddRange(await trendingMusicTask);
                trending.AddRange(await trendingGamesTask);

                var upcoming = await upcomingTask;
                var featured = await featuredTask;

                await AttachTrailerUrlsFromTmdbAsync(trending, tmdbKey, ct);
                await AttachTrailerUrlsFromTmdbAsync(upcoming, tmdbKey, ct);
                if (featured != null)
                {
                    await AttachTrailerUrlsFromTmdbAsync(new List<DiscoveryMedia> { featured }, tmdbKey, ct);
                }

                return new DiscoveryData
                {
                    Trending = trending,
                    Trailers = await trailersTask,
                    News = await newsTask,
                    Upcoming = upcoming,
                    Celebrities = await celebritiesTask,
                    Featured = featured,
                    HeroMovieTv = await heroMovieTvTask
                };
            }
            catch (Exception ex)
            {
                return new DiscoveryData
                {
                    Error = $"Failed to load discovery data: {ex.Message}"
                };
            }
        }

        private async Task<List<DiscoveryMedia>> GetTrendingMediaAsync(string tmdbKey, CancellationToken ct)
        {
            var results = new List<DiscoveryMedia>();

            try
            {
                // Get trending movies
                var moviesUrl = $"https://api.themoviedb.org/3/trending/movie/week?api_key={tmdbKey}";
                var moviesResponse = await _httpClient.GetStringAsync(moviesUrl, ct);
                var moviesData = JsonDocument.Parse(moviesResponse);
                
                foreach (var item in moviesData.RootElement.GetProperty("results").EnumerateArray().Take(10))
                {
                    var backdropPath = item.TryGetProperty("backdrop_path", out var backdropEl) ? (backdropEl.GetString() ?? "") : "";
                    var posterPath = item.TryGetProperty("poster_path", out var posterEl) ? (posterEl.GetString() ?? "") : "";
                    var imagePath = !string.IsNullOrWhiteSpace(backdropPath) ? backdropPath : posterPath;

                    results.Add(new DiscoveryMedia
                    {
                        Id = item.GetProperty("id").GetInt32().ToString(),
                        Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                        Image = string.IsNullOrWhiteSpace(imagePath) ? "" : $"https://image.tmdb.org/t/p/w1280{imagePath}",
                        Rating = item.TryGetProperty("vote_average", out var rating) ? rating.GetDouble() : 0,
                        Type = "movie",
                        IsNew = true,
                        Overview = item.TryGetProperty("overview", out var overview) ? overview.GetString() : "",
                        ReleaseDate = item.TryGetProperty("release_date", out var releaseDate) ? releaseDate.GetString() : ""
                    });
                }

                // Get trending TV shows
                var tvUrl = $"https://api.themoviedb.org/3/trending/tv/week?api_key={tmdbKey}";
                var tvResponse = await _httpClient.GetStringAsync(tvUrl, ct);
                var tvData = JsonDocument.Parse(tvResponse);
                
                foreach (var item in tvData.RootElement.GetProperty("results").EnumerateArray().Take(5))
                {
                    var backdropPath = item.TryGetProperty("backdrop_path", out var backdropEl) ? (backdropEl.GetString() ?? "") : "";
                    var posterPath = item.TryGetProperty("poster_path", out var posterEl) ? (posterEl.GetString() ?? "") : "";
                    var imagePath = !string.IsNullOrWhiteSpace(backdropPath) ? backdropPath : posterPath;

                    results.Add(new DiscoveryMedia
                    {
                        Id = item.GetProperty("id").GetInt32().ToString(),
                        Title = item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                        Image = string.IsNullOrWhiteSpace(imagePath) ? "" : $"https://image.tmdb.org/t/p/w1280{imagePath}",
                        Rating = item.TryGetProperty("vote_average", out var rating) ? rating.GetDouble() : 0,
                        Type = "tv",
                        IsNew = true,
                        Overview = item.TryGetProperty("overview", out var overview) ? overview.GetString() : "",
                        ReleaseDate = item.TryGetProperty("first_air_date", out var firstAirDate) ? firstAirDate.GetString() : ""
                    });
                }
            }
            catch
            {
                // Fallback to empty list on error
            }

            return results;
        }

        private async Task<List<DiscoveryMedia>> GetLatestTrailersAsync(string tmdbKey, CancellationToken ct)
        {
            var results = new List<DiscoveryMedia>();

            try
            {
                if (string.IsNullOrWhiteSpace(tmdbKey))
                    return results;

                async Task AddTrailersFromEndpointAsync(string mediaType, string endpointUrl, int take)
                {
                    var response = await _httpClient.GetStringAsync(endpointUrl, ct);
                    using var data = JsonDocument.Parse(response);
                    if (!data.RootElement.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
                        return;

                    foreach (var item in items.EnumerateArray().Take(take))
                    {
                        if (ct.IsCancellationRequested)
                            return;

                        if (!item.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
                            continue;

                        var tmdbId = idElement.GetInt32();
                        if (tmdbId <= 0)
                            continue;

                        var title = mediaType == "tv"
                            ? (item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "")
                            : (item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "");
                        title = title.Trim();
                        if (string.IsNullOrWhiteSpace(title))
                            continue;

                        var trailerUrl = await TryGetTmdbTrailerUrlAsync(mediaType, tmdbId, tmdbKey, ct);
                        if (string.IsNullOrWhiteSpace(trailerUrl))
                            continue;

                        var backdropPath = item.TryGetProperty("backdrop_path", out var backdropEl) ? (backdropEl.GetString() ?? "") : "";
                        var posterPath = item.TryGetProperty("poster_path", out var posterEl) ? (posterEl.GetString() ?? "") : "";
                        var imagePath = !string.IsNullOrWhiteSpace(backdropPath) ? backdropPath : posterPath;

                        var releaseDate = mediaType == "tv"
                            ? (item.TryGetProperty("first_air_date", out var firstAirEl) ? firstAirEl.GetString() : "")
                            : (item.TryGetProperty("release_date", out var releaseEl) ? releaseEl.GetString() : "");

                        results.Add(new DiscoveryMedia
                        {
                            Id = $"{mediaType}-{tmdbId}",
                            Title = $"{title} - Official Trailer",
                            Image = string.IsNullOrWhiteSpace(imagePath) ? "" : $"https://image.tmdb.org/t/p/w1280{imagePath}",
                            Type = mediaType,
                            ReleaseDate = releaseDate,
                            Overview = item.TryGetProperty("overview", out var overview) ? overview.GetString() : "",
                            TrailerUrl = trailerUrl,
                            Rating = item.TryGetProperty("vote_average", out var voteAverage) ? voteAverage.GetDouble() : 0
                        });
                    }
                }

                var upcomingMoviesUrl = $"https://api.themoviedb.org/3/movie/upcoming?api_key={tmdbKey}&language=en-US&page=1";
                await AddTrailersFromEndpointAsync("movie", upcomingMoviesUrl, 16);

                var popularTvUrl = $"https://api.themoviedb.org/3/tv/popular?api_key={tmdbKey}&language=en-US&page=1";
                await AddTrailersFromEndpointAsync("tv", popularTvUrl, 14);

                results = results
                    .GroupBy(item => item.TrailerUrl ?? item.Id)
                    .Select(group => group.First())
                    .Take(18)
                    .ToList();
            }
            catch
            {
                // Fallback to empty list on error
            }

            return results;
        }

        private async Task<List<DiscoveryNews>> GetEntertainmentNewsAsync(CancellationToken ct)
        {
            var results = new List<DiscoveryNews>();

            try
            {
                // Use NewsAPI for comprehensive entertainment news
                var newsApiKey = (IntegrationKeyStore.GetDecrypted("newsapi") ?? "").Trim();
                
                if (!string.IsNullOrWhiteSpace(newsApiKey))
                {
                    // Get news for movies, music, gaming, and celebrities
                    var categories = new[] { "entertainment", "movies", "music", "gaming", "celebrities", "actors", "singers" };
                    var query = string.Join(" OR ", categories);
                    
                    var newsUrl = $"https://newsapi.org/v2/everything?q={Uri.EscapeDataString(query)}&sortBy=publishedAt&apiKey={newsApiKey}&pageSize=30&language=en";
                    var response = await _httpClient.GetStringAsync(newsUrl, ct);
                    var data = JsonDocument.Parse(response);
                    
                    foreach (var article in data.RootElement.GetProperty("articles").EnumerateArray().Take(20))
                    {
                        var title = article.GetProperty("title").GetString() ?? "";
                        var description = article.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
                        
                        // Determine if trending based on keywords
                        var isTrending = title.Contains("breaking", StringComparison.OrdinalIgnoreCase) ||
                                       title.Contains("exclusive", StringComparison.OrdinalIgnoreCase) ||
                                       title.Contains("announces", StringComparison.OrdinalIgnoreCase);

                        results.Add(new DiscoveryNews
                        {
                            Id = Guid.NewGuid().ToString(),
                            Headline = title,
                            Image = article.TryGetProperty("urlToImage", out var img) ? img.GetString() ?? "" : "",
                            Preview = description,
                            TimeAgo = CalculateTimeAgo(article.GetProperty("publishedAt").GetString()),
                            Url = article.TryGetProperty("url", out var url) ? url.GetString() : "",
                            Trending = isTrending,
                            Source = article.TryGetProperty("source", out var source) && source.TryGetProperty("name", out var sourceName) 
                                ? sourceName.GetString() ?? "" : ""
                        });
                    }
                }
            }
            catch
            {
                // Fallback to empty list on error
            }

            return results;
        }

        private async Task<List<DiscoveryMedia>> GetTrendingMusicAsync(string lastFmKey, string spotifyClientId, CancellationToken ct)
        {
            var results = new List<DiscoveryMedia>();

            try
            {
                if (!string.IsNullOrWhiteSpace(lastFmKey))
                {
                    // Get trending tracks from Last.fm
                    var url = $"https://ws.audioscrobbler.com/2.0/?method=chart.gettoptracks&api_key={lastFmKey}&format=json&limit=15";
                    var response = await _httpClient.GetStringAsync(url, ct);
                    var data = JsonDocument.Parse(response);
                    
                    foreach (var track in data.RootElement.GetProperty("tracks").GetProperty("track").EnumerateArray().Take(10))
                    {
                        var artist = track.GetProperty("artist").GetProperty("name").GetString() ?? "";
                        var trackName = track.GetProperty("name").GetString() ?? "";
                        var playcount = track.TryGetProperty("playcount", out var pc) ? pc.GetInt64() : 0;
                        
                        // Get album art
                        var image = "";
                        if (track.TryGetProperty("image", out var images))
                        {
                            foreach (var img in images.EnumerateArray())
                            {
                                if (img.GetProperty("size").GetString() == "extralarge")
                                {
                                    image = img.GetProperty("#text").GetString() ?? "";
                                    break;
                                }
                            }
                        }

                        results.Add(new DiscoveryMedia
                        {
                            Id = track.TryGetProperty("mbid", out var mbid) ? mbid.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                            Title = $"{trackName} - {artist}",
                            Image = image,
                            Rating = Math.Min(10, playcount / 100000.0), // Convert playcount to rating scale
                            Type = "music",
                            IsNew = true,
                            Overview = $"Trending track by {artist}",
                            TrailerUrl = BuildYouTubeSearchUrl($"{trackName} {artist} official video")
                        });
                    }

                    // Get trending albums
                    var albumsUrl = $"https://ws.audioscrobbler.com/2.0/?method=chart.gettopalbums&api_key={lastFmKey}&format=json&limit=10";
                    var albumsResponse = await _httpClient.GetStringAsync(albumsUrl, ct);
                    var albumsData = JsonDocument.Parse(albumsResponse);
                    
                    foreach (var album in albumsData.RootElement.GetProperty("albums").GetProperty("album").EnumerateArray().Take(5))
                    {
                        var artist = album.GetProperty("artist").GetProperty("name").GetString() ?? "";
                        var albumName = album.GetProperty("name").GetString() ?? "";
                        
                        var image = "";
                        if (album.TryGetProperty("image", out var images))
                        {
                            foreach (var img in images.EnumerateArray())
                            {
                                if (img.GetProperty("size").GetString() == "extralarge")
                                {
                                    image = img.GetProperty("#text").GetString() ?? "";
                                    break;
                                }
                            }
                        }

                        results.Add(new DiscoveryMedia
                        {
                            Id = album.TryGetProperty("mbid", out var mbid) ? mbid.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                            Title = $"{albumName} - {artist}",
                            Image = image,
                            Type = "music",
                            IsNew = false,
                            Overview = $"Popular album by {artist}",
                            TrailerUrl = BuildYouTubeSearchUrl($"{albumName} {artist} official audio")
                        });
                    }
                }

                if (results.Count == 0)
                {
                    var appleMusicUrl = "https://rss.applemarketingtools.com/api/v2/us/music/most-played/25/songs.json";
                    var appleMusicResponse = await _httpClient.GetStringAsync(appleMusicUrl, ct);
                    using var appleMusicData = JsonDocument.Parse(appleMusicResponse);

                    if (appleMusicData.RootElement.TryGetProperty("feed", out var feed) &&
                        feed.TryGetProperty("results", out var songs) &&
                        songs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var song in songs.EnumerateArray().Take(10))
                        {
                            var title = song.TryGetProperty("name", out var titleEl) ? (titleEl.GetString() ?? "") : "";
                            var artist = song.TryGetProperty("artistName", out var artistEl) ? (artistEl.GetString() ?? "") : "";
                            var releaseDate = song.TryGetProperty("releaseDate", out var releaseEl) ? (releaseEl.GetString() ?? "") : "";
                            var image = song.TryGetProperty("artworkUrl100", out var artEl) ? (artEl.GetString() ?? "") : "";
                            if (!string.IsNullOrWhiteSpace(image))
                                image = image.Replace("100x100", "600x600", StringComparison.OrdinalIgnoreCase);

                            if (string.IsNullOrWhiteSpace(title))
                                continue;

                            results.Add(new DiscoveryMedia
                            {
                                Id = song.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? Guid.NewGuid().ToString()) : Guid.NewGuid().ToString(),
                                Title = string.IsNullOrWhiteSpace(artist) ? title : $"{title} - {artist}",
                                Image = image,
                                Rating = 7.5,
                                Type = "music",
                                IsNew = true,
                                ReleaseDate = releaseDate,
                                Overview = string.IsNullOrWhiteSpace(artist) ? "Top chart track" : $"Top chart track by {artist}",
                                TrailerUrl = BuildYouTubeSearchUrl($"{title} {artist} official video")
                            });
                        }
                    }
                }
            }
            catch
            {
                // Fallback to empty list on error
            }

            return results;
        }

        private async Task<List<DiscoveryMedia>> GetTrendingGamesAsync(string igdbClientId, string igdbSecret, CancellationToken ct)
        {
            var results = new List<DiscoveryMedia>();

            try
            {
                if (!string.IsNullOrWhiteSpace(igdbClientId) && !string.IsNullOrWhiteSpace(igdbSecret))
                {
                    // Get IGDB access token
                    var tokenUrl = $"https://id.twitch.tv/oauth2/token?client_id={igdbClientId}&client_secret={igdbSecret}&grant_type=client_credentials";
                    var tokenResponse = await _httpClient.PostAsync(tokenUrl, null, ct);
                    var tokenData = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
                    var accessToken = tokenData.RootElement.GetProperty("access_token").GetString();

                    // Get trending games
                    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
                    request.Headers.Add("Client-ID", igdbClientId);
                    request.Headers.Add("Authorization", $"Bearer {accessToken}");
                    request.Content = new StringContent(
                        "fields name, cover.url, rating, first_release_date, summary, genres.name; where rating > 70 & first_release_date > 1640995200; sort rating desc; limit 20;",
                        System.Text.Encoding.UTF8,
                        "text/plain"
                    );

                    var response = await _httpClient.SendAsync(request, ct);
                    var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    
                    foreach (var game in data.RootElement.EnumerateArray().Take(15))
                    {
                        var coverUrl = "";
                        if (game.TryGetProperty("cover", out var cover) && cover.TryGetProperty("url", out var url))
                        {
                            coverUrl = "https:" + url.GetString()?.Replace("t_thumb", "t_cover_big");
                        }

                        var genres = new List<string>();
                        if (game.TryGetProperty("genres", out var genresArray))
                        {
                            foreach (var genre in genresArray.EnumerateArray())
                            {
                                if (genre.TryGetProperty("name", out var genreName))
                                    genres.Add(genreName.GetString() ?? "");
                            }
                        }

                        results.Add(new DiscoveryMedia
                        {
                            Id = game.GetProperty("id").GetInt32().ToString(),
                            Title = game.GetProperty("name").GetString() ?? "",
                            Image = coverUrl,
                            Rating = game.TryGetProperty("rating", out var rating) ? rating.GetDouble() / 10.0 : 0,
                            Type = "game",
                            IsNew = true,
                            Overview = game.TryGetProperty("summary", out var summary) ? summary.GetString() : "",
                            Genres = genres,
                            ReleaseDate = game.TryGetProperty("first_release_date", out var releaseDate) 
                                ? DateTimeOffset.FromUnixTimeSeconds(releaseDate.GetInt64()).ToString("MMM dd, yyyy") 
                                : "",
                            TrailerUrl = BuildYouTubeSearchUrl($"{game.GetProperty("name").GetString()} gameplay trailer")
                        });
                    }
                }

                if (results.Count == 0)
                {
                    var steamUrl = "https://store.steampowered.com/api/featuredcategories?cc=us&l=en";
                    var steamResponse = await _httpClient.GetStringAsync(steamUrl, ct);
                    using var steamData = JsonDocument.Parse(steamResponse);

                    static IEnumerable<JsonElement> EnumerateSteamItems(JsonElement root, string section)
                    {
                        if (!root.TryGetProperty(section, out var sectionElement) ||
                            !sectionElement.TryGetProperty("items", out var items) ||
                            items.ValueKind != JsonValueKind.Array)
                            return Enumerable.Empty<JsonElement>();

                        return items.EnumerateArray();
                    }

                    var steamItems = EnumerateSteamItems(steamData.RootElement, "coming_soon")
                        .Concat(EnumerateSteamItems(steamData.RootElement, "top_sellers"))
                        .Take(12)
                        .ToList();

                    foreach (var game in steamItems)
                    {
                        var name = game.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        var image = game.TryGetProperty("large_capsule_image", out var imageEl) ? (imageEl.GetString() ?? "") : "";

                        results.Add(new DiscoveryMedia
                        {
                            Id = game.TryGetProperty("id", out var idEl) ? idEl.GetInt32().ToString() : Guid.NewGuid().ToString(),
                            Title = name,
                            Image = image,
                            Rating = 7.4,
                            Type = "game",
                            IsNew = true,
                            ReleaseDate = "Coming Soon",
                            Overview = "Popular game currently trending on Steam.",
                            TrailerUrl = BuildYouTubeSearchUrl($"{name} gameplay trailer")
                        });
                    }
                }
            }
            catch
            {
                // Fallback to empty list on error
            }

            return results;
        }

        private async Task<List<DiscoveryMedia>> GetUpcomingReleasesAsync(string tmdbKey, string lastFmKey, string igdbClientId, string igdbSecret, CancellationToken ct)
        {
            var results = new List<DiscoveryMedia>();

            try
            {
                // Movies
                if (!string.IsNullOrWhiteSpace(tmdbKey))
                {
                    var url = $"https://api.themoviedb.org/3/movie/upcoming?api_key={tmdbKey}&language=en-US&page=1";
                    var response = await _httpClient.GetStringAsync(url, ct);
                    var data = JsonDocument.Parse(response);
                    
                    foreach (var item in data.RootElement.GetProperty("results").EnumerateArray().Take(10))
                    {
                        var backdropPath = item.TryGetProperty("backdrop_path", out var backdropEl) ? (backdropEl.GetString() ?? "") : "";
                        var posterPath = item.TryGetProperty("poster_path", out var posterEl) ? (posterEl.GetString() ?? "") : "";
                        var imagePath = !string.IsNullOrWhiteSpace(backdropPath) ? backdropPath : posterPath;

                        results.Add(new DiscoveryMedia
                        {
                            Id = item.GetProperty("id").GetInt32().ToString(),
                            Title = item.GetProperty("title").GetString() ?? "",
                            Image = string.IsNullOrWhiteSpace(imagePath) ? "" : $"https://image.tmdb.org/t/p/w1280{imagePath}",
                            Type = "movie",
                            ReleaseDate = item.TryGetProperty("release_date", out var releaseDate) ? releaseDate.GetString() : "",
                            Overview = item.TryGetProperty("overview", out var overview) ? overview.GetString() : "",
                            Rating = item.TryGetProperty("vote_average", out var rating) ? rating.GetDouble() : 0
                        });
                    }
                }

                // Upcoming Games
                if (!string.IsNullOrWhiteSpace(igdbClientId) && !string.IsNullOrWhiteSpace(igdbSecret))
                {
                    var tokenUrl = $"https://id.twitch.tv/oauth2/token?client_id={igdbClientId}&client_secret={igdbSecret}&grant_type=client_credentials";
                    var tokenResponse = await _httpClient.PostAsync(tokenUrl, null, ct);
                    var tokenData = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
                    var accessToken = tokenData.RootElement.GetProperty("access_token").GetString();

                    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
                    request.Headers.Add("Client-ID", igdbClientId);
                    request.Headers.Add("Authorization", $"Bearer {accessToken}");
                    
                    var futureTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    request.Content = new StringContent(
                        $"fields name, cover.url, first_release_date, summary, hypes; where first_release_date > {futureTimestamp}; sort hypes desc; limit 10;",
                        System.Text.Encoding.UTF8,
                        "text/plain"
                    );

                    var response = await _httpClient.SendAsync(request, ct);
                    var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    
                    foreach (var game in data.RootElement.EnumerateArray())
                    {
                        var coverUrl = "";
                        if (game.TryGetProperty("cover", out var cover) && cover.TryGetProperty("url", out var url))
                        {
                            coverUrl = "https:" + url.GetString()?.Replace("t_thumb", "t_cover_big");
                        }

                        results.Add(new DiscoveryMedia
                        {
                            Id = game.GetProperty("id").GetInt32().ToString(),
                            Title = game.GetProperty("name").GetString() ?? "",
                            Image = coverUrl,
                            Type = "game",
                            ReleaseDate = game.TryGetProperty("first_release_date", out var releaseDate) 
                                ? DateTimeOffset.FromUnixTimeSeconds(releaseDate.GetInt64()).ToString("MMM dd, yyyy") 
                                : "",
                            Overview = game.TryGetProperty("summary", out var summary) ? summary.GetString() : "",
                            TrailerUrl = BuildYouTubeSearchUrl($"{game.GetProperty("name").GetString()} gameplay trailer")
                        });
                    }
                }

                if (results.Count == 0)
                {
                    var steamUrl = "https://store.steampowered.com/api/featuredcategories?cc=us&l=en";
                    var steamResponse = await _httpClient.GetStringAsync(steamUrl, ct);
                    using var steamData = JsonDocument.Parse(steamResponse);

                    if (steamData.RootElement.TryGetProperty("coming_soon", out var comingSoon) &&
                        comingSoon.TryGetProperty("items", out var items) &&
                        items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var game in items.EnumerateArray().Take(10))
                        {
                            var name = game.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            var image = game.TryGetProperty("large_capsule_image", out var imageEl) ? (imageEl.GetString() ?? "") : "";

                            results.Add(new DiscoveryMedia
                            {
                                Id = game.TryGetProperty("id", out var idEl) ? idEl.GetInt32().ToString() : Guid.NewGuid().ToString(),
                                Title = name,
                                Image = image,
                                Type = "game",
                                ReleaseDate = "Coming Soon",
                                Overview = "Upcoming game release from Steam coming soon list.",
                                TrailerUrl = BuildYouTubeSearchUrl($"{name} gameplay trailer")
                            });
                        }
                    }
                }
            }
            catch
            {
                // Fallback to empty list on error
            }

            return results;
        }

        private async Task<List<DiscoveryCelebrity>> GetTrendingCelebritiesAsync(string tmdbKey, string lastFmKey, CancellationToken ct)
        {
            var results = new List<DiscoveryCelebrity>();

            try
            {
                // Actors and Directors from TMDB
                if (!string.IsNullOrWhiteSpace(tmdbKey))
                {
                    var url = $"https://api.themoviedb.org/3/trending/person/week?api_key={tmdbKey}";
                    var response = await _httpClient.GetStringAsync(url, ct);
                    var data = JsonDocument.Parse(response);
                    
                    foreach (var item in data.RootElement.GetProperty("results").EnumerateArray().Take(8))
                    {
                        var knownFor = item.TryGetProperty("known_for", out var kf) && kf.GetArrayLength() > 0
                            ? kf[0].TryGetProperty("title", out var title) ? title.GetString() : kf[0].TryGetProperty("name", out var name) ? name.GetString() : ""
                            : "";

                        var role = knownFor;
                        if (string.IsNullOrWhiteSpace(role) && item.TryGetProperty("known_for_department", out var dept))
                        {
                            role = dept.GetString() ?? "";
                        }

                        results.Add(new DiscoveryCelebrity
                        {
                            Id = item.GetProperty("id").GetInt32().ToString(),
                            Name = item.GetProperty("name").GetString() ?? "",
                            Image = $"https://image.tmdb.org/t/p/w500{item.GetProperty("profile_path").GetString()}",
                            Role = role,
                            Trending = true,
                            Category = "Actor/Director"
                        });
                    }
                }

                // Musicians from Last.fm
                if (!string.IsNullOrWhiteSpace(lastFmKey))
                {
                    var url = $"https://ws.audioscrobbler.com/2.0/?method=chart.gettopartists&api_key={lastFmKey}&format=json&limit=8";
                    var response = await _httpClient.GetStringAsync(url, ct);
                    var data = JsonDocument.Parse(response);
                    
                    foreach (var artist in data.RootElement.GetProperty("artists").GetProperty("artist").EnumerateArray().Take(5))
                    {
                        var image = "";
                        if (artist.TryGetProperty("image", out var images))
                        {
                            foreach (var img in images.EnumerateArray())
                            {
                                if (img.GetProperty("size").GetString() == "extralarge")
                                {
                                    image = img.GetProperty("#text").GetString() ?? "";
                                    break;
                                }
                            }
                        }

                        results.Add(new DiscoveryCelebrity
                        {
                            Id = artist.TryGetProperty("mbid", out var mbid) ? mbid.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                            Name = artist.GetProperty("name").GetString() ?? "",
                            Image = image,
                            Role = "Musician",
                            Trending = true,
                            Category = "Music"
                        });
                    }
                }
            }
            catch
            {
                // Fallback to empty list on error
            }

            return results;
        }

        private async Task<DiscoveryMedia?> GetFeaturedSpotlightAsync(string tmdbKey, CancellationToken ct)
        {
            try
            {
                // Get the top trending movie
                var url = $"https://api.themoviedb.org/3/trending/movie/day?api_key={tmdbKey}";
                var response = await _httpClient.GetStringAsync(url, ct);
                var data = JsonDocument.Parse(response);
                
                var item = data.RootElement.GetProperty("results")[0];
                var movieId = item.GetProperty("id").GetInt32();

                // Get detailed info including genres
                var detailsUrl = $"https://api.themoviedb.org/3/movie/{movieId}?api_key={tmdbKey}";
                var detailsResponse = await _httpClient.GetStringAsync(detailsUrl, ct);
                var details = JsonDocument.Parse(detailsResponse);

                var genres = details.RootElement.TryGetProperty("genres", out var genresArray)
                    ? genresArray.EnumerateArray().Select(g => g.GetProperty("name").GetString() ?? "").ToList()
                    : new List<string>();

                return new DiscoveryMedia
                {
                    Id = movieId.ToString(),
                    Title = item.GetProperty("title").GetString() ?? "",
                    Image = $"https://image.tmdb.org/t/p/original{item.GetProperty("backdrop_path").GetString()}",
                    Rating = item.TryGetProperty("vote_average", out var rating) ? rating.GetDouble() : 0,
                    Type = "movie",
                    IsNew = true,
                    Overview = item.TryGetProperty("overview", out var overview) ? overview.GetString() : "",
                    ReleaseDate = item.TryGetProperty("release_date", out var releaseDate) ? releaseDate.GetString() : "",
                    Genres = genres,
                    Runtime = details.RootElement.TryGetProperty("runtime", out var runtime) ? $"{runtime.GetInt32()}m" : ""
                };
            }
            catch
            {
                return null;
            }
        }

        private static string CalculateTimeAgo(string? publishedAt)
        {
            if (string.IsNullOrWhiteSpace(publishedAt) || !DateTime.TryParse(publishedAt, out var published))
                return "Recently";

            var timeSpan = DateTime.UtcNow - published;
            
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minutes ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hours ago";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} days ago";
            
            return published.ToString("MMM dd, yyyy");
        }

        private async Task<List<DiscoveryMedia>> GetRecentMovieTvForHeroAsync(string tmdbKey, CancellationToken ct)
        {
            // Future-ready config: swap from user settings when available
            const string defaultContentLanguage = "en";
            const string defaultRegion = "GB";
            const double minPopularity = 5.0;
            const int minVoteCount = 50;
            const int minHeroPoolSize = 10;

            var raw = new List<DiscoveryMedia>();

            if (string.IsNullOrWhiteSpace(tmdbKey))
            {
                Console.WriteLine("[DiscoveryMovieTv] tmdbKey missing \u2013 skip");
                AtlasAI.Core.AppLogger.LogInfo("[DiscoveryMovieTv] tmdbKey missing \u2013 skip");
                return raw;
            }

            Console.WriteLine($"[DiscoveryMovieTv] request begin recentWindowDays=10 lang={defaultContentLanguage} region={defaultRegion}");
            AtlasAI.Core.AppLogger.LogInfo($"[DiscoveryMovieTv] request begin recentWindowDays=10 lang={defaultContentLanguage} region={defaultRegion}");

            var today = DateTime.UtcNow.Date;
            var windowStart = today.AddDays(-10).ToString("yyyy-MM-dd");
            var windowEnd = today.ToString("yyyy-MM-dd");

            // Recent movies: English originals, popularity-sorted, GB region, vote threshold at API level
            try
            {
                var url = $"https://api.themoviedb.org/3/discover/movie?api_key={tmdbKey}&language=en-GB" +
                          $"&region={defaultRegion}&with_original_language={defaultContentLanguage}" +
                          $"&sort_by=popularity.desc&vote_count.gte=50&popularity.gte=5" +
                          $"&primary_release_date.gte={windowStart}&primary_release_date.lte={windowEnd}&page=1";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("results", out var items))
                    foreach (var item in items.EnumerateArray())
                    {
                        var media = ParseTmdbHeroItem(item, "movie");
                        if (media != null) raw.Add(media);
                    }
            }
            catch { }

            var rawMovieCount = raw.Count;
            Console.WriteLine($"[DiscoveryMovieTv] tmdb.movies.recent count={rawMovieCount}");
            AtlasAI.Core.AppLogger.LogInfo($"[DiscoveryMovieTv] tmdb.movies.recent count={rawMovieCount}");

            // Recent TV: English originals, popularity-sorted, vote threshold at API level
            try
            {
                var url = $"https://api.themoviedb.org/3/discover/tv?api_key={tmdbKey}&language=en-GB" +
                          $"&with_original_language={defaultContentLanguage}" +
                          $"&sort_by=popularity.desc&vote_count.gte=50&popularity.gte=5" +
                          $"&first_air_date.gte={windowStart}&first_air_date.lte={windowEnd}&page=1";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("results", out var items))
                    foreach (var item in items.EnumerateArray())
                    {
                        var media = ParseTmdbHeroItem(item, "tv");
                        if (media != null) raw.Add(media);
                    }
            }
            catch { }

            var rawTvCount = raw.Count - rawMovieCount;
            Console.WriteLine($"[DiscoveryMovieTv] tmdb.tv.recent count={rawTvCount}");
            AtlasAI.Core.AppLogger.LogInfo($"[DiscoveryMovieTv] tmdb.tv.recent count={rawTvCount}");

            // Upcoming movies: English originals, GB region
            var beforeUpcoming = raw.Count;
            try
            {
                var url = $"https://api.themoviedb.org/3/movie/upcoming?api_key={tmdbKey}&language=en-GB" +
                          $"&region={defaultRegion}&with_original_language={defaultContentLanguage}&page=1";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("results", out var items))
                    foreach (var item in items.EnumerateArray())
                    {
                        var media = ParseTmdbHeroItem(item, "movie");
                        if (media != null && !raw.Any(r => r.Id == media.Id))
                            raw.Add(media);
                    }
            }
            catch { }

            var rawUpcomingCount = raw.Count - beforeUpcoming;
            Console.WriteLine($"[DiscoveryMovieTv] tmdb.movies.upcoming count={rawUpcomingCount}");
            AtlasAI.Core.AppLogger.LogInfo($"[DiscoveryMovieTv] tmdb.movies.upcoming count={rawUpcomingCount}");

            // ── Quality filter (in-memory safety net after API-level filters) ────────────
            var originalCount = raw.Count;
            var removedLanguage = 0;
            var removedPopularity = 0;
            var removedVoteCount = 0;
            var kept = new List<DiscoveryMedia>();
            foreach (var item in raw)
            {
                if (!string.IsNullOrWhiteSpace(item.OriginalLanguage) &&
                    !string.Equals(item.OriginalLanguage, defaultContentLanguage, StringComparison.OrdinalIgnoreCase))
                { removedLanguage++; continue; }

                if (item.VoteCount > 0 && item.VoteCount < minVoteCount)
                { removedVoteCount++; continue; }

                if (item.Popularity > 0 && item.Popularity < minPopularity)
                { removedPopularity++; continue; }

                kept.Add(item);
            }

            var qualityLog = $"[DiscoveryMovieTv] quality.filtered original={originalCount} kept={kept.Count} removedLanguage={removedLanguage} removedPopularity={removedPopularity} removedVoteCount={removedVoteCount}";
            Console.WriteLine(qualityLog);
            AtlasAI.Core.AppLogger.LogInfo(qualityLog);

            // ── Supplement if pool is too small ──────────────────────────────────────────
            int suppTrendingMovie = 0, suppTrendingTv = 0, suppUpcoming = 0, suppPopularTv = 0;

            if (kept.Count < minHeroPoolSize)
            {
                static bool IsEnglish(DiscoveryMedia m, string lang) =>
                    string.IsNullOrWhiteSpace(m.OriginalLanguage) ||
                    string.Equals(m.OriginalLanguage, lang, StringComparison.OrdinalIgnoreCase);

                // Trending movies/week
                try
                {
                    var url = $"https://api.themoviedb.org/3/trending/movie/week?api_key={tmdbKey}&language=en-GB";
                    var response = await _httpClient.GetStringAsync(url, ct);
                    using var doc = JsonDocument.Parse(response);
                    if (doc.RootElement.TryGetProperty("results", out var items))
                        foreach (var item in items.EnumerateArray())
                        {
                            var media = ParseTmdbHeroItem(item, "movie");
                            if (media != null && IsEnglish(media, defaultContentLanguage) && !kept.Any(r => r.Id == media.Id))
                            { kept.Add(media); suppTrendingMovie++; }
                        }
                }
                catch { }

                // Trending TV/week
                try
                {
                    var url = $"https://api.themoviedb.org/3/trending/tv/week?api_key={tmdbKey}&language=en-GB";
                    var response = await _httpClient.GetStringAsync(url, ct);
                    using var doc = JsonDocument.Parse(response);
                    if (doc.RootElement.TryGetProperty("results", out var items))
                        foreach (var item in items.EnumerateArray())
                        {
                            var media = ParseTmdbHeroItem(item, "tv");
                            if (media != null && IsEnglish(media, defaultContentLanguage) && !kept.Any(r => r.Id == media.Id))
                            { kept.Add(media); suppTrendingTv++; }
                        }
                }
                catch { }

                // Upcoming movies (second page)
                try
                {
                    var url = $"https://api.themoviedb.org/3/movie/upcoming?api_key={tmdbKey}&language=en-GB&region={defaultRegion}&page=2";
                    var response = await _httpClient.GetStringAsync(url, ct);
                    using var doc = JsonDocument.Parse(response);
                    if (doc.RootElement.TryGetProperty("results", out var items))
                        foreach (var item in items.EnumerateArray())
                        {
                            var media = ParseTmdbHeroItem(item, "movie");
                            if (media != null && IsEnglish(media, defaultContentLanguage) && !kept.Any(r => r.Id == media.Id))
                            { kept.Add(media); suppUpcoming++; }
                        }
                }
                catch { }

                // Popular TV (English)
                try
                {
                    var url = $"https://api.themoviedb.org/3/tv/popular?api_key={tmdbKey}&language=en-GB&page=1";
                    var response = await _httpClient.GetStringAsync(url, ct);
                    using var doc = JsonDocument.Parse(response);
                    if (doc.RootElement.TryGetProperty("results", out var items))
                        foreach (var item in items.EnumerateArray())
                        {
                            var media = ParseTmdbHeroItem(item, "tv");
                            if (media != null && IsEnglish(media, defaultContentLanguage) && !kept.Any(r => r.Id == media.Id))
                            { kept.Add(media); suppPopularTv++; }
                        }
                }
                catch { }

                var suppLog = $"[DiscoveryMovieTv] quality.supplemented trendingMovie={suppTrendingMovie} trendingTv={suppTrendingTv} upcoming={suppUpcoming} popularTv={suppPopularTv}";
                Console.WriteLine(suppLog);
                AtlasAI.Core.AppLogger.LogInfo(suppLog);
            }

            // ── Sort by weighted score: popularity (weight 1) + vote_count (weight 0.05) + rating (weight 10) ─
            kept = kept
                .OrderByDescending(m => m.Popularity * 1.0 + m.VoteCount * 0.05 + m.Rating * 10.0)
                .ToList();

            // ── Attach trailers ──────────────────────────────────────────────────────────
            await AttachTrailerUrlsFromTmdbAsync(kept, tmdbKey, ct);
            var trailerCount = kept.Count(r => !string.IsNullOrWhiteSpace(r.TrailerUrl));
            Console.WriteLine($"[DiscoveryMovieTv] trailers count={trailerCount}");
            AtlasAI.Core.AppLogger.LogInfo($"[DiscoveryMovieTv] trailers count={trailerCount}");

            Console.WriteLine($"[DiscoveryMovieTv] mixed.total={kept.Count}");
            AtlasAI.Core.AppLogger.LogInfo($"[DiscoveryMovieTv] mixed.total={kept.Count}");

            return kept;
        }

        private static DiscoveryMedia? ParseTmdbHeroItem(JsonElement item, string type)
        {
            if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                return null;

            var id = idEl.GetInt32();
            if (id <= 0) return null;

            var title = string.Equals(type, "tv", StringComparison.OrdinalIgnoreCase)
                ? (item.TryGetProperty("name", out var nameEl) ? nameEl.GetString()?.Trim() ?? "" : "")
                : (item.TryGetProperty("title", out var titleEl) ? titleEl.GetString()?.Trim() ?? "" : "");
            if (string.IsNullOrWhiteSpace(title)) return null;

            var backdropPath = item.TryGetProperty("backdrop_path", out var bpEl) ? (bpEl.GetString() ?? "") : "";
            var posterPath = item.TryGetProperty("poster_path", out var ppEl) ? (ppEl.GetString() ?? "") : "";
            var backdropUrl = string.IsNullOrWhiteSpace(backdropPath) ? null : $"https://image.tmdb.org/t/p/w1280{backdropPath}";
            var posterUrl = string.IsNullOrWhiteSpace(posterPath) ? null : $"https://image.tmdb.org/t/p/w500{posterPath}";
            var image = backdropUrl ?? posterUrl ?? "";

            var releaseDate = string.Equals(type, "tv", StringComparison.OrdinalIgnoreCase)
                ? (item.TryGetProperty("first_air_date", out var fadEl) ? fadEl.GetString() : "")
                : (item.TryGetProperty("release_date", out var rdEl) ? rdEl.GetString() : "");

            var originalLanguage = item.TryGetProperty("original_language", out var olEl) ? (olEl.GetString() ?? "") : "";
            var popularity = item.TryGetProperty("popularity", out var popEl) && popEl.ValueKind == JsonValueKind.Number ? popEl.GetDouble() : 0.0;
            var voteCount = item.TryGetProperty("vote_count", out var vcEl) && vcEl.ValueKind == JsonValueKind.Number ? vcEl.GetInt32() : 0;

            return new DiscoveryMedia
            {
                Id = id.ToString(),
                Title = title,
                Image = image,
                BackdropUrl = backdropUrl,
                PosterUrl = posterUrl,
                Rating = item.TryGetProperty("vote_average", out var ratingEl) ? ratingEl.GetDouble() : 0,
                Type = type,
                IsNew = true,
                Overview = item.TryGetProperty("overview", out var overviewEl) ? overviewEl.GetString() : "",
                ReleaseDate = releaseDate,
                OriginalLanguage = originalLanguage,
                Popularity = popularity,
                VoteCount = voteCount,
            };
        }

        private async Task AttachTrailerUrlsFromTmdbAsync(List<DiscoveryMedia> items, string tmdbKey, CancellationToken ct)
        {
            if (items == null || items.Count == 0)
                return;
            if (string.IsNullOrWhiteSpace(tmdbKey))
                return;

            var candidates = items
                .Where(i => i != null)
                .Where(i => string.IsNullOrWhiteSpace(i.TrailerUrl))
                .Where(i => string.Equals(i.Type, "movie", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Type, "tv", StringComparison.OrdinalIgnoreCase))
                .Take(24)
                .ToList();

            foreach (var item in candidates)
            {
                if (ct.IsCancellationRequested)
                    break;

                if (!int.TryParse((item.Id ?? "").Trim(), out var tmdbId) || tmdbId <= 0)
                    continue;

                var mediaType = string.Equals(item.Type, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
                var trailer = await TryGetTmdbTrailerUrlAsync(mediaType, tmdbId, tmdbKey, ct);
                if (!string.IsNullOrWhiteSpace(trailer))
                    item.TrailerUrl = trailer;
            }
        }

        private async Task<string?> TryGetTmdbTrailerUrlAsync(string mediaType, int tmdbId, string tmdbKey, CancellationToken ct)
        {
            try
            {
                var videosUrl = $"https://api.themoviedb.org/3/{mediaType}/{tmdbId}/videos?api_key={tmdbKey}";
                var videosResponse = await _httpClient.GetStringAsync(videosUrl, ct);
                var videosData = JsonDocument.Parse(videosResponse);

                string? fallback = null;
                foreach (var video in videosData.RootElement.GetProperty("results").EnumerateArray())
                {
                    var site = video.TryGetProperty("site", out var siteEl) ? (siteEl.GetString() ?? "") : "";
                    if (!string.Equals(site, "YouTube", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var key = video.TryGetProperty("key", out var keyEl) ? (keyEl.GetString() ?? "").Trim() : "";
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var type = video.TryGetProperty("type", out var typeEl) ? (typeEl.GetString() ?? "") : "";
                    if (string.Equals(type, "Trailer", StringComparison.OrdinalIgnoreCase))
                        return $"https://www.youtube.com/watch?v={key}";

                    if (fallback == null && (string.Equals(type, "Teaser", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Clip", StringComparison.OrdinalIgnoreCase)))
                        fallback = $"https://www.youtube.com/watch?v={key}";
                }

                return fallback;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildYouTubeSearchUrl(string query)
        {
            var safe = (query ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(safe))
                return string.Empty;

            return $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(safe)}";
        }
    }

    public class DiscoveryData
    {
        public List<DiscoveryMedia> Trending { get; set; } = new();
        public List<DiscoveryMedia> Trailers { get; set; } = new();
        public List<DiscoveryNews> News { get; set; } = new();
        public List<DiscoveryMedia> Upcoming { get; set; } = new();
        public List<DiscoveryCelebrity> Celebrities { get; set; } = new();
        public DiscoveryMedia? Featured { get; set; }
        public List<DiscoveryMedia> HeroMovieTv { get; set; } = new();
        public string? Error { get; set; }
    }

    public class DiscoveryMedia
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Image { get; set; } = "";
        public double Rating { get; set; }
        public string Type { get; set; } = "";
        public bool IsNew { get; set; }
        public string? ReleaseDate { get; set; }
        public string? Overview { get; set; }
        public List<string>? Genres { get; set; }
        public string? Runtime { get; set; }
        public string? TrailerUrl { get; set; }
        public string? BackdropUrl { get; set; }
        public string? PosterUrl { get; set; }
        // Quality-filter fields (internal, not rendered in UI)
        public string? OriginalLanguage { get; set; }
        public double Popularity { get; set; }
        public int VoteCount { get; set; }
    }

    public class DiscoveryNews
    {
        public string Id { get; set; } = "";
        public string Headline { get; set; } = "";
        public string Image { get; set; } = "";
        public string Preview { get; set; } = "";
        public string TimeAgo { get; set; } = "";
        public bool Trending { get; set; }
        public string? Url { get; set; }
        public string Source { get; set; } = "";
    }

    public class DiscoveryCelebrity
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Image { get; set; } = "";
        public string Role { get; set; } = "";
        public bool Trending { get; set; }
        public string Category { get; set; } = "";
    }
}
