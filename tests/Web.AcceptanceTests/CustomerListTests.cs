namespace CleanArchitecture.Web.AcceptanceTests;

public partial class CustomerOverviewTests
{
    [Test] public async Task CustomerListReflectsTheRegistrationLifecycle()
    {
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
        var page = await context.NewPageAsync();
        await Login(page, "reader-writer");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Registered customers", Exact = true })).ToBeVisibleAsync();
        var id = "LIST-" + Guid.NewGuid().ToString("N");
        await page.GetByRole(AriaRole.Link, new() { Name = "Create customer", Exact = true }).ClickAsync();
        await page.GetByLabel("Customer ID", new() { Exact = true }).FillAsync(id);
        await page.GetByLabel("Display name", new() { Exact = true }).FillAsync("List customer");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create customer", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".receipt")).ToContainTextAsync("Customer created");
        await page.GetByRole(AriaRole.Link, new() { Name = "All customers", Exact = true }).ClickAsync();
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = id });
        await Assertions.Expect(row).ToContainTextAsync("List customer");
        await row.GetByRole(AriaRole.Link).ClickAsync();
        await Assertions.Expect(page.Locator(".profile-card")).ToContainTextAsync("List customer");
        await page.GetByRole(AriaRole.Link, new() { Name = "Change name", Exact = true }).ClickAsync();
        await page.GetByLabel("Display name", new() { Exact = true }).FillAsync("Updated list customer");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".receipt")).ToContainTextAsync("Updated list customer");
        await page.GetByRole(AriaRole.Link, new() { Name = "All customers", Exact = true }).ClickAsync();
        await Assertions.Expect(row).ToContainTextAsync("Updated list customer");
        await Snapshot(page, "customer-list-desktop");
        await page.SetViewportSizeAsync(375, 812);
        Assert.That(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"), Is.True);
        await Snapshot(page, "customer-list-mobile");
        await page.GotoAsync(BaseUrl + "/customers/manage?customerId=" + id + "&mode=delete");
        await page.GetByRole(AriaRole.Button, new() { Name = "Review deletion", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete customer", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".receipt")).ToContainTextAsync("Customer deleted");
        await page.GetByRole(AriaRole.Link, new() { Name = "All customers", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("tbody")).ToBeVisibleAsync();
        await Assertions.Expect(row).ToHaveCountAsync(0);
        await page.GotoAsync(BaseUrl + "/customers/" + id + "/overview");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "No customer found", Exact = true })).ToBeVisibleAsync();
    }

    [Test] public async Task ListEmptyAndErrorStatesAreDistinctAndReadersCannotCreate()
    {
        await using var context = await _browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/customers", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = """{"status":"Success","data":[],"issues":[],"correlationId":"fixture"}"""
        }));
        await Login(page);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "No customers yet", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Create customer", Exact = true })).ToHaveCountAsync(0);
        await page.UnrouteAsync("**/api/customers");
        await page.RouteAsync("**/api/customers", route => route.FulfillAsync(new()
        {
            Status = 503, ContentType = "application/json", Body = """{"status":"Error","data":null,"issues":[{"code":"CUSTOMER.UNAVAILABLE","message":"Registry unavailable"}],"correlationId":"fixture"}"""
        }));
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh customers", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("app-outcome")).ToContainTextAsync("Registry unavailable");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "No customers yet", Exact = true })).ToHaveCountAsync(0);
        var denied = await context.APIRequest.PostAsync(BaseUrl + "/api/customers", new() { DataObject = new { customerId = "DENIED", displayName = "Denied" } });
        Assert.That(denied.Status, Is.EqualTo(403));
    }
}
