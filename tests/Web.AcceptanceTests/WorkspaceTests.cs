namespace CleanArchitecture.Web.AcceptanceTests;

public partial class CustomerOverviewTests
{
    private static string BaseUrl => Environment.GetEnvironmentVariable("TEST_BASE_URL")!;
    private static async Task Login(IPage page, string profile = "reader")
    {
        await page.GotoAsync(BaseUrl);
        await page.GetByLabel("Access profile", new() { Exact = true }).SelectOptionAsync(profile);
        await page.Locator(".sign-in-card a.button").ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true })).ToBeVisibleAsync();
    }
    private static async Task Snapshot(IPage page, string name)
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new() { Path = path, FullPage = true, Animations = ScreenshotAnimations.Disabled });
        TestContext.AddTestAttachment(path);
    }
    [TestCase("reader", true, false)]
    [TestCase("writer", false, true)]
    [TestCase("reader-writer", true, true)]
    [TestCase("no-access", false, false)]
    public async Task NavigationAndDirectRoutesRespectCapabilities(string profile, bool read, bool write)
    {
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
        var page = await context.NewPageAsync(); await Login(page, profile);
        var navigation = page.GetByRole(AriaRole.Navigation, new() { Name = "Main navigation" });
        await Assertions.Expect(navigation.GetByRole(AriaRole.Link, new() { Name = "Customers", Exact = true })).ToHaveCountAsync(read ? 1 : 0);
        await Assertions.Expect(navigation.GetByRole(AriaRole.Link, new() { Name = "Manage customers", Exact = true })).ToHaveCountAsync(write ? 1 : 0);
        await page.GotoAsync(BaseUrl + "/customers/manage");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = write ? "Manage customers" : "Access not available", Exact = true })).ToBeVisibleAsync();
        await page.GotoAsync(BaseUrl + "/customers/CUST-001/overview");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = read ? "Complete" : "Access not available", Exact = true })).ToBeVisibleAsync();
        var api = await context.APIRequest.GetAsync(BaseUrl + "/api/customers/CUST-001/overview");
        Assert.That(api.Status, Is.EqualTo(read ? 200 : 403));
        await page.GotoAsync(BaseUrl + "/account");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Your account", Exact = true })).ToBeVisibleAsync();
        await Snapshot(page, "account-" + profile);
    }
    [Test] public async Task WriterCompletesAllMutationsWithoutReadingCustomers()
    {
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync(); var reads = new List<string>();
        page.Request += (_, request) => { if (request.Url.Contains("/api/customers") && request.Method == "GET") reads.Add(request.Url); };
        await Login(page, "writer");
        await page.GetByRole(AriaRole.Link, new() { Name = "Create customer", Exact = false }).ClickAsync();
        var id = "UI-" + Guid.NewGuid().ToString("N");
        await page.GetByLabel("Customer ID", new() { Exact = true }).FillAsync(id);
        await page.GetByLabel("Display name", new() { Exact = true }).FillAsync("UI Customer");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create customer", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".receipt")).ToContainTextAsync("Customer created");
        await page.GotoAsync(BaseUrl + "/customers/manage?customerId=" + id);
        await page.GetByLabel("Display name", new() { Exact = true }).FillAsync("Renamed customer");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".receipt")).ToContainTextAsync("Renamed customer");
        await page.GetByRole(AriaRole.Button, new() { Name = "Edit details", Exact = true }).ClickAsync();
        await page.GetByLabel("Display name", new() { Exact = true }).FillAsync("Replaced customer");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".receipt")).ToContainTextAsync("Replaced customer");
        await Snapshot(page, "writer-success");
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Review deletion", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete customer", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".receipt")).ToContainTextAsync("Customer deleted");
        Assert.That(reads, Is.Empty, "Writer-only workflows must never fetch a customer list or overview.");
    }
    [Test] public async Task ValidationConflictAndUnknownOutcomeKeepUserInput()
    {
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync(); await Login(page, "reader-writer");
        await page.GotoAsync(BaseUrl + "/customers/create");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create customer", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByLabel("Customer ID", new() { Exact = true })).ToHaveAttributeAsync("aria-invalid", "true");
        await page.GetByLabel("Customer ID", new() { Exact = true }).FillAsync("CUST-001");
        await page.GetByLabel("Display name", new() { Exact = true }).FillAsync("Duplicate");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create customer", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("app-outcome")).ToContainTextAsync("CUSTOMER.CONFLICT");
        await Assertions.Expect(page.GetByLabel("Display name", new() { Exact = true })).ToHaveValueAsync("Duplicate");
        await page.GotoAsync(BaseUrl + "/customers/manage?customerId=CUST-FAIL");
        await page.GetByLabel("Display name", new() { Exact = true }).FillAsync("Uncertain change");
        var writes = 0; page.Request += (_, request) => { if (request.Method == "PATCH") writes++; };
        await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("app-outcome")).ToContainTextAsync("CUSTOMER.OUTCOME_UNKNOWN");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Check customer status", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Display name", new() { Exact = true })).ToHaveValueAsync("Uncertain change");
        Assert.That(writes, Is.EqualTo(1));
        await Snapshot(page, "unknown-write-outcome");
    }
    [Test] public async Task ThemesResponsiveNavigationAndStatusWork()
    {
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true, ColorScheme = ColorScheme.Dark, ViewportSize = new() { Width = 1440, Height = 1000 } });
        var page = await context.NewPageAsync(); await page.GotoAsync(BaseUrl);
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        await Snapshot(page, "sign-in-dark");
        await Login(page, "reader-writer");
        await page.GetByLabel("Customer ID", new() { Exact = true }).FillAsync("CUST-WARN");
        await page.GetByRole(AriaRole.Button, new() { Name = "Look up", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Partial data", Exact = true })).ToBeVisibleAsync();
        Assert.That(await page.GetByLabel("Customer ID", new() { Exact = true }).EvaluateAsync<double>("element => element.getBoundingClientRect().width"), Is.GreaterThan(180));
        Assert.That(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"), Is.True);
        await Snapshot(page, "overview-dark");
        await page.GetByLabel("Theme", new() { Exact = true }).SelectOptionAsync("light");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Partial data", Exact = true })).ToBeVisibleAsync();
        await Snapshot(page, "overview-light");
        await page.SetViewportSizeAsync(375, 812);
        await page.GetByRole(AriaRole.Button, new() { Name = "Open navigation", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Service status", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Service status", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".status-value").First).ToHaveTextAsync("Healthy");
        Assert.That(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"), Is.True);
        await Snapshot(page, "status-mobile");
        await page.RouteAsync("**/health", route => route.FulfillAsync(new() { Status = 200, ContentType = "text/plain", Body = "Degraded" }));
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh status", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".status-value").Last).ToHaveTextAsync("Degraded");
    }
    [Test] public async Task UnknownApiRoutesRemainApiErrors()
    {
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var response = await context.APIRequest.GetAsync(BaseUrl + "/api/unknown-route");
        Assert.That(response.Status, Is.EqualTo(404));
        Assert.That(response.Headers["content-type"], Does.Contain("application/json"));
    }
    [Test] public async Task ScalarSendsBearerForACustomerMutation()
    {
        var token = Environment.GetEnvironmentVariable("TEST_WRITER_TOKEN");
        if (string.IsNullOrWhiteSpace(token)) Assert.Ignore("The template verifier supplies a CLI-issued writer token.");
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();
        await page.GotoAsync(BaseUrl + "/scalar");
        await page.GetByPlaceholder("Token", new() { Exact = true }).First.FillAsync(token!);
        await page.GetByRole(AriaRole.Button, new() { Name = "Open Group - Customers", Exact = true }).ClickAsync();
        await page.Locator("a[href='#tag/customers/POST/api/customers']").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Test Request (post /api/customers)", Exact = true }).ClickAsync();
        var id = "SCALAR-" + Guid.NewGuid().ToString("N");
        var editor = page.Locator("[contenteditable=true][role=textbox]").Last;
        await editor.ClickAsync();
        await editor.PressAsync("ControlOrMeta+a");
        await page.Keyboard.InsertTextAsync(System.Text.Json.JsonSerializer.Serialize(new { customerId = id, displayName = "Scalar customer" }));
        await editor.PressAsync("Tab");
        // Scalar debounces synchronization from its JSON editor to the request model.
        await page.WaitForTimeoutAsync(1000);
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Send post request to /api/customers", Exact = false }).ClickAsync(),
            response => response.Url == BaseUrl + "/api/customers" && response.Request.Method == "POST");
        Assert.That(response.Request.PostData, Does.Contain(id), "The edited Scalar request body must be submitted.");
        Assert.That(response.Status, Is.EqualTo(200), await response.TextAsync());
        var headers = await response.Request.AllHeadersAsync();
        Assert.That(headers.TryGetValue("authorization", out var authorization) && authorization == "Bearer " + token, Is.True, "Scalar must send the configured Bearer token.");
        Assert.That(headers.ContainsKey("x-csrf-token"), Is.False);
        var cleanup = await context.APIRequest.DeleteAsync(BaseUrl + "/api/customers/" + id, new() { Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token } });
        Assert.That(cleanup.Status, Is.EqualTo(200));
    }
}
