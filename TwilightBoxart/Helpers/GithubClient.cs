using System;
using System.Net.Http;
using System.Runtime.Serialization;
using Utf8Json;

namespace TwilightBoxart.Helpers
{
    public static class GithubClient
    {
        public static GithubRelease GetNewRelease(string repoPath, Version currentVersion)
        {
            var releasesUrl = $"https://api.github.com/repos/{repoPath}/releases/latest";


            using (var httpClientHandler = new HttpClientHandler())
            {
                // This is BAD practice but needed for some users. Don't use this for production apps. ;)
                httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true; 
                using (var client = new HttpClient(httpClientHandler))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("TwilightBoxart");
                    var json = client.GetStringAsync(releasesUrl).Result;
                    var latest = JsonSerializer.Deserialize<GithubRelease>(json);

                    if (latest?.Version > currentVersion)
                    {
                        var text = latest.Body;
                        if (text.Contains("---"))
                        {
                            try
                            {
                                // Use shorttext.
                                text = latest.Body.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                            }
                            catch { }
                        }

                        latest.UpdateText = $"A new update is available! (v{latest.VersionStr})" + Environment.NewLine + Environment.NewLine +
                                            "Release notes:" + Environment.NewLine +
                                            text + Environment.NewLine + Environment.NewLine +
                                            "Visit Github now?";

                        return latest;
                    }

                    return null;
                }
            }
        }
    }

    public class GithubRelease
    {
        [DataMember(Name = "tag_name")]
        public string VersionStr { get; set; }
        [DataMember(Name = "body")]
        public string Body { get; set; }
        [DataMember(Name = "prerelease")]
        public bool PreRelease { get; set; }
        public string UpdateText { get; set; }

        public Version Version
        {
            get
            {
                try
                {
                    return new Version(VersionStr);
                }
                catch
                {
                    return new Version(0, 0);
                }
            }
        }
    }
}