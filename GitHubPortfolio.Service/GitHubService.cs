using Octokit;
using Microsoft.Extensions.Options;

namespace GitHubPortfolio.Service
{
    public class GitHubService : IGitHubService
    {
        private readonly GitHubClient _client;
        private readonly GitHubOptions _options;

        public GitHubService(IOptions<GitHubOptions> options)
        {
            _options = options.Value;
            _client = new GitHubClient(new ProductHeaderValue(_options.AppName));

            // הזדהות עם Token אם קיים
            if (!string.IsNullOrEmpty(_options.Token))
            {
                _client.Credentials = new Credentials(_options.Token);
            }
        }

        public async Task<User> GetUserAsync(string userName)
        {
            return await _client.User.Get(userName);
        }

        public async Task<IReadOnlyList<Repository>> GetUserRepositoriesAsync(string userName)
        {
            return await _client.Repository.GetAllForUser(userName);
        }

        public async Task<Repository> GetRepositoryAsync(string owner, string repoName)
        {
            return await _client.Repository.Get(owner, repoName);
        }

        public async Task<SearchRepositoryResult> SearchRepositoriesAsync(
            string searchTerm,
            string? language = null,
            string? user = null,
            int page = 1,
            int perPage = 20)
        {
            var request = new SearchRepositoriesRequest(searchTerm)
            {
                Page = page,
                PerPage = perPage,
                SortField = RepoSearchSort.Stars,
                Order = SortDirection.Descending
            };

            if (!string.IsNullOrWhiteSpace(language))
            {
                var lang = ConvertToLanguageEnum(language);
                if (lang.HasValue)
                {
                    request.Language = lang.Value;
                }
            }

            if (!string.IsNullOrWhiteSpace(user))
            {
                request.User = user;
            }

            return await _client.Search.SearchRepo(request);
        }

        public async Task<IReadOnlyList<GitHubCommit>> GetCommitsAsync(string owner, string repoName, int count = 30)
        {
            var commits = await _client.Repository.Commit.GetAll(owner, repoName);
            return commits.Take(count).ToList();
        }

        public async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string owner, string repoName)
        {
            var prRequest = new PullRequestRequest
            {
                State = ItemStateFilter.All
            };

            return await _client.PullRequest.GetAllForRepository(owner, repoName, prRequest);
        }

        public async Task<IReadOnlyList<RepositoryLanguage>> GetRepositoryLanguagesAsync(string owner, string repoName)
        {
            return await _client.Repository.GetAllLanguages(owner, repoName);
        }

        private Language? ConvertToLanguageEnum(string languageName)
        {
            return languageName.ToLower() switch
            {
                "csharp" or "c#" or "cs" => Language.CSharp,
                "javascript" or "js" => Language.JavaScript,
                "python" or "py" => Language.Python,
                "java" => Language.Java,
                "typescript" or "ts" => Language.TypeScript,
                "ruby" or "rb" => Language.Ruby,
                "go" or "golang" => Language.Go,
                "swift" => Language.Swift,
                "kotlin" or "kt" => Language.Kotlin,
                "rust" or "rs" => Language.Rust,
                _ => null
            };
        }
    }
}