namespace CleanArchitecture.Web.AcceptanceTests;
[Category("Browser")]
public class CustomerOverviewTests
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    [OneTimeSetUp] public async Task StartBrowser()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_BASE_URL")))
            Assert.Ignore("Set TEST_BASE_URL to the published Development application before running browser tests.");
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Channel = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSER_CHANNEL")
        });
    }
    [OneTimeTearDown] public async Task StopBrowser()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
    [TestCase("CUST-001", "Complete")]
    [TestCase("CUST-WARN", "Partial data")]
    [TestCase("CUST-FAIL", "Unable to load customer")]
    public async Task DisplaysResultStatus(string id, string heading)
    {
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Environment.GetEnvironmentVariable("TEST_BASE_URL")!);
        await page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true }).ClickAsync();
        var session = await context.APIRequest.GetAsync(Environment.GetEnvironmentVariable("TEST_BASE_URL") + "/auth/me");
        Assert.That(session.Status, Is.EqualTo(200), "Development sign-in must establish a browser session.");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();
        await page.GetByLabel("Customer ID", new() { Exact = true }).FillAsync(id);
        await page.GetByRole(AriaRole.Button, new() { Name = "Look up", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true })).ToBeVisibleAsync();
        if (id == "CUST-WARN") await Assertions.Expect(page.GetByText("Unavailable", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
    }
}
