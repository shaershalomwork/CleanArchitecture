namespace CleanArchitecture.TestAppHost;
public class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);
        if (builder.Configuration["FixtureProvider"] == "Oracle")
            builder.AddOracle("fixture-oracle").WithImageTag("23.26.1.0-lite")
                .AddDatabase("customer-registry", "FREEPDB1");
        else
            builder.AddSqlServer("fixture-sql").AddDatabase("customer-registry");
        builder.Build().Run();
    }
}
