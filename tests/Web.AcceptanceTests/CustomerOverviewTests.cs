namespace CleanArchitecture.Web.AcceptanceTests;
[Category("Browser")]
public partial class CustomerOverviewTests
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _fixtures;
    private Dictionary<string, string>? _fixtureHeaders;
    private readonly List<string> _registeredCustomers = [];

    [SetUp] public async Task RegisterFixtures()
    {
        _registeredCustomers.Clear();
        _fixtures = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await _fixtures.NewPageAsync();
        await Login(page, "reader-writer");
        var csrf = await _fixtures.APIRequest.GetAsync(BaseUrl + "/auth/antiforgery");
        var json = await csrf.JsonAsync();
        _fixtureHeaders = new() { ["X-CSRF-TOKEN"] = json!.Value.GetProperty("token").GetString()! };
        foreach (var id in new[] { "CUST-001", "CUST-WARN" })
        {
            var response = await _fixtures.APIRequest.PostAsync(BaseUrl + "/api/customers", new()
            {
                Headers = _fixtureHeaders, DataObject = new { customerId = id, displayName = "Example Customer" }
            });
            Assert.That(response.Status, Is.EqualTo(200), await response.TextAsync());
            _registeredCustomers.Add(id);
        }
    }

    [TearDown] public async Task RemoveFixtures()
    {
        if (_fixtures is null) return;
        foreach (var id in _registeredCustomers)
            await _fixtures.APIRequest.DeleteAsync(BaseUrl + "/api/customers/" + id, new() { Headers = _fixtureHeaders });
        await _fixtures.DisposeAsync();
    }
    [OneTimeSetUp] public async Task StartBrowser()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_BASE_URL")))
            Assert.Ignore("Set TEST_BASE_URL to the published Development application before running browser tests.");
        if (!new Uri(BaseUrl).IsLoopback)
            throw new InvalidOperationException("Browser tests require a local disposable application.");
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
    [TestCase("CUST-FAIL", "Unable to complete request")]
    public async Task DisplaysResultStatus(string id, string heading)
    {
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Environment.GetEnvironmentVariable("TEST_BASE_URL")!);
        await page.Locator(".sign-in-card").GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = false }).ClickAsync();
        var session = await context.APIRequest.GetAsync(Environment.GetEnvironmentVariable("TEST_BASE_URL") + "/auth/me");
        Assert.That(session.Status, Is.EqualTo(200), "Development sign-in must establish a browser session.");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();
        await page.GetByLabel("Customer ID", new() { Exact = true }).FillAsync(id);
        await page.GetByRole(AriaRole.Button, new() { Name = "Look up", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true })).ToBeVisibleAsync();
        if (id == "CUST-WARN") await Assertions.Expect(page.GetByText("Unavailable", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".sign-in-card").GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = false })).ToBeVisibleAsync();
    }
}
