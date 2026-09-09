namespace CleanArchitecture.TestAppHost;
public class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);
        builder.AddSqlServer("fixture-sql").AddDatabase("customer-registry");
        builder.Build().Run();
    }
}
