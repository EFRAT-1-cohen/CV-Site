using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Octokit;

namespace GitHubPortfolio.Service
{
    public class GitHubCachingService : IGitHubService
    {
        private readonly IGitHubService _innerService;
        private readonly IMemoryCache _cache;
        private readonly GitHubOptions _options;
        private readonly ILogger<GitHubCachingService> _logger;

        private const string PORTFOLIO_CACHE_KEY = "portfolio_data";
        private const string LAST_ACTIVITY_CACHE_KEY = "last_activity_date";
        private const int CACHE_DURATION_MINUTES = 10;

        public GitHubCachingService(
            IGitHubService innerService,
            IMemoryCache cache,
            IOptions<GitHubOptions> options,
            ILogger<GitHubCachingService> logger)
        {
            _innerService = innerService;
            _cache = cache;
            _options = options.Value;
            _logger = logger;
        }

        // ===== פונקציות ללא Cache (מידע דינמי) =====
        public Task<User> GetUserAsync(string userName)
        {
            return _innerService.GetUserAsync(userName);
        }

        public Task<Repository> GetRepositoryAsync(string owner, string repoName)
        {
            return _innerService.GetRepositoryAsync(owner, repoName);
        }

        public Task<SearchRepositoryResult> SearchRepositoriesAsync(
            string searchTerm,
            string? language = null,
            string? user = null,
            int page = 1,
            int perPage = 20)
        {
            return _innerService.SearchRepositoriesAsync(searchTerm, language, user, page, perPage);
        }

        // ===== GetUserRepositoriesAsync עם Cache חכם =====
        public async Task<IReadOnlyList<Repository>> GetUserRepositoriesAsync(string userName)
        {
            // אם זה לא המשתמש הראשי, לא משתמשים ב-cache
            if (userName != _options.Username)
            {
                return await _innerService.GetUserRepositoriesAsync(userName);
            }

            string cacheKey = $"repos_{userName}";

            // בדיקה אם יש פעילות חדשה
            bool hasNewActivity = await CheckForNewActivityAsync();

            if (hasNewActivity)
            {
                _logger.LogInformation("זוהתה פעילות חדשה - מנקה cache");
                _cache.Remove(cacheKey);
                _cache.Remove(LAST_ACTIVITY_CACHE_KEY);
            }

            // ניסיון לשלוף מ-cache
            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Repository>? cachedRepos))
            {
                _logger.LogInformation("מחזיר repositories מ-cache");
                return cachedRepos!;
            }

            // אין ב-cache - שולפים מ-API
            _logger.LogInformation("שולף repositories מ-GitHub API");
            var repos = await _innerService.GetUserRepositoriesAsync(userName);

            // שמירה ב-cache
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

            _cache.Set(cacheKey, repos, cacheOptions);

            return repos;
        }

        // ===== GetCommitsAsync עם Cache =====
        public async Task<IReadOnlyList<GitHubCommit>> GetCommitsAsync(
            string owner,
            string repoName,
            int count = 30)
        {
            // commits משתנים הרבה - cache קצר יותר
            string cacheKey = $"commits_{owner}_{repoName}_{count}";

            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<GitHubCommit>? cachedCommits))
            {
                _logger.LogInformation($"מחזיר commits מ-cache עבור {owner}/{repoName}");
                return cachedCommits!;
            }

            var commits = await _innerService.GetCommitsAsync(owner, repoName, count);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5)); // cache קצר יותר

            _cache.Set(cacheKey, commits, cacheOptions);

            return commits;
        }

        // ===== GetPullRequestsAsync עם Cache =====
        public async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(
            string owner,
            string repoName)
        {
            string cacheKey = $"prs_{owner}_{repoName}";

            // בדיקת פעילות חדשה
            bool hasNewActivity = await CheckForNewActivityAsync();
            if (hasNewActivity)
            {
                _cache.Remove(cacheKey);
            }

            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<PullRequest>? cachedPRs))
            {
                _logger.LogInformation($"מחזיר PRs מ-cache עבור {owner}/{repoName}");
                return cachedPRs!;
            }

            var prs = await _innerService.GetPullRequestsAsync(owner, repoName);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

            _cache.Set(cacheKey, prs, cacheOptions);

            return prs;
        }

        // ===== GetRepositoryLanguagesAsync עם Cache =====
        public async Task<IReadOnlyList<RepositoryLanguage>> GetRepositoryLanguagesAsync(
            string owner,
            string repoName)
        {
            // שפות משתנות רק כשמוסיפים קוד חדש
            string cacheKey = $"languages_{owner}_{repoName}";

            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<RepositoryLanguage>? cachedLangs))
            {
                _logger.LogInformation($"מחזיר languages מ-cache עבור {owner}/{repoName}");
                return cachedLangs!;
            }

            var languages = await _innerService.GetRepositoryLanguagesAsync(owner, repoName);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(30)); // cache ארוך יותר

            _cache.Set(cacheKey, languages, cacheOptions);

            return languages;
        }

        // ===== בדיקת פעילות חדשה (האתגר!) =====
        private async Task<bool> CheckForNewActivityAsync()
        {
            try
            {
                // בדיקה אם יש תאריך פעילות אחרון שמור
                if (!_cache.TryGetValue(LAST_ACTIVITY_CACHE_KEY, out DateTimeOffset lastCheckedActivity))
                {
                    // אין תאריך שמור - נחשיב שיש פעילות חדשה
                    await UpdateLastActivityDateAsync();
                    return true;
                }

                // שליפת הפעילות האחרונה מ-GitHub
                var currentActivity = await GetLatestActivityDateAsync();

                // אם הפעילות החדשה יותר מאוחרת מהשמורה - יש עדכון
                if (currentActivity > lastCheckedActivity)
                {
                    _logger.LogInformation(
                        $"זוהתה פעילות חדשה: {currentActivity} (שמור: {lastCheckedActivity})");
                    await UpdateLastActivityDateAsync();
                    return true;
                }

                _logger.LogInformation("אין פעילות חדשה - משתמש ב-cache");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "שגיאה בבדיקת פעילות - מניח שיש פעילות חדשה");
                return true; // במקרה של שגיאה, עדיף לשלוף מחדש
            }
        }

        private async Task<DateTimeOffset> GetLatestActivityDateAsync()
        {
            try
            {
                // שליפת הפעילות האחרונה של המשתמש
                var repos = await _innerService.GetUserRepositoriesAsync(_options.Username);

                if (!repos.Any())
                {
                    return DateTimeOffset.UtcNow;
                }

                // מציאת התאריך המאוחר ביותר מבין כל ה-repos
                DateTimeOffset latestUpdate = DateTimeOffset.MinValue;

                foreach (var repo in repos)
                {
                    var updateDate = repo.UpdatedAt;
                    if (updateDate > latestUpdate)
                    {
                        latestUpdate = updateDate;
                    }
                }

                // אם לא נמצא תאריך תקין, נחזיר את הזמן הנוכחי
                if (latestUpdate == DateTimeOffset.MinValue)
                {
                    return DateTimeOffset.UtcNow;
                }

                return latestUpdate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "שגיאה בשליפת תאריך פעילות אחרון");
                return DateTimeOffset.UtcNow;
            }
        }

        private async Task UpdateLastActivityDateAsync()
        {
            var latestActivity = await GetLatestActivityDateAsync();

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

            _cache.Set(LAST_ACTIVITY_CACHE_KEY, latestActivity, cacheOptions);
        }
    }
}