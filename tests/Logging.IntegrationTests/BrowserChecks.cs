using Microsoft.Playwright;
using NUnit.Framework;

namespace CleanArchitecture.Logging.IntegrationTests;

internal static class BrowserChecks
{
    public static async Task Dashboard(Uri endpoint, string token, IEnumerable<string>? resources = null, bool expectTrace = true)
    {
        var stage = "opening the dashboard";
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSER_CHANNEL"), Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(new Uri(endpoint, "/login?t=" + Uri.EscapeDataString(token)).AbsoluteUri);
            stage = "opening Resources";
            await page.GotoAsync(new Uri(endpoint, "/").AbsoluteUri);
            foreach (var resource in resources ?? ["elasticsearch", "elasticsearch-init", "otel-collector", "kibana", "kibana-init", "webapi"])
            {
                stage = "locating resource " + resource;
                await Assertions.Expect(page.GetByText(resource, new() { Exact = true }).First).ToBeVisibleAsync(new() { Timeout = 30000 });
            }
            if (expectTrace)
            {
                stage = "opening Traces";
                await page.GetByRole(AriaRole.Link, new() { Name = "Traces", Exact = true }).ClickAsync();
                stage = "finding the request trace";
                await Assertions.Expect(page.GetByText("GET /api/customers", new() { Exact = false }).First).ToBeVisibleAsync(new() { Timeout = 30000 });
            }
        }
        catch (PlaywrightException error) { throw new InvalidOperationException($"Dashboard acceptance failed while {stage}: {error.Message.Replace(token, "[redacted]", StringComparison.Ordinal)}"); }
    }

    public static async Task Kibana(Uri endpoint, string password, string trace)
    {
        try { await CheckKibana(endpoint, password, trace); }
        catch (PlaywrightException) { throw new InvalidOperationException("Kibana browser acceptance failed; inspect login and Discover readiness. Browser diagnostics are suppressed to protect credentials."); }
    }

    private static async Task CheckKibana(Uri endpoint, string password, string trace)
    {
        Assertions.SetDefaultExpectTimeout(60000);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSER_CHANNEL"), Headless = true
        });
        // Use platform trust; local acceptance must never bypass HTTPS errors.
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60000);
        await page.GotoAsync(new Uri(endpoint, "/login").AbsoluteUri);
        await page.GetByLabel("Username", new() { Exact = true }).FillAsync("elastic");
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/app/**");
        var state = $"(dataSource:(dataViewId:application-logs,type:dataView),query:(language:kuery,query:'attributes.CorrelationId : \"{trace}\"'))";
        await page.GotoAsync(new Uri(endpoint, "/app/discover#/?_a=" + Uri.EscapeDataString(state)).AbsoluteUri);
        try
        {
            await Assertions.Expect(page.GetByText("Application logs", new() { Exact = true }).First).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("HTTP request completed", new() { Exact = false }).First).ToBeVisibleAsync();
        }
        catch (PlaywrightException)
        {
            var diagnostics = Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "logging");
            Directory.CreateDirectory(diagnostics);
            await page.ScreenshotAsync(new() { Path = Path.Combine(diagnostics, "kibana-discover.png") });
            await File.WriteAllTextAsync(Path.Combine(diagnostics, "kibana-discover.txt"), await page.Locator("body").InnerTextAsync());
            throw;
        }
    }
}

[Category("Browser")]
public class GuideTests
{
    [Test]
    public async Task OfflineHebrewGuideSupportsSearchKeyboardAndNarrowScreens()
    {
        if (Environment.GetEnvironmentVariable("RUN_LOGGING_BROWSER_TESTS") != "1") Assert.Ignore("Opt-in logging browser checks.");
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "docs/logging-aspire.he.html"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSER_CHANNEL"), Headless = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var requests = new List<string>();
        page.Request += (_, request) => { if (request.Url.StartsWith("http", StringComparison.Ordinal)) requests.Add(request.Url); };
        await page.GotoAsync(new Uri(Path.Combine(root!.FullName, "docs/logging-aspire.he.html")).AbsoluteUri);
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "he");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
        await page.Locator("#search").FillAsync("תעודה");
        Assert.That(await page.Locator("section[hidden]").CountAsync(), Is.GreaterThan(0));
        await page.Locator("#search").FillAsync("");
        await page.Locator("summary").First.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("details").First).ToHaveAttributeAsync("open", "");
        await page.EvaluateAsync("Object.defineProperty(navigator,'clipboard',{value:undefined,configurable:true})");
        await page.Locator(".copy").First.ClickAsync();
        await Assertions.Expect(page.Locator("#copy-status")).ToHaveTextAsync("הפקודה הועתקה");
        Assert.That(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"), Is.True);
        Assert.That(requests, Is.Empty);
    }
}
