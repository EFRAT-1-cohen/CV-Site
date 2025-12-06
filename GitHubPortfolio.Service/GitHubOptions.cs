namespace GitHubPortfolio.Service
{
    public class GitHubOptions
    {
        public const string SectionName = "GitHub";

        public string Token { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string AppName { get; set; } = "github-portfolio-app";
    }
}