namespace CleanArchitecture.Application.Customers.Commands;

internal static class CustomerCommandRules
{
    public static void CustomerId<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithErrorCode("CUSTOMER.INVALID_ID")
            .MaximumLength(50).WithErrorCode("CUSTOMER.INVALID_ID")
            .Matches("^[A-Za-z0-9-]+$").WithErrorCode("CUSTOMER.INVALID_ID");

    public static void DisplayName<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().WithErrorCode("CUSTOMER.INVALID_DISPLAY_NAME")
            .MaximumLength(200).WithErrorCode("CUSTOMER.INVALID_DISPLAY_NAME");
}
