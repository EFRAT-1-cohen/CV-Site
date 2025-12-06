using Octokit;

namespace GitHubPortfolio.Service
{
    public interface IGitHubService
    {
        Task<User> GetUserAsync(string userName);
        Task<IReadOnlyList<Repository>> GetUserRepositoriesAsync(string userName);
        Task<Repository> GetRepositoryAsync(string owner, string repoName);
        Task<SearchRepositoryResult> SearchRepositoriesAsync(string searchTerm, string? language = null, string? user = null, int page = 1, int perPage = 20);
        Task<IReadOnlyList<GitHubCommit>> GetCommitsAsync(string owner, string repoName, int count = 30);
        Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string owner, string repoName);
        Task<IReadOnlyList<RepositoryLanguage>> GetRepositoryLanguagesAsync(string owner, string repoName);
    }
}