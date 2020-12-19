using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp;
using dotenv.net;
using Flurl.Http;
using Octokit;
using OpenQA.Selenium.Chrome;

namespace FUPA
{
    class Program
    {
        private static IDictionary _envDic;
        static async Task Main(string[] args)
        {
            
            DotEnv.Config();
            
            _envDic = Environment.GetEnvironmentVariables();

            var pattern = @"Current version: (r\d*-\d*-.*),";
            
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            Console.Write("Getting version from component repository... ");
            var content = await $"https://foobar2000.org/components/view/{_envDic["COMPONENT_SHORT_NAME"]}".GetStringAsync();
            var doc = await context.OpenAsync(req => req.Content(content));
            var vx = doc.QuerySelector("body > div.size.color.border.margin.content > div > h3:nth-child(4)").TextContent;

            var fbVersion = Regex.Matches(vx, pattern, RegexOptions.Multiline).First().Groups[1].Value;
            Console.WriteLine(fbVersion);
            Console.Write("Getting latest CI version... ");
            var builtVersion = (await "https://vgmstream-builds.s3-us-west-1.amazonaws.com/latest_ver".GetStringAsync()).Trim();
            Console.WriteLine(builtVersion);
            
            if (fbVersion != builtVersion)
            {
                Console.WriteLine("Versions don't match! Calling FUPA.");
                await DoFupa();
            }
            else
            {
                Console.WriteLine("Versions match! Doing nothing.");
                if ((string) _envDic["COMPONENT_SHORT_NAME"] == "foo_fupatest")
                {
                    Console.WriteLine("But we're debugging, so let's do it anyway");
                    await DoFupa();
                }
            }

        }

        private static async Task DoFupa()
        {
            var ok = new GitHubClient(new ProductHeaderValue("FUPA"));

            var crOp = new ChromeOptions();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) crOp.AddArguments("headless");
            var webDriver = new ChromeDriver(crOp);

            webDriver.Url = $"https://{_envDic["HTTP_USER"]}:{_envDic["HTTP_PASS"]}@foobar2000.org/componentsadmin";
            Console.WriteLine("Opening a `web browser´");

            var fldUser = webDriver.FindElementByName("name");
            var fldPass = webDriver.FindElementByName("password");
            fldUser.SendKeys(_envDic["FORM_USER"] as string);
            fldPass.SendKeys(_envDic["FORM_PASS"] as string);

            Console.WriteLine("Authenticating with the components repository");

            var btnSubmit = webDriver.FindElementByCssSelector("[type='submit']");
            btnSubmit.Click();

            var btnContinue = webDriver.FindElementByCssSelector("[type='submit']");
            btnContinue.Click();

            webDriver.Url = $"https://foobar2000.org/componentsadmin?action=add-release&component_id={_envDic["COMPONENT_ID"]}";

            Console.WriteLine("Getting latest commit...");
            var lastCommit = await ok.Repository.Commit.Get("vgmstream", "vgmstream", "HEAD");
            var lastBuild = await "https://vgmstream-builds.losno.co/latestdata".GetJsonAsync<VGB.VgbLatestData>();
            var lastVer = await "https://vgmstream-builds.s3-us-west-1.amazonaws.com/latest_ver".GetStringAsync();
            Console.WriteLine($"Downloading build {lastBuild.LatestCommitData.Sha}...");
            var dl =
                await
                    $"https://vgmstream-builds.s3-us-west-1.amazonaws.com/{lastBuild.LatestCommitData.Sha}/windows/foo_input_vgmstream.fb2k-component"
                        .DownloadFileAsync(Path.GetTempPath());
            Console.WriteLine("Filling form...");

            var fldVersion = webDriver.FindElementByName("version");
            var fldFile = webDriver.FindElementByName("file");
            var fldChangelog = webDriver.FindElementByName("release_info");
            var fldRequired = webDriver.FindElementByName("required_version");

            var sb = new StringBuilder();

            sb.AppendLine("* This release is automated.");
            sb.AppendLine();
            sb.AppendLine($@"* Built from commit {lastBuild.LatestCommitData.Sha}:");
            var lines = lastBuild.LatestCommitData.Commit.Message.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            );
            foreach (var line in lines)
            {
                sb.AppendLine($"  * {line}");
            }
            
            sb.AppendLine($"* {lastBuild.LatestCommitData.HtmlUrl}");
            sb.AppendLine($@"* Authored by {lastBuild.LatestCommitData.Author.Login} on {lastBuild.LatestCommitData.Commit.Author.Date:R}");
            
            fldVersion.SendKeys(lastVer.Trim());
            fldFile.SendKeys(dl);
            fldChangelog.SendKeys(sb.ToString());
            fldRequired.SendKeys("1.3");

            Console.WriteLine("Uploading update...");

            var btnAddRel = webDriver.FindElementByCssSelector("[type='submit'][value='Add release']");
            btnAddRel.Click();

            Console.WriteLine("Done!");
        }
    }
}