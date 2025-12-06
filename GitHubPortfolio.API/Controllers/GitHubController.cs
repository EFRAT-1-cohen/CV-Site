using Microsoft.AspNetCore.Mvc;
using GitHubPortfolio.Service;
using Octokit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

namespace GitHubPortfolio.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GitHubController : ControllerBase
    {
        private readonly IGitHubService _gitHubService;
        private readonly GitHubOptions _options;
        private readonly IMemoryCache _cache;

        public GitHubController(
            IGitHubService gitHubService,
            IOptions<GitHubOptions> options,
            IMemoryCache cache)
        {
            _gitHubService = gitHubService;
            _options = options.Value;
            _cache = cache;
        }

        // ===== בדיקת חיבור =====
        [HttpGet("test-connection")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                var user = await _gitHubService.GetUserAsync(_options.Username);
                return Ok(new
                {
                    message = "החיבור ל-GitHub עובד!",
                    username = user.Login,
                    name = user.Name,
                    publicRepos = user.PublicRepos
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = "שגיאה בחיבור ל-GitHub",
                    error = ex.Message,
                    hint = "בדקי את הטוקן וה-Username ב-User Secrets"
                });
            }
        }

        // ===== ניהול Cache =====
        [HttpPost("cache/clear")]
        public IActionResult ClearCache()
        {
            try
            {
                // ניקוי כל ה-cache
                if (_cache is MemoryCache memCache)
                {
                    memCache.Compact(1.0); // מנקה 100% מה-cache
                }

                return Ok(new { message = "Cache נוקה בהצלחה" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "שגיאה בניקוי Cache", error = ex.Message });
            }
        }

        [HttpGet("cache/stats")]
        public IActionResult GetCacheStats()
        {
            try
            {
                if (_cache is MemoryCache memCache)
                {
                    var stats = memCache.GetCurrentStatistics();
                    return Ok(new
                    {
                        currentEntryCount = stats?.CurrentEntryCount ?? 0,
                        currentEstimatedSize = stats?.CurrentEstimatedSize ?? 0,
                        message = "Cache statistics"
                    });
                }

                return Ok(new { message = "לא ניתן לשלוף נתוני cache" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ===== GetPortfolio - הפונקציה המרכזית =====
        [HttpGet("portfolio")]
        public async Task<IActionResult> GetPortfolio()
        {
            try
            {
                var repos = await _gitHubService.GetUserRepositoriesAsync(_options.Username);

                var portfolioItems = new List<object>();

                foreach (var repo in repos)
                {
                    try
                    {
                        // שליפת שפות פיתוח
                        var languages = await _gitHubService.GetRepositoryLanguagesAsync(_options.Username, repo.Name);

                        // שליפת קומיט אחרון
                        var commits = await _gitHubService.GetCommitsAsync(_options.Username, repo.Name, 1);
                        var lastCommit = commits.FirstOrDefault();

                        // שליפת Pull Requests
                        var pullRequests = await _gitHubService.GetPullRequestsAsync(_options.Username, repo.Name);

                        portfolioItems.Add(new
                        {
                            Name = repo.Name,
                            Description = repo.Description,
                            Url = repo.HtmlUrl,
                            Homepage = repo.Homepage, // קישור לאתר אם יש
                            Stars = repo.StargazersCount,
                            Forks = repo.ForksCount,
                            Languages = languages.Select(l => new
                            {
                                Name = l.Name,
                                Bytes = l.NumberOfBytes,
                                Percentage = Math.Round((double)l.NumberOfBytes / languages.Sum(x => x.NumberOfBytes) * 100, 2)
                            }).OrderByDescending(l => l.Bytes).ToList(),
                            LastCommit = lastCommit != null ? new
                            {
                                Message = lastCommit.Commit.Message,
                                Author = lastCommit.Commit.Author.Name,
                                Date = lastCommit.Commit.Author.Date,
                                Sha = lastCommit.Sha
                            } : null,
                            PullRequests = new
                            {
                                Total = pullRequests.Count,
                                Open = pullRequests.Count(pr => pr.State == ItemState.Open),
                                Closed = pullRequests.Count(pr => pr.State == ItemState.Closed)
                            },
                            CreatedAt = repo.CreatedAt,
                            UpdatedAt = repo.UpdatedAt,
                            IsPrivate = repo.Private
                        });
                    }
                    catch (Exception ex)
                    {
                        // אם יש שגיאה בשליפת מידע של repo מסוים, נמשיך לשאר
                        Console.WriteLine($"שגיאה בשליפת מידע עבור {repo.Name}: {ex.Message}");
                    }
                }

                return Ok(new
                {
                    Username = _options.Username,
                    TotalRepositories = repos.Count,
                    Repositories = portfolioItems.OrderByDescending(r => ((dynamic)r).Stars).ToList()
                });
            }
            catch (NotFoundException)
            {
                return NotFound(new { message = $"המשתמש '{_options.Username}' לא נמצא" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "שגיאה בשליפת Portfolio", error = ex.Message });
            }
        }

        // ===== GetPortfolio מהיר יותר (ללא נתונים מפורטים) =====
        [HttpGet("portfolio/basic")]
        public async Task<IActionResult> GetBasicPortfolio()
        {
            try
            {
                var repos = await _gitHubService.GetUserRepositoriesAsync(_options.Username);

                var basicItems = repos.Select(r => new
                {
                    r.Name,
                    r.Description,
                    r.HtmlUrl,
                    Homepage = r.Homepage,
                    r.Language,
                    Stars = r.StargazersCount,
                    Forks = r.ForksCount,
                    LastUpdate = r.UpdatedAt
                }).OrderByDescending(r => r.Stars).ToList();

                return Ok(new
                {
                    Username = _options.Username,
                    TotalRepositories = repos.Count,
                    Repositories = basicItems
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ===== קוד קיים =====
        [HttpGet("user/{username}")]
        public async Task<IActionResult> GetUser(string username)
        {
            try
            {
                var user = await _gitHubService.GetUserAsync(username);

                return Ok(new
                {
                    user.Login,
                    user.Name,
                    user.Bio,
                    user.AvatarUrl,
                    user.Followers,
                    user.Following,
                    PublicRepos = user.PublicRepos
                });
            }
            catch (NotFoundException)
            {
                return NotFound(new { message = $"המשתמש '{username}' לא נמצא" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("repositories/{username}")]
        public async Task<IActionResult> GetUserRepositories(
            string username,
            [FromQuery] int limit = 20)  // הגבלת מספר repositories
        {
            try
            {
                var repos = await _gitHubService.GetUserRepositoriesAsync(username);

                var repoList = repos
                    .OrderByDescending(r => r.StargazersCount)
                    .Take(limit)  // מגביל את מספר התוצאות
                    .Select(r => new
                    {
                        r.Name,
                        r.Description,
                        r.HtmlUrl,
                        r.Language,
                        Stars = r.StargazersCount,
                        Forks = r.ForksCount,
                        LastUpdate = r.UpdatedAt
                    }).ToList();

                return Ok(new
                {
                    Total = repos.Count,
                    Showing = repoList.Count,
                    Repositories = repoList
                });
            }
            catch (NotFoundException)
            {
                return NotFound(new { message = $"לא נמצאו repositories למשתמש '{username}'" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("repository/{owner}/{repoName}")]
        public async Task<IActionResult> GetRepository(string owner, string repoName)
        {
            try
            {
                var repo = await _gitHubService.GetRepositoryAsync(owner, repoName);

                return Ok(new
                {
                    repo.Name,
                    repo.Description,
                    repo.HtmlUrl,
                    repo.Language,
                    Stars = repo.StargazersCount,
                    Forks = repo.ForksCount
                });
            }
            catch (NotFoundException)
            {
                return NotFound(new { message = $"Repository '{owner}/{repoName}' לא נמצא" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchRepositories(
            [FromQuery] string term,
            [FromQuery] string? language = null,
            [FromQuery] string? user = null,
            [FromQuery] int page = 1,
            [FromQuery] int perPage = 20)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(term))
                {
                    return BadRequest(new { message = "חייב להזין מילת חיפוש" });
                }

                var result = await _gitHubService.SearchRepositoriesAsync(term, language, user, page, perPage);

                var repos = result.Items.Select(r => new
                {
                    r.Name,
                    r.Description,
                    r.HtmlUrl,
                    r.Language,
                    Stars = r.StargazersCount,
                    Owner = r.Owner.Login
                }).ToList();

                return Ok(new
                {
                    TotalCount = result.TotalCount,
                    Results = repos
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("commits/{owner}/{repoName}")]
        public async Task<IActionResult> GetCommits(
            string owner,
            string repoName,
            [FromQuery] int count = 10)  // שינוי ברירת המחדל ל-10 במקום 30
        {
            try
            {
                // הגבלה מקסימלית של 30 commits
                if (count > 30)
                {
                    count = 30;
                }

                var commits = await _gitHubService.GetCommitsAsync(owner, repoName, count);

                var commitList = commits.Select(c => new
                {
                    c.Sha,
                    Message = c.Commit.Message,
                    Author = c.Commit.Author.Name,
                    Date = c.Commit.Author.Date
                }).ToList();

                return Ok(new
                {
                    Count = commitList.Count,
                    Commits = commitList
                });
            }
            catch (NotFoundException)
            {
                return NotFound(new { message = $"Commits עבור '{owner}/{repoName}' לא נמצאו" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("languages/{owner}/{repoName}")]
        public async Task<IActionResult> GetLanguages(string owner, string repoName)
        {
            try
            {
                var languages = await _gitHubService.GetRepositoryLanguagesAsync(owner, repoName);

                var languageList = languages.Select(l => new
                {
                    l.Name,
                    Bytes = l.NumberOfBytes
                }).ToList();

                return Ok(languageList);
            }
            catch (NotFoundException)
            {
                return NotFound(new { message = $"שפות עבור '{owner}/{repoName}' לא נמצאו" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}